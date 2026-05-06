using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UniGetUI.Core.Logging;
using UniGetUI.Interface.Enums;
using UniGetUI.PackageEngine.Interfaces;
using UniGetUI.PackageEngine.PackageLoader;

namespace UniGetUI.PhisonService
{
    /// <summary>
    /// 監控套件狀態並更新燈號顯示
    /// </summary>
    public class PackageStatusMonitor : IDisposable
    {
        // 靜態快取：記錄所有套件的當前狀態（key: Package.Id, value: StartupStatusLight）
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, StartupStatusLight> _statusCache 
            = new System.Collections.Concurrent.ConcurrentDictionary<string, StartupStatusLight>();
        
        private readonly IPackage _package;
        private CancellationTokenSource? _statusUpdateCancellationTokenSource;
        private StartupStatusLight _currentStatus = StartupStatusLight.Gray;
        
        /// <summary>
        /// 狀態改變事件
        /// </summary>
        public event Action<StartupStatusLight>? StatusChanged;

        /// <summary>
        /// 當前狀態
        /// </summary>
        public StartupStatusLight CurrentStatus
        {
            get => _currentStatus;
            private set
            {
                if (_currentStatus != value)
                {
                    var previousStatus = _currentStatus;
                    _currentStatus = value;
                    
                    // 更新靜態快取
                    _statusCache[_package.Id] = _currentStatus;
                    
                    StatusChanged?.Invoke(_currentStatus);
                    
                    // 當狀態從 Green 轉為 Gray 時，檢查是否需要關閉 aiDAPTIV Service
                    if (previousStatus == StartupStatusLight.Green && _currentStatus == StartupStatusLight.Gray)
                    {
                        _ = CheckAndStopAiDAPTIVServiceAsync();
                    }
                }
            }
        }

        public PackageStatusMonitor(IPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            
            // 初始化快取狀態為灰燈
            _statusCache[_package.Id] = StartupStatusLight.Gray;
        }

        /// <summary>
        /// 開始監控狀態，每秒偵測一次
        /// </summary>
        public void StartMonitoring()
        {
            if (_statusUpdateCancellationTokenSource != null)
            {
                // 已經在監控中
                return;
            }

            _statusUpdateCancellationTokenSource = new CancellationTokenSource();
            _ = UpdateStatusLoopAsync(_statusUpdateCancellationTokenSource.Token);
        }

        /// <summary>
        /// 停止監控
        /// </summary>
        public void StopMonitoring()
        {
            _statusUpdateCancellationTokenSource?.Cancel();
            _statusUpdateCancellationTokenSource?.Dispose();
            _statusUpdateCancellationTokenSource = null;
        }

