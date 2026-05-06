using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UniGetUI.Core.Data;
using UniGetUI.Core.Logging;
using UniGetUI.Interface.Enums;
using UniGetUI.PackageEngine.Interfaces;
using UniGetUI.PackageEngine.PackageLoader;

namespace UniGetUI.PhisonService
{
    /// <summary>
    /// Manages Windows service start and stop operations
    /// </summary>
    public class ServiceManager
    {
        private static string aiDAPTIVStatus = "Idle";
        private static ServiceErrorCode aiDAPTIVErrorCode = ServiceErrorCode.Init;
        private static readonly object statusLock = new object();
        private static string? _cachedPowerShellPath = null;

        /// <summary>
        /// Gets the current aiDAPTIV service status and error code
        /// </summary>
        /// <returns>A tuple containing the status string and error code</returns>
        public static Task<(string status, ServiceErrorCode errorCode)> GetServiceStatusAsync()
        {
            lock (statusLock)
            {
                return Task.FromResult((aiDAPTIVStatus, aiDAPTIVErrorCode));
            }
        }

        /// <summary>
        /// Gets the available PowerShell executable path
        /// Only uses Windows PowerShell (powershell.exe)
        /// </summary>
        /// <returns>The path to the PowerShell executable, or null if not found</returns>
        public static string? GetPowerShellPath()
        {
            if (_cachedPowerShellPath != null)
            {
                Logger.Info($"Using cached PowerShell path: {_cachedPowerShellPath}");
                return _cachedPowerShellPath;
            }

            Logger.Info("Searching for Windows PowerShell executable...");

            // Common Windows PowerShell installation paths
            var powershellPaths = new[]
            {
                "powershell.exe", // Try PATH first
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "WindowsPowerShell", "v1.0", "powershell.exe"),
                @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                @"C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe",
            };

            // Only use Windows PowerShell (powershell.exe)
            foreach (var path in powershellPaths)
            {
                Logger.Info($"Trying PowerShell path: {path}");

                // Check if file exists for full paths
                if (Path.IsPathRooted(path))
                {
                    Logger.Info($"  File exists: {File.Exists(path)}");
                }

                if (IsPowerShellAvailable(path))
                {
                    _cachedPowerShellPath = path;
                    Logger.Info($"✓ Successfully found Windows PowerShell: {path}");
                    return _cachedPowerShellPath;
                }
                else
                {
                    Logger.Warn($"✗ PowerShell not available at: {path}");
                }
            }

            Logger.Error("No Windows PowerShell executable found after checking all paths");
            // Last resort: try the most common path without verification
            string fallbackPath = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
            Logger.Warn($"Using fallback path without verification: {fallbackPath}");
            Logger.Warn("If this fails, PowerShell may not be installed or accessible on this system");

            _cachedPowerShellPath = fallbackPath;
            return _cachedPowerShellPath;
        }

