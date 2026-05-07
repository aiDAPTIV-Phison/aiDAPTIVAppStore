using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using UniGetUI.Core.Logging;

namespace UniGetUI.Services
{
    /// <summary>
    /// 管理 aiDAPTIVService.exe 的生命週期
    /// 支援透過 HTTP API 優雅停止服務
    /// </summary>
    public class AIDAPTIVServiceManager
    {
        private const int StartupRetryCount = 3;
        private Process? _aidaptivProcess;
        private readonly string _aidaptivServicePath;
        private readonly string _aidaptivConfigPath;
        private readonly string _logDirectory;
        private readonly string _apiHost;
        private readonly int _apiPort;
        private readonly string _apiBaseUrl;
        private readonly HttpClient _httpClient;
        private readonly string _aidaptivDirectory;
        private readonly string _wServiceCreateBatPath;
        private readonly string _wServiceDeleteBatPath;

        public bool IsRunning => _aidaptivProcess != null && !_aidaptivProcess.HasExited;

        /// <summary>
        /// 創建 aiDAPTIVService 管理器
        /// </summary>
        /// <param name="aidaptivServicePath">aiDAPTIVService.exe 的路徑</param>
        /// <param name="apiHost">API 主機地址，預設 127.0.0.1</param>
        /// <param name="apiPort">API 端口，預設 13140</param>
        public AIDAPTIVServiceManager(string aidaptivServicePath, string apiHost = "127.0.0.1", int apiPort = 13140)
        {
            _aidaptivServicePath = aidaptivServicePath;
            _aidaptivConfigPath = Path.Combine(Path.GetDirectoryName(aidaptivServicePath) ?? "", "aidaptiv_config.json");

            // Set aidaptiv directory path (appstore/aiDAPTIV/aidaptiv)
            string? serviceDir = Path.GetDirectoryName(aidaptivServicePath);
            _aidaptivDirectory = Path.Combine(serviceDir ?? "", "aidaptiv");
            _wServiceCreateBatPath = Path.Combine(_aidaptivDirectory, "wService_create.bat");
            _wServiceDeleteBatPath = Path.Combine(_aidaptivDirectory, "wService_delete.bat");

            // Set log directory: use the aiDAPTIV folder under the application directory
            // Path example: {AppPath}\appstore\aiDAPTIV\logs
            _logDirectory = Path.Combine(serviceDir ?? "", "logs");

            // 確保日誌目錄存在
            try
            {
                Directory.CreateDirectory(_logDirectory);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to create log directory: {_logDirectory}");
                Logger.Warn(ex.Message);
                // 如果創建失敗，使用相對路徑作為後備方案
                _logDirectory = Path.Combine(Path.GetDirectoryName(aidaptivServicePath) ?? "", "logs");
            }

            _apiHost = apiHost;
            _apiPort = apiPort;
            _apiBaseUrl = $"http://{_apiHost}:{_apiPort}";

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        /// <summary>
        /// 啟動 aiDAPTIVService（隱藏視窗）
        /// </summary>
        public bool Start(Action<string>? statusReporter = null)
        {
            try
            {
                // 檢查是否已經在運行
                if (IsRunning)
                {
                    Logger.Warn("aiDAPTIVService is already running");
                    return true;
                }

                // Execute Windows Service batch files: delete then create
                bool batchReady = ExecuteWindowsServiceBatch(statusReporter);
                if (!batchReady)
                {
                    statusReporter?.Invoke("Windows Service initialization failed, cannot start aiDAPTIVService.");
                    return false;
                }

                // 檢查執行檔是否存在
                if (!File.Exists(_aidaptivServicePath))
                {
                    Logger.Error($"aiDAPTIVService.exe not found at: {_aidaptivServicePath}");
                    return false;
                }

                // 檢查配置檔是否存在（警告但不阻止啟動）
                if (!File.Exists(_aidaptivConfigPath))
                {
                    Logger.Warn($"aidaptiv_config.json not found at: {_aidaptivConfigPath}");
                }
                else
                {
                    Logger.Info($"Using aiDAPTIV config: {_aidaptivConfigPath}");
                }

                // 配置進程啟動參數（包含自訂日誌目錄）
                var startInfo = new ProcessStartInfo
                {
                    FileName = _aidaptivServicePath,
                    Arguments = $"--host {_apiHost} --port {_apiPort} --log-dir \"{_logDirectory}\"",
                    WorkingDirectory = Path.GetDirectoryName(_aidaptivServicePath),
                    UseShellExecute = false,
                    CreateNoWindow = true,           // 不創建新視窗
                    WindowStyle = ProcessWindowStyle.Hidden,  // 隱藏視窗
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                Logger.Info($"Starting aiDAPTIVService with arguments: {startInfo.Arguments}");
                Logger.Info($"aiDAPTIV logs will be written to: {_logDirectory}");

                for (int attempt = 1; attempt <= StartupRetryCount; attempt++)
                {
                    statusReporter?.Invoke($"Starting aiDAPTIVService (attempt {attempt}/{StartupRetryCount})...");
                    _aidaptivProcess = Process.Start(startInfo);

                    if (_aidaptivProcess != null)
                    {
                        Logger.ImportantInfo($"✅ aiDAPTIVService started successfully (PID: {_aidaptivProcess.Id})");
                        Logger.ImportantInfo($"   API available at: {_apiBaseUrl}");
                        statusReporter?.Invoke($"aiDAPTIVService started successfully on attempt {attempt}.");

                        // 訂閱進程退出事件
                        _aidaptivProcess.EnableRaisingEvents = true;
                        _aidaptivProcess.Exited += (sender, args) =>
                        {
                            Logger.Warn($"aiDAPTIVService process exited (Exit Code: {_aidaptivProcess?.ExitCode})");
                        };

                        return true;
                    }

                    Logger.Error("Failed to start aiDAPTIVService: Process.Start returned null");
                    statusReporter?.Invoke($"aiDAPTIVService startup failed (attempt {attempt}/{StartupRetryCount}).");
                    if (attempt < StartupRetryCount)
                    {
                        System.Threading.Thread.Sleep(1000);
                    }
                }

                Logger.Error("aiDAPTIVService failed to start after 3 attempts");
                statusReporter?.Invoke("aiDAPTIVService failed to start after 3 attempts.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception while starting aiDAPTIVService: {ex.Message}");
                Logger.Error(ex);
                statusReporter?.Invoke($"aiDAPTIVService startup exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止 aiDAPTIVService（優雅關閉 + 強制終止）
        /// </summary>
        public void Stop()
        {
            try
            {
                if (_aidaptivProcess == null || _aidaptivProcess.HasExited)
                {
                    Logger.Info("aiDAPTIVService is not running, no need to stop");
                    return;
                }

                Logger.Info($"Stopping aiDAPTIVService (PID: {_aidaptivProcess.Id})...");

                // 階段一：嘗試透過 API 優雅關閉
                bool apiStopSuccessful = TryStopViaApi();

                if (apiStopSuccessful)
                {
                    // 等待進程自然退出（最多 5 秒）
                    Logger.Info("Waiting for aiDAPTIVService to exit gracefully...");
                    bool exited = _aidaptivProcess.WaitForExit(5000);

                    if (exited)
                    {
                        Logger.Info("✅ aiDAPTIVService stopped gracefully via API");
                        _aidaptivProcess.Dispose();
                        _aidaptivProcess = null;
                        return;
                    }
                    else
                    {
                        Logger.Warn("aiDAPTIVService did not exit after API stop, will force terminate");
                    }
                }
                else
                {
                    Logger.Warn("API stop failed, will attempt graceful process termination");

                    // 階段二：嘗試優雅關閉視窗
                    _aidaptivProcess.CloseMainWindow();
                    bool exited = _aidaptivProcess.WaitForExit(3000);

                    if (exited)
                    {
                        Logger.Info("✅ aiDAPTIVService stopped gracefully via CloseMainWindow");
                        _aidaptivProcess.Dispose();
                        _aidaptivProcess = null;
                        return;
                    }
                }

                // 階段三：強制終止
                if (!_aidaptivProcess.HasExited)
                {
                    Logger.Warn("Forcing aiDAPTIVService termination...");
                    _aidaptivProcess.Kill(entireProcessTree: true);
                    _aidaptivProcess.WaitForExit(2000);
                    Logger.Info("✅ aiDAPTIVService forcefully terminated");
                }

                _aidaptivProcess.Dispose();
                _aidaptivProcess = null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception while stopping aiDAPTIVService: {ex.Message}");
                Logger.Error(ex);
            }
            finally
            {
                // Execute Windows Service cleanup batch file
                ExecuteWindowsServiceDelete();
                _httpClient?.Dispose();
            }
        }

        /// <summary>
        /// 嘗試透過 API 停止服務
        /// </summary>
        private bool TryStopViaApi()
        {
            try
            {
                Logger.Info("Attempting to stop aiDAPTIVService via API...");

                // 1. 檢查服務健康狀態
                if (!CheckHealth())
                {
                    Logger.Warn("aiDAPTIVService API is not responding to health check");
                    return false;
                }

                Logger.Info("aiDAPTIVService API is responsive, proceeding with graceful shutdown");

                // 2. 檢查並停止 Llama Server
                bool llamaRunning = CheckLlamaStatus();
                if (llamaRunning)
                {
                    Logger.Info("Llama server is running, sending stop command...");
                    bool llamaStopped = StopLlamaServer();
                    if (llamaStopped)
                    {
                        Logger.Info("✅ Llama server stop command sent successfully");
                        // 等待一下讓服務有時間停止
                        System.Threading.Thread.Sleep(1000);
                        // 再次確認是否已停止
                        llamaRunning = CheckLlamaStatus();
                        if (!llamaRunning)
                        {
                            Logger.Info("✅ Llama server confirmed stopped");
                        }
                        else
                        {
                            Logger.Warn("⚠️ Llama server still running after stop command");
                        }
                    }
                    else
                    {
                        Logger.Warn("⚠️ Failed to send stop command to Llama server");
                    }
                }
                else
                {
                    Logger.Info("Llama server is not running, skipping stop");
                }

                // 3. 檢查並停止 Embedding Server
                bool embeddingRunning = CheckEmbeddingStatus();
                if (embeddingRunning)
                {
                    Logger.Info("Embedding server is running, sending stop command...");
                    bool embeddingStopped = StopEmbeddingServer();
                    if (embeddingStopped)
                    {
                        Logger.Info("✅ Embedding server stop command sent successfully");
                        // 等待一下讓服務有時間停止
                        System.Threading.Thread.Sleep(1000);
                        // 再次確認是否已停止
                        embeddingRunning = CheckEmbeddingStatus();
                        if (!embeddingRunning)
                        {
                            Logger.Info("✅ Embedding server confirmed stopped");
                        }
                        else
                        {
                            Logger.Warn("⚠️ Embedding server still running after stop command");
                        }
                    }
                    else
                    {
                        Logger.Warn("⚠️ Failed to send stop command to Embedding server");
                    }
                }
                else
                {
                    Logger.Info("Embedding server is not running, skipping stop");
                }

                // 4. 最終確認：只有當兩個子服務都停止後，才能安全關閉 aiDAPTIVService
                bool allStopped = !llamaRunning && !embeddingRunning;

                if (!allStopped)
                {
                    Logger.Warn($"⚠️ Not all sub-services stopped - Llama: {(llamaRunning ? "running" : "stopped")}, Embedding: {(embeddingRunning ? "running" : "stopped")}");
                    return false;
                }

                Logger.Info("✅ All sub-services (Llama & Embedding) are stopped");

                // 5. 關鍵步驟：調用 shutdown API 讓 aiDAPTIVService 自己關閉
                Logger.Info("Sending shutdown command to aiDAPTIVService...");
                bool shutdownSuccessful = ShutdownService();

                if (shutdownSuccessful)
                {
                    Logger.Info("✅ aiDAPTIVService shutdown command sent successfully");
                }
                else
                {
                    Logger.Warn("⚠️ Failed to send shutdown command to aiDAPTIVService");
                }

                return shutdownSuccessful;
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception during API stop: {ex.Message}");
                Logger.Error(ex);
                return false;
            }
        }

        /// <summary>
        /// 檢查服務健康狀態
        /// </summary>
        private bool CheckHealth()
        {
            try
            {
                string url = $"{_apiBaseUrl}/health";
                Logger.Debug($"Checking health: GET {url}");

                var response = _httpClient.GetAsync(url).Result;
                bool isHealthy = response.IsSuccessStatusCode;

                if (isHealthy)
                {
                    Logger.Debug("Health check passed");
                }
                else
                {
                    Logger.Debug($"Health check failed: {response.StatusCode}");
                }

                return isHealthy;
            }
            catch (Exception ex)
            {
                Logger.Debug($"Health check exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 檢查 Llama Server 是否正在運行
        /// </summary>
        private bool CheckLlamaStatus()
        {
            try
            {
                string url = $"{_apiBaseUrl}/llama/status";
                Logger.Debug($"Checking Llama status: GET {url}");

                var response = _httpClient.GetAsync(url).Result;

                if (response.IsSuccessStatusCode)
                {
                    string content = response.Content.ReadAsStringAsync().Result;
                    Logger.Debug($"Llama status response: {content}");

                    // 解析 JSON 回應判斷是否運行中
                    using (JsonDocument doc = JsonDocument.Parse(content))
                    {
                        if (doc.RootElement.TryGetProperty("running", out JsonElement runningElement))
                        {
                            bool isRunning = runningElement.GetBoolean();
                            Logger.Debug($"Llama server running: {isRunning}");
                            return isRunning;
                        }
                        else if (doc.RootElement.TryGetProperty("status", out JsonElement statusElement))
                        {
                            string status = statusElement.GetString() ?? "";
                            bool isRunning = status.Equals("running", StringComparison.OrdinalIgnoreCase);
                            Logger.Debug($"Llama server status: {status}");
                            return isRunning;
                        }
                    }
                }

                Logger.Debug($"Llama status check failed: {response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Debug($"Llama status check exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 檢查 Embedding Server 是否正在運行
        /// </summary>
        private bool CheckEmbeddingStatus()
        {
            try
            {
                string url = $"{_apiBaseUrl}/embedding/status";
                Logger.Debug($"Checking Embedding status: GET {url}");

                var response = _httpClient.GetAsync(url).Result;

                if (response.IsSuccessStatusCode)
                {
                    string content = response.Content.ReadAsStringAsync().Result;
                    Logger.Debug($"Embedding status response: {content}");

                    // 解析 JSON 回應判斷是否運行中
                    using (JsonDocument doc = JsonDocument.Parse(content))
                    {
                        if (doc.RootElement.TryGetProperty("running", out JsonElement runningElement))
                        {
                            bool isRunning = runningElement.GetBoolean();
                            Logger.Debug($"Embedding server running: {isRunning}");
                            return isRunning;
                        }
                        else if (doc.RootElement.TryGetProperty("status", out JsonElement statusElement))
                        {
                            string status = statusElement.GetString() ?? "";
                            bool isRunning = status.Equals("running", StringComparison.OrdinalIgnoreCase);
                            Logger.Debug($"Embedding server status: {status}");
                            return isRunning;
                        }
                    }
                }

                Logger.Debug($"Embedding status check failed: {response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Debug($"Embedding status check exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止 Llama Server
        /// </summary>
        private bool StopLlamaServer()
        {
            try
            {
                string url = $"{_apiBaseUrl}/llama/stop?timeout_seconds=60";
                Logger.Info($"Stopping Llama server: POST {url}");

                var response = _httpClient.PostAsync(url, null).Result;

                if (response.IsSuccessStatusCode)
                {
                    Logger.Info("Llama server stop command sent successfully");
                    return true;
                }
                else
                {
                    Logger.Warn($"Llama server stop failed: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Exception stopping Llama server: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止 Embedding Server
        /// </summary>
        private bool StopEmbeddingServer()
        {
            try
            {
                string url = $"{_apiBaseUrl}/embedding/stop?timeout_seconds=60";
                Logger.Info($"Stopping Embedding server: POST {url}");

                var response = _httpClient.PostAsync(url, null).Result;

                if (response.IsSuccessStatusCode)
                {
                    Logger.Info("Embedding server stop command sent successfully");
                    return true;
                }
                else
                {
                    Logger.Warn($"Embedding server stop failed: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Exception stopping Embedding server: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 關閉 aiDAPTIVService 本身（在所有子服務停止後調用）
        /// </summary>
        private bool ShutdownService()
        {
            try
            {
                string url = $"{_apiBaseUrl}/shutdown";
                Logger.Info($"Shutting down aiDAPTIVService: POST {url}");

                var response = _httpClient.PostAsync(url, null).Result;

                if (response.IsSuccessStatusCode)
                {
                    Logger.Info("aiDAPTIVService shutdown command sent successfully");
                    return true;
                }
                else
                {
                    Logger.Warn($"aiDAPTIVService shutdown failed: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Exception shutting down aiDAPTIVService: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 取得服務狀態描述
        /// </summary>
        public string GetStatus()
        {
            if (_aidaptivProcess == null)
                return "Not Started";

            if (_aidaptivProcess.HasExited)
                return $"Exited (Code: {_aidaptivProcess.ExitCode})";

            return $"Running (PID: {_aidaptivProcess.Id}, API: {_apiBaseUrl})";
        }

        /// <summary>
        /// Execute Windows Service batch files: delete then create
        /// This is called when UniGetUI starts (before starting aiDAPTIV service)
        /// </summary>
        private bool ExecuteWindowsServiceBatch(Action<string>? statusReporter = null)
        {
            try
            {
                Logger.Info("=== Executing Windows Service batch files ===");

                // Step 1: Execute wService_delete.bat to cleanup any existing service
                // Note: ada.exe might not exist, so errors are expected and acceptable
                Logger.Info("Step 1: Cleaning up existing Windows Service (errors are acceptable if service doesn't exist)");
                bool deleteSuccess = ExecuteBatchFileWithRetry(_wServiceDeleteBatPath, "wService_delete.bat", acceptErrors: true, statusReporter: statusReporter);

                if (!deleteSuccess)
                {
                    Logger.Error("❌ wService_delete.bat FAILED after retries");
                    statusReporter?.Invoke("wService_delete.bat failed after 3 attempts.");
                    return false;
                }

                // Wait a moment for the service to be deleted
                System.Threading.Thread.Sleep(1000);

                // Step 2: Execute wService_create.bat to create and start the service
                // Note: This step is critical - failures should be logged
                Logger.Info("Step 2: Creating and starting Windows Service");
                bool createSuccess = ExecuteBatchFileWithRetry(_wServiceCreateBatPath, "wService_create.bat", acceptErrors: false, statusReporter: statusReporter);

                if (!createSuccess)
                {
                    Logger.Error("❌ wService_create.bat FAILED - Windows Service may not be available");
                    statusReporter?.Invoke("wService_create.bat failed after 3 attempts.");
                    return false;
                }
                else
                {
                    Logger.ImportantInfo("✅ Windows Service created and started successfully");
                }

                Logger.Info("=== Windows Service batch files execution completed ===");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Exception during Windows Service batch execution:");
                Logger.Error(ex);
                statusReporter?.Invoke($"Windows Service initialization exception: {ex.Message}");
                return false;
            }
        }

        private bool ExecuteBatchFileWithRetry(string batchFilePath, string batchFileName, bool acceptErrors, Action<string>? statusReporter)
        {
            for (int attempt = 1; attempt <= StartupRetryCount; attempt++)
            {
                statusReporter?.Invoke($"{batchFileName} running (attempt {attempt}/{StartupRetryCount})...");
                bool success = ExecuteBatchFile(batchFilePath, batchFileName, acceptErrors: acceptErrors);
                if (success)
                {
                    statusReporter?.Invoke($"{batchFileName} succeeded on attempt {attempt}.");
                    return true;
                }

                statusReporter?.Invoke($"{batchFileName} failed (attempt {attempt}/{StartupRetryCount}).");
                if (attempt < StartupRetryCount)
                {
                    Logger.Warn($"{batchFileName} failed on attempt {attempt}, retrying...");
                    System.Threading.Thread.Sleep(1000);
                }
            }

            Logger.Error($"{batchFileName} failed after {StartupRetryCount} attempts");
            return false;
        }

        /// <summary>
        /// Execute Windows Service delete batch file
        /// This is called when UniGetUI stops (AFTER aiDAPTIV service has stopped)
        /// </summary>
        private void ExecuteWindowsServiceDelete()
        {
            try
            {
                Logger.Info("=== Executing Windows Service cleanup (after aiDAPTIV service stopped) ===");
                bool deleteSuccess = ExecuteBatchFile(_wServiceDeleteBatPath, "wService_delete.bat", acceptErrors: true);

                if (deleteSuccess)
                {
                    Logger.Info("✅ Windows Service cleanup completed successfully");
                }
                else
                {
                    Logger.Warn("⚠️ Windows Service cleanup had issues");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Exception during Windows Service cleanup:");
                Logger.Error(ex);
            }
        }

        /// <summary>
        /// Execute a batch file with elevated privileges
        /// </summary>
        /// <param name="batchFilePath">Full path to the batch file</param>
        /// <param name="batchFileName">Name of the batch file (for logging)</param>
        /// <param name="acceptErrors">If true, errors are logged as warnings instead of errors</param>
        /// <returns>True if execution succeeded, false otherwise</returns>
        private bool ExecuteBatchFile(string batchFilePath, string batchFileName, bool acceptErrors = false)
        {
            if (!File.Exists(batchFilePath))
            {
                if (acceptErrors)
                {
                    Logger.Warn($"{batchFileName} not found at: {batchFilePath}");
                }
                else
                {
                    Logger.Error($"{batchFileName} not found at: {batchFilePath}");
                }
                return false;
            }

            Logger.Info($"Executing {batchFileName}: {batchFilePath}");

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batchFilePath}\"",
                    WorkingDirectory = Path.GetDirectoryName(batchFilePath),
                    UseShellExecute = true,
                    Verb = "runas",  // Request administrator privileges
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (Process? process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        // Wait for the batch file to complete (max 10 seconds)
                        bool exited = process.WaitForExit(10000);

                        if (exited)
                        {
                            int exitCode = process.ExitCode;

                            // Exit code 0 typically means success
                            if (exitCode == 0)
                            {
                                Logger.Info($"✅ {batchFileName} executed successfully (Exit Code: {exitCode})");
                                return true;
                            }
                            else
                            {
                                // Non-zero exit code
                                if (acceptErrors)
                                {
                                    Logger.Warn($"⚠️ {batchFileName} completed with exit code {exitCode} (acceptable)");
                                    return true;  // Still return true if errors are acceptable
                                }
                                else
                                {
                                    Logger.Error($"❌ {batchFileName} failed with exit code {exitCode}");
                                    return false;
                                }
                            }
                        }
                        else
                        {
                            Logger.Warn($"⚠️ {batchFileName} did not complete within timeout");
                            try
                            {
                                process.Kill();
                            }
                            catch { }
                            return false;
                        }
                    }
                    else
                    {
                        Logger.Warn($"⚠️ Failed to start process for {batchFileName}");
                        return false;
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception win32Ex)
            {
                // This typically happens when user cancels UAC prompt
                if (acceptErrors)
                {
                    Logger.Warn($"⚠️ {batchFileName} execution cancelled or failed (Win32Exception: {win32Ex.Message}) - acceptable");
                }
                else
                {
                    Logger.Error($"❌ {batchFileName} execution cancelled or failed (Win32Exception: {win32Ex.Message})");
                }
                return false;
            }
            catch (Exception ex)
            {
                if (acceptErrors)
                {
                    Logger.Warn($"⚠️ Exception executing {batchFileName}: {ex.Message} - acceptable");
                }
                else
                {
                    Logger.Error($"❌ Exception executing {batchFileName}:");
                    Logger.Error(ex);
                }
                return false;
            }
        }
    }
}