        /// <summary>
        /// 持續循環更新狀態，每秒偵測一次
        /// </summary>
        private async Task UpdateStatusLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await UpdateStatusAsync();
                    // 等待 1 秒後再次偵測
                    await Task.Delay(1000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，退出循環
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Exception in status update loop for package {_package.Id}: {ex.Message}");
                    Logger.Error(ex);
                    // 發生錯誤時也等待 1 秒再重試
                    try
                    {
                        await Task.Delay(1000, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 從 status.ps1 獲取套件狀態並更新燈號
        /// </summary>
        private async Task UpdateStatusAsync()
        {
            // 使用 ServiceManager 獲取狀態值
            int? status = await ServiceManager.GetPackageStatusAsync(_package);
            
            // 根據狀態值判斷燈號
            CurrentStatus = GetStatusLightFromStatus(status);
        }

        /// <summary>
        /// 根據 status 值判斷對應的燈號狀態
        /// </summary>
        /// <param name="status">status 值（1 = 執行中，其他 = 沒有執行中），null 表示無法獲取狀態</param>
        /// <returns>對應的燈號狀態</returns>
        private static StartupStatusLight GetStatusLightFromStatus(int? status)
        {
            if (status == null)
            {
                // 無法獲取狀態 = 灰燈（沒有執行中）
                return StartupStatusLight.Gray;
            }

            // status 1 = 執行中（綠燈），其他值 = 沒有執行中（灰燈）
            return status == 1 ? StartupStatusLight.Green : StartupStatusLight.Gray;
        }

        /// <summary>
        /// 檢查是否還有其他 aiDAPTIV 套件在運行，如果沒有則關閉 aiDAPTIV Service
        /// </summary>
        private async Task CheckAndStopAiDAPTIVServiceAsync()
        {
            try
            {
                // 只有 aiDAPTIV-bucket 的套件才需要處理
                if (!ServiceManager.IsFromValidBucket(_package))
                {
                    return;
                }

                Logger.Info($"Package {_package.Id} status changed from Green to Gray, checking if aiDAPTIV Service should be stopped");

                // 檢查是否還有其他 aiDAPTIV 套件在運行
                int runningCount = await CountRunningAiDAPTIVAppsAsync();

                if (runningCount > 0)
                {
                    Logger.Info($"{runningCount} aiDAPTIV app(s) still running, keeping services alive");
                    return;
                }

                // 沒有任何 aiDAPTIV 套件在運行，檢查服務狀態
                Logger.Info("No aiDAPTIV apps running, checking aiDAPTIV Service status...");

                var llamaStatus = await aiDAPTIVToolService.aiDAPTIVStatus();
                var embeddingStatus = await aiDAPTIVToolService.aiDAPTIVEmbeddingStatus();

                bool llamaRunning = llamaStatus?.Ok == true && llamaStatus.Running;
                bool embeddingRunning = embeddingStatus?.Ok == true && embeddingStatus.Running;

                if (!llamaRunning && !embeddingRunning)
                {
                    Logger.Info("aiDAPTIV Services are already stopped, no action needed");
                    return;
                }

                // 服務還在運行，需要關閉
                Logger.Info("aiDAPTIV Services are running but no apps are using them, stopping services...");

                if (llamaRunning)
                {
                    Logger.Info("Stopping aiDAPTIV Llama service...");
                    var (success, errorMessage) = await aiDAPTIVToolService.aiDAPTIVStop();
                    if (!success)
                    {
                        Logger.Error($"Failed to stop aiDAPTIV Llama service: {errorMessage}");
                    }
                    else
                    {
                        Logger.Info("aiDAPTIV Llama service stopped successfully");
                    }
                }

                if (embeddingRunning)
                {
                    Logger.Info("Stopping aiDAPTIV Embedding service...");
                    var (embeddingSuccess, embeddingErrorMessage) = await aiDAPTIVToolService.aiDAPTIVEmbeddingStop();
                    if (!embeddingSuccess)
                    {
                        Logger.Error($"Failed to stop aiDAPTIV Embedding service: {embeddingErrorMessage}");
                    }
                    else
                    {
                        Logger.Info("aiDAPTIV Embedding service stopped successfully");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception in CheckAndStopAiDAPTIVServiceAsync for package {_package.Id}: {ex.Message}");
                Logger.Error(ex);
            }
        }

        /// <summary>
        /// 計算目前有多少個 aiDAPTIV 套件正在運行（使用快取的狀態，避免重複執行 status.ps1）
        /// </summary>
        /// <returns>運行中的套件數量</returns>
        private Task<int> CountRunningAiDAPTIVAppsAsync()
        {
            try
            {
                var allPackages = InstalledPackagesLoader.Instance.Packages;
                
                // 從快取中讀取狀態，避免重複執行 status.ps1
                int runningCount = allPackages
                    .Where(ServiceManager.IsFromValidBucket)
                    .Count(pkg => _statusCache.TryGetValue(pkg.Id, out var status) && status == StartupStatusLight.Green);

                Logger.Debug($"Counted {runningCount} running aiDAPTIV apps from status cache");
                return Task.FromResult(runningCount);
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception when counting running aiDAPTIV apps: {ex.Message}");
                return Task.FromResult(0);
            }
        }

        public void Dispose()
        {
            StopMonitoring();
            
            // 從快取中移除此套件的狀態
            _statusCache.TryRemove(_package.Id, out _);
        }
    }
}