        /// <summary>
        /// Checks if the specified Windows PowerShell executable is available
        /// </summary>
        /// <param name="executablePath">The path or name of the PowerShell executable</param>
        /// <returns>True if the executable is available, false otherwise</returns>
        private static bool IsPowerShellAvailable(string executablePath)
        {
            try
            {
                // If it's a full path, check if the file exists first
                if (Path.IsPathRooted(executablePath))
                {
                    if (!File.Exists(executablePath))
                    {
                        Logger.Debug($"  PowerShell file does not exist: {executablePath}");
                        return false;
                    }
                    Logger.Debug($"  PowerShell file exists: {executablePath}");
                }

                // Test PowerShell with a simple command to verify it works
                Logger.Debug($"  Testing PowerShell execution: {executablePath} -Command \"exit 0\"");
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "-Command \"exit 0\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(processStartInfo))
                {
                    if (process != null)
                    {
                        process.WaitForExit(5000); // Wait max 5 seconds
                        Logger.Debug($"  Process exit code: {process.ExitCode}");

                        if (process.ExitCode == 0)
                        {
                            Logger.Debug($"  PowerShell is available and working");
                            return true;
                        }
                    }
                    else
                    {
                        Logger.Debug($"  Failed to start process for: {executablePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the exception for debugging purposes
                Logger.Warn($"  PowerShell check exception for '{executablePath}': {ex.GetType().Name} - {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Starts the specified Windows service using aiDAPTIV service
        /// </summary>
        /// <param name="package">The package whose service should be started</param>
        /// <returns>A tuple containing error code and error message if any</returns>
        public static async Task<(ServiceErrorCode errorCode, string? errorMessage)> StartServiceAsync(IPackage package, bool useLegacy = false)
        {
            // Reset status variables to prevent UI from reading stale Fail state
            lock (statusLock)
            {
                aiDAPTIVStatus = "Initializing...";
                aiDAPTIVErrorCode = ServiceErrorCode.Init;
            }

            if (package is null)
            {
                Logger.Warn("Package is null, cannot start service");
                lock (statusLock)
                {
                    aiDAPTIVErrorCode = ServiceErrorCode.Fail;
                }
                return (ServiceErrorCode.Fail, "Package is null");
            }

            string serviceName = package.Id;
            Logger.Info($"Starting service: {serviceName}");

            try
            {
                // Step 0: Pre-start status check
                Logger.Info("Checking service status before starting...");
                var llamaStatus = await aiDAPTIVToolService.aiDAPTIVStatus();
                var embeddingStatus = await aiDAPTIVToolService.aiDAPTIVEmbeddingStatus();

                bool llamaReady = llamaStatus?.Ok == true && llamaStatus.Running && llamaStatus.Ready;
                bool embeddingReady = embeddingStatus?.Ok == true && embeddingStatus.Running && embeddingStatus.Ready;

                Logger.Info($"Pre-check: Llama ready={llamaReady}, Embedding ready={embeddingReady}");

                // Step 0.5: SSD offload space check (only when llama needs to start)
                if (llamaReady)
                {
                    Logger.Info($"Llama already running and ready on port {llamaStatus!.Port}, skipping SSD offload space check and start");
                }
                else
                {
                    // Temporarily disable SSD offload space check.
                    // Logger.Info("Checking SSD offload space before starting Llama...");
                    // var (spaceOk, spaceError) = await aiDAPTIVToolService.ValidateSsdOffloadSpace();
                    // if (!spaceOk)
                    // {
                    //     Logger.Error($"SSD offload space check failed: {spaceError}");
                    //     lock (statusLock)
                    //     {
                    //         aiDAPTIVStatus = "Insufficient disk space for SSD offload.";
                    //         aiDAPTIVErrorCode = ServiceErrorCode.Fail;
                    //     }
                    //     return (ServiceErrorCode.Fail, spaceError);
                    // }

                    // Step 1: Start Llama service
                    Logger.Info("Starting aiDAPTIV Llama service...");
                    lock (statusLock)
                    {
                        aiDAPTIVStatus = "Starting aiDAPTIV Llama Service...";
                        aiDAPTIVErrorCode = ServiceErrorCode.AiDAPTIVStarting;
                    }

                    var (success, errorMessage, errorCode) = useLegacy ? await aiDAPTIVToolService.aiDAPTIVStartLegacy() : await aiDAPTIVToolService.aiDAPTIVStart();

                    if (!success)
                    {
                        // Check if it's an "already running" error (race condition fallback)
                        if (errorMessage?.Contains("already running", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            Logger.Info("Llama started concurrently by another process, treating as success");
                        }
                        else
                        {
                            Logger.Error($"Failed to start aiDAPTIV Llama service: [{errorCode}] {errorMessage}");
                            lock (statusLock)
                            {
                                aiDAPTIVStatus = $"Starting aiDAPTIV Llama Service fail. [{errorCode}]";
                                aiDAPTIVErrorCode = ServiceErrorCode.Fail;
                            }
                            return (ServiceErrorCode.Fail, $"[{errorCode}] {errorMessage}");
                        }
                    }
                }

                // Step 1.5: Start Embedding service if needed
                if (embeddingReady)
                {
                    Logger.Info($"Embedding already running and ready on port {embeddingStatus!.Port}, skipping start");
                }
                else
                {
                    Logger.Info("Starting aiDAPTIV Embedding service...");
                    lock (statusLock)
                    {
                        aiDAPTIVStatus = "Starting aiDAPTIV Embedding Service...";
                        aiDAPTIVErrorCode = ServiceErrorCode.AiDAPTIVStarting;
                    }

                    var (embeddingSuccess, embeddingErrorMessage) = await aiDAPTIVToolService.aiDAPTIVEmbeddingStart();

                    if (!embeddingSuccess)
                    {
                        // Check if it's an "already running" error (race condition fallback)
                        if (embeddingErrorMessage?.Contains("already running", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            Logger.Info("Embedding started concurrently by another process, treating as success");
                        }
                        else
                        {
                            Logger.Error($"Failed to start aiDAPTIV Embedding service: {embeddingErrorMessage}");
                            lock (statusLock)
                            {
                                aiDAPTIVStatus = "Starting aiDAPTIV Embedding Service fail.";
                                aiDAPTIVErrorCode = ServiceErrorCode.Fail;
                            }
                            return (ServiceErrorCode.Fail, embeddingErrorMessage);
                        }
                    }
                }

                // Step 2: Wait for both services to be ready (300 seconds timeout)
                Logger.Info("Waiting for both services to be ready...");
                var startTime = DateTime.Now;
                var timeout = TimeSpan.FromSeconds(300);
                var pollingInterval = TimeSpan.FromSeconds(2);

                while (DateTime.Now - startTime < timeout)
                {
                    lock (statusLock)
                    {
                        aiDAPTIVStatus = "Waiting for services to be ready (Llama & Embedding)...";
                        aiDAPTIVErrorCode = ServiceErrorCode.AiDAPTIVPolling;
                    }

                    llamaStatus = await aiDAPTIVToolService.aiDAPTIVStatus();
                    embeddingStatus = await aiDAPTIVToolService.aiDAPTIVEmbeddingStatus();

                    llamaReady = llamaStatus?.Ok == true && llamaStatus.Running && llamaStatus.Ready;
                    embeddingReady = embeddingStatus?.Ok == true && embeddingStatus.Running && embeddingStatus.Ready;

                    if (llamaReady && embeddingReady)
                    {
                        Logger.Info($"Both services are ready - Llama on port {llamaStatus.Port}, Embedding on port {embeddingStatus.Port}");
                        lock (statusLock)
                        {
                            aiDAPTIVStatus = $"aiDAPTIV services ready - Llama:{llamaStatus.Port}, Embedding:{embeddingStatus.Port}";
                            aiDAPTIVErrorCode = ServiceErrorCode.AppStarting;
                        }

                        // Execute start.ps1 if it exists in the app directory
                        var (scriptErrorCode, scriptError) = await ExecuteStartScriptAsync(package);

                        if (scriptErrorCode != ServiceErrorCode.Success && scriptError != null)
                        {
                            Logger.Error($"start.ps1 execution failed: {scriptError}");
                            lock (statusLock)
                            {
                                aiDAPTIVStatus = $"start.ps1 execution failed: {scriptError}";
                                aiDAPTIVErrorCode = ServiceErrorCode.Fail;
                            }
                            return (ServiceErrorCode.Fail, scriptError);
                        }

                        lock (statusLock)
                        {
                            aiDAPTIVErrorCode = ServiceErrorCode.Success;
                        }
                        return (ServiceErrorCode.Success, null);
                    }

                    await Task.Delay(pollingInterval);
                }

                // Timeout reached
                string timeoutMessage = "Start aiDAPTIV Service Timeout";
                Logger.Error(timeoutMessage);
                lock (statusLock)
                {
                    aiDAPTIVStatus = "Starting aiDAPTIV Service fail.";
                    aiDAPTIVErrorCode = ServiceErrorCode.Fail;
                }
                return (ServiceErrorCode.Fail, timeoutMessage);
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception when starting service {serviceName}: {ex.Message}");
                lock (statusLock)
                {
                    aiDAPTIVStatus = "Starting aiDAPTIV Service fail.";
                    aiDAPTIVErrorCode = ServiceErrorCode.Fail;
                }
                return (ServiceErrorCode.Fail, ex.Message);
            }
        }

        /// <summary>
        /// Counts how many aiDAPTIV-bucket apps are currently running
        /// </summary>
        /// <returns>Count of running apps</returns>
        private static async Task<int> CountRunningAiDAPTIVAppsAsync()
        {
            try
            {
                var allPackages = InstalledPackagesLoader.Instance.Packages;
                var tasks = allPackages
                    .Where(IsFromValidBucket)
                    .Select(async pkg => new { Package = pkg, Status = await GetPackageStatusAsync(pkg) })
                    .ToArray();

                var results = await Task.WhenAll(tasks);
                return results.Count(r => r.Status == 1);
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception when counting running aiDAPTIV apps: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Stops the specified Windows service using aiDAPTIV service
        /// </summary>
        /// <param name="package">The package whose service should be stopped</param>
        /// <returns>A tuple containing error code and error message if any</returns>
        public static async Task<(ServiceErrorCode errorCode, string? errorMessage)> StopServiceAsync(IPackage package)
        {
            if (package is null)
            {
                Logger.Warn("Package is null, cannot stop service");
                lock (statusLock)
                {
                    aiDAPTIVErrorCode = ServiceErrorCode.Fail;
                }
                return (ServiceErrorCode.Fail, "Package is null");
            }

            string serviceName = package.Id;
            Logger.Info($"Stopping service: {serviceName}");

            try
            {
                // Step 1: Execute stop.ps1 for the current app
                lock (statusLock)
                {
                    aiDAPTIVStatus = "Executing stop.ps1 for package...";
                    aiDAPTIVErrorCode = ServiceErrorCode.AppStopping;
                }

                var (scriptErrorCode, scriptError) = await ExecuteStopScriptAsync(package);
                if (scriptErrorCode != ServiceErrorCode.Success)
                {
                    Logger.Warn($"stop.ps1 failed: {scriptError}");
                }

                // Step 2: Check if other aiDAPTIV apps are still running
                Logger.Info("Checking if other aiDAPTIV apps are still running...");
                int runningCount = await CountRunningAiDAPTIVAppsAsync();

                if (runningCount > 0)
                {
                    Logger.ImportantInfo($"{runningCount} aiDAPTIV app(s) still running, keeping services alive");
                    lock (statusLock)
                    {
                        aiDAPTIVStatus = $"App stopped, services kept running ({runningCount} app(s) active)";
                        aiDAPTIVErrorCode = ServiceErrorCode.Success;
                    }
                    return (ServiceErrorCode.Success, null);
                }

                // Step 3: No apps running, stop aiDAPTIV services
                Logger.Info("No aiDAPTIV apps running, stopping aiDAPTIV services...");
                lock (statusLock)
                {
                    aiDAPTIVStatus = "Stopping aiDAPTIV Llama Service...";
                    aiDAPTIVErrorCode = ServiceErrorCode.AiDAPTIVStopping;
                }

                var (success, errorMessage) = await aiDAPTIVToolService.aiDAPTIVStop();
                if (!success)
                {
                    Logger.Error($"Failed to stop aiDAPTIV Llama service: {errorMessage}");
                    lock (statusLock)
                    {
                        aiDAPTIVStatus = "Stopping aiDAPTIV Llama Service fail.";
                        aiDAPTIVErrorCode = ServiceErrorCode.Fail;
                    }
                    return (ServiceErrorCode.Fail, errorMessage);
                }

                // Step 3.5: Stop Embedding service
                Logger.Info("Stopping aiDAPTIV Embedding service...");
                lock (statusLock)
                {
                    aiDAPTIVStatus = "Stopping aiDAPTIV Embedding Service...";
                    aiDAPTIVErrorCode = ServiceErrorCode.AiDAPTIVStopping;
                }

                var (embeddingSuccess, embeddingErrorMessage) = await aiDAPTIVToolService.aiDAPTIVEmbeddingStop();
                if (!embeddingSuccess)
                {
                    Logger.Error($"Failed to stop aiDAPTIV Embedding service: {embeddingErrorMessage}");
                    lock (statusLock)
                    {
                        aiDAPTIVStatus = "Stopping aiDAPTIV Embedding Service fail.";
                        aiDAPTIVErrorCode = ServiceErrorCode.Fail;
                    }
                    return (ServiceErrorCode.Fail, embeddingErrorMessage);
                }

                // Step 4: Poll both services until stopped (300 seconds timeout)
                Logger.Info("aiDAPTIV services stop initiated, polling status...");
                var startTime = DateTime.Now;
                var timeout = TimeSpan.FromSeconds(300);
                var pollingInterval = TimeSpan.FromSeconds(2);

                while (DateTime.Now - startTime < timeout)
                {
                    lock (statusLock)
                    {
                        aiDAPTIVStatus = "Polling aiDAPTIV Service status (Llama & Embedding)...";
                        aiDAPTIVErrorCode = ServiceErrorCode.AiDAPTIVPolling;
                    }

                    var llamaStatus = await aiDAPTIVToolService.aiDAPTIVStatus();
                    var embeddingStatus = await aiDAPTIVToolService.aiDAPTIVEmbeddingStatus();

                    bool llamaStopped = llamaStatus?.Ok == true && !llamaStatus.Running && !llamaStatus.Ready;
                    bool embeddingStopped = embeddingStatus?.Ok == true && !embeddingStatus.Running && !embeddingStatus.Ready;

                    if (llamaStopped && embeddingStopped)
                    {
                        Logger.Info("Both aiDAPTIV services stopped successfully");
                        lock (statusLock)
                        {
                            aiDAPTIVStatus = "aiDAPTIV services stopped successfully";
                            aiDAPTIVErrorCode = ServiceErrorCode.Success;
                        }
                        return (ServiceErrorCode.Success, null);
                    }

                    await Task.Delay(pollingInterval);
                }

                // Step 5: Timeout reached, try to kill process
                lock (statusLock)
                {
                    aiDAPTIVStatus = "aiDAPTIV service is killing";
                    aiDAPTIVErrorCode = ServiceErrorCode.AiDAPTIVKilling;
                }
                Logger.Warn("Stop aiDAPTIV Service Timeout, attempting to kill llama-server.exe process");

                var processes = Process.GetProcessesByName("llama-server");
                if (processes.Length > 0)
                {
                    foreach (var process in processes)
                    {
                        Logger.Info($"Killing process: llama-server.exe (PID: {process.Id})");
                        process.Kill();
                        process.WaitForExit();
                    }
                    Logger.Info("llama-server.exe process(es) killed successfully");
                    lock (statusLock)
                    {
                        aiDAPTIVStatus = "aiDAPTIV service stopped successfully (process killed)";
                        aiDAPTIVErrorCode = ServiceErrorCode.Success;
                    }
                    return (ServiceErrorCode.Success, null);
                }

                string timeoutError = "Stop aiDAPTIV Service Timeout and llama-server.exe process not found";
                Logger.Error(timeoutError);
                lock (statusLock)
                {
                    aiDAPTIVStatus = "Stop aiDAPTIV Service Timeout (process not found)";
                    aiDAPTIVErrorCode = ServiceErrorCode.Fail;
                }
                return (ServiceErrorCode.Fail, timeoutError);
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception when stopping service {serviceName}: {ex.Message}");
                lock (statusLock)
                {
                    aiDAPTIVStatus = $"Exception occurred: {ex.Message}";
                    aiDAPTIVErrorCode = ServiceErrorCode.Fail;
                }
                return (ServiceErrorCode.Fail, ex.Message);
            }
        }

        /// <summary>
        /// Checks if the package is from a valid bucket source by verifying the repository URL
        /// against the ValidBucketList configuration.
        /// </summary>
        /// <param name="package">The package to check</param>
        /// <returns>Whether the package is from a valid bucket repository</returns>
        public static bool IsFromValidBucket(IPackage package)
        {
            if (package is null) return false;

            if (package.Source.Url is null) return false;

            // Check if the source URL matches any valid bucket URL from ValidBucketList.json
            return CoreData.IsValidBucket(package.Source.Url);
        }

        /// <summary>
        /// Stops running packages that belong to buckets declared in ValidBucketList.json.
        /// Packages are processed sequentially following the bucket order in the list.
        /// </summary>
        /// <returns>A tuple containing processed package count and failed package count</returns>
        public static async Task<(int processedCount, int failedCount)> StopRunningValidBucketPackagesAsync()
        {
            int processedCount = 0;
            int failedCount = 0;

            try
            {
                var allPackages = InstalledPackagesLoader.Instance.Packages.ToList();
                if (allPackages.Count == 0)
                {
                    Logger.Info("No installed packages found, skipping ValidBucket stop flow");
                    return (0, 0);
                }

                Logger.Info("Stopping running packages from ValidBucketList...");
                foreach (var bucket in CoreData.ValidBucketList)
                {
                    var packagesInBucket = allPackages
                        .Where(pkg => pkg.Source.Url is not null)
                        .Where(pkg => bucket.NormalizedUrl.Equals(
                            pkg.Source.Url!.ToString().TrimEnd('/'),
                            StringComparison.OrdinalIgnoreCase))
                        .OrderBy(pkg => pkg.Id, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (!packagesInBucket.Any())
                    {
                        continue;
                    }

                    Logger.Info($"Processing bucket '{bucket.Name}' with {packagesInBucket.Count} installed package(s)");
                    foreach (var package in packagesInBucket)
                    {
                        try
                        {
                            int? status = await GetPackageStatusAsync(package);
                            if (status != 1)
                            {
                                Logger.Debug($"Package '{package.Id}' is not running (status={status?.ToString() ?? "null"}), skipping stop.ps1");
                                continue;
                            }

                            processedCount++;
                            Logger.Info($"Executing stop.ps1 for running package '{package.Id}'");
                            var stopResult = await ExecuteStopScriptAsync(package);
                            if (stopResult.errorCode != ServiceErrorCode.Success)
                            {
                                failedCount++;
                                Logger.Warn($"Failed to execute stop.ps1 for '{package.Id}': {stopResult.errorMessage}");
                            }
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            Logger.Error($"Exception while stopping package '{package.Id}': {ex.Message}");
                        }
                    }
                }

                Logger.Info($"ValidBucket stop flow completed. Processed={processedCount}, Failed={failedCount}");
                return (processedCount, failedCount);
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception in StopRunningValidBucketPackagesAsync: {ex.Message}");
                return (processedCount, failedCount + 1);
            }
        }

        /// <summary>
        /// 從 status.ps1 獲取套件的狀態值
        /// </summary>
        /// <param name="package">要檢查的套件</param>
        /// <returns>status 值，如果無法獲取則返回 null</returns>
        public static async Task<int?> GetPackageStatusAsync(IPackage package)
        {
            if (package is null)
            {
                Logger.Warn("Package is null, cannot get status");
                return null;
            }

            try
            {
                // 獲取套件安裝路徑
                string? appPath = package.Manager.DetailsHelper.GetInstallLocation(package);
                if (string.IsNullOrEmpty(appPath) || !Directory.Exists(appPath))
                {
                    Logger.Debug($"Installation location not found for package: {package.Id}");
                    return null;
                }

                string statusScriptPath = Path.Combine(appPath, "status.ps1");
                if (!File.Exists(statusScriptPath))
                {
                    Logger.Debug($"status.ps1 not found at: {statusScriptPath}");
                    return null;
                }

                // 獲取 PowerShell 路徑
                string? powerShellPath = GetPowerShellPath();
                if (powerShellPath == null)
                {
                    Logger.Warn("No PowerShell executable found");
                    return null;
                }

                // 執行 status.ps1
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = powerShellPath,
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{statusScriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = appPath
                };

                using (var process = new Process { StartInfo = processStartInfo })
                {
                    process.Start();
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (process.ExitCode != 0)
                    {
                        Logger.Debug($"status.ps1 exited with code {process.ExitCode}: {error}");
                        return null;
                    }

                    // 解析 JSON 響應
                    try
                    {
                        using (var jsonDoc = JsonDocument.Parse(output))
                        {
                            if (jsonDoc.RootElement.TryGetProperty("status", out var statusElement))
                            {
                                return statusElement.GetInt32();
                            }
                            else
                            {
                                Logger.Debug($"status.ps1 output does not contain 'status' property: {output}");
                                return null;
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        Logger.Debug($"Failed to parse status.ps1 JSON output: {ex.Message}, Output: {output}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception when getting status for package {package.Id}: {ex.Message}");
                Logger.Error(ex);
                return null;
            }
        }

        /// <summary>
        /// Executes the stop.ps1 script in the app's installation directory
        /// </summary>
        /// <param name="package">The package whose stop script should be executed</param>
        /// <returns>A tuple containing error code and error message if any</returns>
        private static async Task<(ServiceErrorCode errorCode, string? errorMessage)> ExecuteStopScriptAsync(IPackage package)
        {
            try
            {
                // Get the installation location using the package manager's DetailsHelper
                string? appPath = package.Manager.DetailsHelper.GetInstallLocation(package);

                if (string.IsNullOrEmpty(appPath) || !Directory.Exists(appPath))
                {
                    string errorMessage = $"Installation location not found for package: {package.Id}";
                    Logger.Warn(errorMessage);
                    return (ServiceErrorCode.Fail, errorMessage);
                }

                string stopScriptPath = Path.Combine(appPath, "stop.ps1");

                if (!File.Exists(stopScriptPath))
                {
                    string infoMessage = $"stop.ps1 not found at: {stopScriptPath}";
                    Logger.Info(infoMessage);
                    // Not finding stop.ps1 is not an error, just skip it
                    return (ServiceErrorCode.Success, null);
                }

                Logger.Info($"Executing stop.ps1 with administrator privileges at: {stopScriptPath}");

                // Get available PowerShell executable
                string? powerShellPath = GetPowerShellPath();
                if (powerShellPath == null)
                {
                    string errorMessage = "No PowerShell executable found on this system";
                    Logger.Error(errorMessage);
                    return (ServiceErrorCode.Fail, errorMessage);
                }

                // Execute PowerShell script with administrator privileges
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = powerShellPath,
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{stopScriptPath}\"",
                    UseShellExecute = true, // Required for Verb = "runas"
                    Verb = "runas", // Run as administrator
                    WindowStyle = ProcessWindowStyle.Hidden, // Minimize window visibility
                    WorkingDirectory = appPath
                };

                using (var process = new Process { StartInfo = processStartInfo })
                {
                    process.Start();
                    await process.WaitForExitAsync();

                    if (process.ExitCode == 0)
                    {
                        Logger.Info("stop.ps1 executed successfully with administrator privileges");
                        return (ServiceErrorCode.Success, null);
                    }
                    else
                    {
                        string errorMessage = $"stop.ps1 exited with code: {process.ExitCode}";
                        Logger.Warn(errorMessage);
                        return (ServiceErrorCode.Fail, errorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"Exception when executing stop.ps1: {ex.Message}";
                Logger.Error(errorMessage);
                return (ServiceErrorCode.Fail, errorMessage);
            }
        }

        /// <summary>
        /// Executes the start.ps1 script in the app's installation directory
        /// </summary>
        /// <param name="package">The package whose start script should be executed</param>
        /// <returns>A tuple containing error code and error message if any</returns>
        private static async Task<(ServiceErrorCode errorCode, string? errorMessage)> ExecuteStartScriptAsync(IPackage package)
        {
            try
            {
                // Get the installation location using the package manager's DetailsHelper
                string? appPath = package.Manager.DetailsHelper.GetInstallLocation(package);

                if (string.IsNullOrEmpty(appPath) || !Directory.Exists(appPath))
                {
                    string errorMessage = $"Installation location not found for package: {package.Id}";
                    Logger.Warn(errorMessage);
                    return (ServiceErrorCode.Fail, errorMessage);
                }

                string startScriptPath = Path.Combine(appPath, "start.ps1");

                if (!File.Exists(startScriptPath))
                {
                    string infoMessage = $"start.ps1 not found at: {startScriptPath}";
                    Logger.Info(infoMessage);
                    // Not finding start.ps1 is not an error, just skip it
                    return (ServiceErrorCode.Success, null);
                }

                Logger.Info($"Executing start.ps1 with administrator privileges at: {startScriptPath}");

                // Get available PowerShell executable
                string? powerShellPath = GetPowerShellPath();
                if (powerShellPath == null)
                {
                    string errorMessage = "No PowerShell executable found on this system";
                    Logger.Error(errorMessage);
                    return (ServiceErrorCode.Fail, errorMessage);
                }

                // Execute PowerShell script with administrator privileges
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = powerShellPath,
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{startScriptPath}\"",
                    UseShellExecute = true, // Required for Verb = "runas"
                    Verb = "runas", // Run as administrator
                    WindowStyle = ProcessWindowStyle.Hidden, // Minimize window visibility
                    WorkingDirectory = appPath
                };

                using (var process = new Process { StartInfo = processStartInfo })
                {
                    process.Start();
                    await process.WaitForExitAsync();

                    if (process.ExitCode == 0)
                    {
                        Logger.Info("start.ps1 executed successfully with administrator privileges");
                        return (ServiceErrorCode.Success, null);
                    }
                    else
                    {
                        string errorMessage = $"start.ps1 exited with code: {process.ExitCode}";
                        Logger.Warn(errorMessage);
                        return (ServiceErrorCode.Fail, errorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"Exception when executing start.ps1: {ex.Message}";
                Logger.Error(errorMessage);
                return (ServiceErrorCode.Fail, errorMessage);
            }
        }
    }
}

