using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using UniGetUI.Core.Logging;

namespace UniGetUI.Services
{
    /// <summary>
    /// Manages aiDAPTIV_Lite.exe lifecycle
    /// aiDAPTIV_Lite will handle aiDAPTIVService.exe startup and restart automatically
    /// </summary>
    public class AIDAPTIVLiteManager
    {
        private Process? _liteProcess;
        private readonly string _litePath;
        private readonly string _wServiceDeleteBatPath;
        private readonly HttpClient _httpClient;
        private const string ApiBaseUrl = "http://127.0.0.1:13140";

        public bool IsRunning => _liteProcess != null && !_liteProcess.HasExited;

        /// <summary>
        /// Create aiDAPTIV_Lite manager
        /// </summary>
        /// <param name="litePath">Path to aiDAPTIV_Lite.exe</param>
        public AIDAPTIVLiteManager(string litePath)
        {
            _litePath = litePath;

            // Windows Service cleanup batch file
            string liteDirectory = Path.GetDirectoryName(litePath) ?? "";
            string aidaptivDir = Path.Combine(liteDirectory, "aidaptiv");
            _wServiceDeleteBatPath = Path.Combine(aidaptivDir, "wService_delete.bat");

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        /// <summary>
        /// Start aiDAPTIV_Lite.exe (no parameters required)
        /// </summary>
        public bool Start()
        {
            try
            {
                if (IsRunning)
                {
                    Logger.Warn("aiDAPTIV_Lite is already running");
                    return true;
                }

                if (!File.Exists(_litePath))
                {
                    Logger.Error($"aiDAPTIV_Lite.exe not found at: {_litePath}");
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = _litePath,
                    WorkingDirectory = Path.GetDirectoryName(_litePath),
                    UseShellExecute = true,  // Required for Verb = "runas"
                    Verb = "runas",  // Run as administrator - will trigger UAC prompt
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Logger.Info($"Starting aiDAPTIV_Lite at: {_litePath}");
                _liteProcess = Process.Start(startInfo);

                if (_liteProcess != null)
                {
                    Logger.ImportantInfo($"✅ aiDAPTIV_Lite started successfully (PID: {_liteProcess.Id})");

                    // Monitor process exit
                    _liteProcess.EnableRaisingEvents = true;
                    _liteProcess.Exited += (sender, args) =>
                    {
                        Logger.Warn($"aiDAPTIV_Lite process exited (Exit Code: {_liteProcess?.ExitCode})");
                    };

                    return true;
                }

                Logger.Error("Failed to start aiDAPTIV_Lite: Process.Start returned null");
                return false;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // User cancelled UAC prompt
                Logger.Warn(" aiDAPTIV_Lite startup cancelled: User declined UAC prompt");
                return false;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 740)
            {
                // Elevation required (should not happen with Verb = "runas", but just in case)
                Logger.Error(" aiDAPTIV_Lite requires administrator privileges but failed to elevate");
                Logger.Error(ex);
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception while starting aiDAPTIV_Lite: {ex.Message}");
                Logger.Error(ex);
                return false;
            }
        }

        /// <summary>
        /// Stop aiDAPTIV_Lite with full cleanup sequence
        /// 5 steps: Stop Llama -> Stop Embedding -> Shutdown Service -> Kill Process -> Cleanup Windows Service
        /// </summary>
        public void Stop()
        {
            try
            {
                Logger.Info("Starting aiDAPTIV_Lite shutdown sequence...");

                // Step 1: Stop Llama Server
                StopServiceSafely("Llama", () => StopLlamaServer());

                // Step 2: Stop Embedding Server
                StopServiceSafely("Embedding", () => StopEmbeddingServer());

                // Step 3: Shutdown aiDAPTIV Service
                StopServiceSafely("aiDAPTIV Service", () => ShutdownAiDAPTIVService());

                // Wait for services to clean up
                Logger.Info("Waiting for services to clean up...");
                System.Threading.Thread.Sleep(2000);

                // Step 4: Kill aiDAPTIV_Lite.exe process
                KillLiteProcess();

                // Step 5: Cleanup Windows Service
                ExecuteWindowsServiceCleanup();

                Logger.ImportantInfo("✅ aiDAPTIV_Lite shutdown sequence completed");
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception during aiDAPTIV_Lite shutdown: {ex.Message}");
                Logger.Error(ex);
            }
            finally
            {
                _httpClient?.Dispose();
                _liteProcess?.Dispose();
                _liteProcess = null;
            }
        }

        /// <summary>
        /// Stop a service safely with error handling
        /// </summary>
        private void StopServiceSafely(string serviceName, Func<bool> stopAction)
        {
            try
            {
                Logger.Info($"Stopping {serviceName}...");
                bool success = stopAction();
                if (success)
                {
                    Logger.Info($"✅ {serviceName} stopped");
                }
                else
                {
                    Logger.Warn($"⚠️ {serviceName} stop failed (continuing shutdown)");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"⚠️ Exception stopping {serviceName}: {ex.Message} (continuing shutdown)");
            }
        }

        /// <summary>
        /// Stop Llama Server via HTTP API
        /// </summary>
        private bool StopLlamaServer()
        {
            try
            {
                string url = $"{ApiBaseUrl}/llama/stop";
                var response = _httpClient.PostAsync(url, null).Result;
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Logger.Debug($"Llama stop exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Stop Embedding Server via HTTP API
        /// </summary>
        private bool StopEmbeddingServer()
        {
            try
            {
                string url = $"{ApiBaseUrl}/embedding/stop";
                var response = _httpClient.PostAsync(url, null).Result;
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Logger.Debug($"Embedding stop exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Shutdown aiDAPTIV Service via HTTP API
        /// </summary>
        private bool ShutdownAiDAPTIVService()
        {
            try
            {
                string url = $"{ApiBaseUrl}/shutdown";
                var response = _httpClient.PostAsync(url, null).Result;
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Logger.Debug($"aiDAPTIV Service shutdown exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Force kill aiDAPTIV_Lite.exe process
        /// </summary>
        private void KillLiteProcess()
        {
            try
            {
                Logger.Info("Killing aiDAPTIV_Lite.exe process...");

                // Method 1: Kill tracked process
                if (_liteProcess != null && !_liteProcess.HasExited)
                {
                    Logger.Info($"Killing tracked process (PID: {_liteProcess.Id})");
                    _liteProcess.Kill(entireProcessTree: true);
                    _liteProcess.WaitForExit(5000);
                }

                // Method 2: Kill all aiDAPTIV_Lite.exe processes
                var processes = Process.GetProcessesByName("aiDAPTIV_Lite");
                foreach (var process in processes)
                {
                    try
                    {
                        Logger.Info($"Killing aiDAPTIV_Lite process (PID: {process.Id})");
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(2000);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to kill process {process.Id}: {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                Logger.Info(" aiDAPTIV_Lite.exe terminated");
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception killing aiDAPTIV_Lite.exe: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute wService_delete.bat to cleanup Windows Service
        /// </summary>
        private void ExecuteWindowsServiceCleanup()
        {
            try
            {
                if (!File.Exists(_wServiceDeleteBatPath))
                {
                    Logger.Warn($"wService_delete.bat not found at: {_wServiceDeleteBatPath}");
                    return;
                }

                Logger.Info($"Executing wService_delete.bat: {_wServiceDeleteBatPath}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{_wServiceDeleteBatPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(_wServiceDeleteBatPath),
                    UseShellExecute = true,
                    Verb = "runas",  // Run as administrator
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (Process? process = Process.Start(startInfo))
                {
                    if (process != null && process.WaitForExit(10000))
                    {
                        Logger.Info($"✅ wService_delete.bat completed (Exit Code: {process.ExitCode})");
                    }
                    else
                    {
                        Logger.Warn("⚠️ wService_delete.bat did not complete in time");
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User cancelled UAC prompt
                Logger.Warn("⚠️ wService_delete.bat execution cancelled (UAC prompt declined)");
            }
            catch (Exception ex)
            {
                Logger.Warn($"⚠️ Exception executing wService_delete.bat: {ex.Message}");
            }
        }

        /// <summary>
        /// Get status description
        /// </summary>
        public string GetStatus()
        {
            if (_liteProcess == null)
                return "Not Started";

            if (_liteProcess.HasExited)
                return $"Exited (Code: {_liteProcess.ExitCode})";

            return $"Running (PID: {_liteProcess.Id})";
        }
    }
}

