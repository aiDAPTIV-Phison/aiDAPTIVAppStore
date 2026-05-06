using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UniGetUI.Core.Data;
using UniGetUI.Core.Logging;

namespace UniGetUI.PhisonService
{
    /// <summary>
    /// 啟動前置條件檢查結果
    /// </summary>
    public class StartupRequirementResult
    {
        /// <summary>
        /// 需求項目名稱
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 是否通過檢查
        /// </summary>
        public bool Passed { get; set; }

        /// <summary>
        /// 錯誤訊息（如果未通過）
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 建議的修復指令
        /// </summary>
        public string? FixCommand { get; set; }
    }

    /// <summary>
    /// 啟動前置條件檢查器
    /// 檢查必要的工具和環境是否已正確安裝
    /// </summary>
    public static class StartupRequirementsChecker
    {
        /// <summary>
        /// 所有檢查項目的結果
        /// </summary>
        public static List<StartupRequirementResult> Results { get; private set; } = new();

        /// <summary>
        /// 是否所有檢查都通過
        /// </summary>
        public static bool AllPassed => Results.Count > 0 && Results.All(r => r.Passed);

        /// <summary>
        /// 執行所有啟動前置條件檢查
        /// </summary>
        /// <returns>是否所有檢查都通過</returns>
        public static async Task<bool> CheckAllRequirementsAsync()
        {
            Logger.Info("Starting startup requirements check...");
            Results.Clear();

            // 並行執行所有檢查
            var tasks = new List<Task<StartupRequirementResult>>
            {
                CheckGitInstalledAsync(),
                CheckScoopInstalledAsync(),
                CheckVersionsBucketExistsAsync(),
                CheckScoopSearchInstalledAsync(),
                CheckPhisonAidaptivEnvVarAsync()
            };

            var results = await Task.WhenAll(tasks);
            Results.AddRange(results);

            // 確保 ValidBucketList 中的 bucket 都已新增（不存在的會自動新增）
            await EnsureValidBucketsExistAsync();

            // 檢查 ValidBucketList 中的 bucket URL 是否正確（需在 EnsureValidBucketsExistAsync 之後執行）
            var bucketUrlResults = await CheckValidBucketUrlsAsync();
            Results.AddRange(bucketUrlResults);

            // 記錄檢查結果
            foreach (var result in Results)
            {
                if (result.Passed)
                {
                    Logger.Info($"✓ {result.Name}: Passed");
                }
                else
                {
                    Logger.Warn($"✗ {result.Name}: Failed - {result.ErrorMessage}");
                    if (!string.IsNullOrEmpty(result.FixCommand))
                    {
                        Logger.Info($"  Fix command: {result.FixCommand}");
                    }
                }
            }

            Logger.Info($"Startup requirements check completed. All passed: {AllPassed}");
            return AllPassed;
        }

        /// <summary>
        /// 檢查 Git 是否已安裝
        /// </summary>
        private static async Task<StartupRequirementResult> CheckGitInstalledAsync()
        {
            var result = new StartupRequirementResult
            {
                Name = "Git",
                FixCommand = "scoop install -k main/git"
            };

            try
            {
                // 先檢查 PATH 中是否有 git.exe
                var (found, path) = await WhichAsync("git.exe");
                if (found)
                {
                    // 驗證 git 是否可執行
                    var versionOutput = await ExecuteCommandAsync(path, "--version");
                    if (!string.IsNullOrEmpty(versionOutput) && versionOutput.Contains("git version"))
                    {
                        result.Passed = true;
                        Logger.Debug($"Git found at: {path}, version: {versionOutput.Trim()}");
                        return result;
                    }
                }

                result.Passed = false;
                result.ErrorMessage = "Git is not installed or not found in PATH";
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ErrorMessage = $"Error checking Git: {ex.Message}";
                Logger.Error(ex);
            }

            return result;
        }

        /// <summary>
        /// 檢查 Scoop 是否已安裝
        /// </summary>
        private static async Task<StartupRequirementResult> CheckScoopInstalledAsync()
        {
            var result = new StartupRequirementResult
            {
                Name = "Scoop",
                FixCommand = "Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser; Invoke-RestMethod -Uri https://get.scoop.sh | Invoke-Expression"
            };

            try
            {
                // 檢查 scoop.ps1 是否存在
                string scoopPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "scoop", "shims", "scoop.ps1"
                );

                if (File.Exists(scoopPath))
                {
                    result.Passed = true;
                    Logger.Debug($"Scoop found at: {scoopPath}");
                    return result;
                }

                // 嘗試透過 PowerShell 執行 scoop --version
                string? powerShellPath = ServiceManager.GetPowerShellPath();
                if (powerShellPath != null)
                {
                    var versionOutput = await ExecuteCommandAsync(
                        powerShellPath,
                        "-NoProfile -ExecutionPolicy Bypass -Command \"scoop --version\""
                    );

                    if (!string.IsNullOrEmpty(versionOutput) && !versionOutput.Contains("not recognized"))
                    {
                        result.Passed = true;
                        Logger.Debug($"Scoop version: {versionOutput.Trim()}");
                        return result;
                    }
                }

                result.Passed = false;
                result.ErrorMessage = "Scoop is not installed";
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ErrorMessage = $"Error checking Scoop: {ex.Message}";
                Logger.Error(ex);
            }

            return result;
        }

        /// <summary>
        /// 檢查 versions bucket 是否存在
        /// </summary>
        private static async Task<StartupRequirementResult> CheckVersionsBucketExistsAsync()
        {
            var result = new StartupRequirementResult
            {
                Name = "Scoop Versions Bucket",
                FixCommand = "scoop bucket add versions https://github.com/ScoopInstaller/Versions"
            };

            try
            {
                // 檢查 buckets 資料夾中是否有 versions
                string bucketsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "scoop", "buckets", "versions"
                );

                if (Directory.Exists(bucketsPath))
                {
                    result.Passed = true;
                    Logger.Debug($"Versions bucket found at: {bucketsPath}");
                    return result;
                }

                // 嘗試透過 PowerShell 執行 scoop bucket list 來確認
                string? powerShellPath = ServiceManager.GetPowerShellPath();
                if (powerShellPath != null)
                {
                    var bucketListOutput = await ExecuteCommandAsync(
                        powerShellPath,
                        "-NoProfile -ExecutionPolicy Bypass -Command \"scoop bucket list\""
                    );

                    if (!string.IsNullOrEmpty(bucketListOutput) && bucketListOutput.Contains("versions"))
                    {
                        result.Passed = true;
                        Logger.Debug("Versions bucket found in scoop bucket list");
                        return result;
                    }
                }

                result.Passed = false;
                result.ErrorMessage = "Scoop 'versions' bucket is not installed";
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ErrorMessage = $"Error checking versions bucket: {ex.Message}";
                Logger.Error(ex);
            }

            return result;
        }

        /// <summary>
        /// 檢查 scoop-search 是否已安裝
        /// </summary>
        private static async Task<StartupRequirementResult> CheckScoopSearchInstalledAsync()
        {
            var result = new StartupRequirementResult
            {
                Name = "Scoop Search",
                FixCommand = "scoop install -k scoop-search"
            };

            try
            {
                // 檢查 scoop-search.exe 是否存在於 scoop shims 資料夾
                string scoopSearchPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "scoop", "shims", "scoop-search.exe"
                );

                if (File.Exists(scoopSearchPath))
                {
                    result.Passed = true;
                    Logger.Debug($"scoop-search found at: {scoopSearchPath}");
                    return result;
                }

                // 嘗試在 PATH 中尋找 scoop-search.exe
                var (found, path) = await WhichAsync("scoop-search.exe");
                if (found)
                {
                    result.Passed = true;
                    Logger.Debug($"scoop-search found at: {path}");
                    return result;
                }

                result.Passed = false;
                result.ErrorMessage = "scoop-search is not installed";
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ErrorMessage = $"Error checking scoop-search: {ex.Message}";
                Logger.Error(ex);
            }

            return result;
        }

        /// <summary>
        /// 檢查 ValidBucketList 中所有 bucket 的 URL 是否正確
        /// 如果 bucket 存在但 URL 不同，會返回檢查失敗的結果
        /// </summary>
        private static async Task<List<StartupRequirementResult>> CheckValidBucketUrlsAsync()
        {
            var results = new List<StartupRequirementResult>();

            try
            {
                Logger.Info("Checking ValidBucketList bucket URLs...");

                foreach (var bucket in CoreData.ValidBucketList)
                {
                    try
                    {
                        string bucketPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            "scoop", "buckets", bucket.Name
                        );

                        // 如果 bucket 不存在，跳過（已由 EnsureValidBucketsExistAsync 處理）
                        if (!Directory.Exists(bucketPath))
                        {
                            continue;
                        }

                        // 取得目前 bucket 的 URL
                        string? currentUrl = await GetBucketUrlAsync(bucket.Name);
                        if (currentUrl == null)
                        {
                            Logger.Debug($"Could not get URL for bucket '{bucket.Name}', skipping URL check");
                            continue;
                        }

                        // 正規化 URL 進行比較
                        string normalizedCurrent = NormalizeGitUrl(currentUrl);
                        string normalizedExpected = NormalizeGitUrl(bucket.Url);

                        if (!normalizedCurrent.Equals(normalizedExpected, StringComparison.OrdinalIgnoreCase))
                        {
                            // URL 不同，建立檢查失敗的結果
                            Logger.Warn($"Bucket '{bucket.Name}' URL mismatch:");
                            Logger.Warn($"  Current:  {currentUrl}");
                            Logger.Warn($"  Expected: {bucket.Url}");

                            results.Add(new StartupRequirementResult
                            {
                                Name = $"Bucket '{bucket.Name}' URL",
                                Passed = false,
                                ErrorMessage = $"URL mismatch. Current: {currentUrl}, Expected: {bucket.Url}",
                                FixCommand = $"scoop bucket rm {bucket.Name} && scoop bucket add {bucket.Name} {bucket.Url}"
                            });
                        }
                        else
                        {
                            Logger.Debug($"Bucket '{bucket.Name}' URL is correct");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error checking bucket '{bucket.Name}' URL: {ex.Message}");
                    }
                }

                Logger.Info($"ValidBucketList URL check completed. Found {results.Count} mismatch(es)");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in CheckValidBucketUrlsAsync: {ex.Message}");
                Logger.Error(ex);
            }

            return results;
        }

        /// <summary>
        /// 取得 bucket 的 Git remote URL
        /// </summary>
        private static async Task<string?> GetBucketUrlAsync(string bucketName)
        {
            try
            {
                string bucketPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "scoop", "buckets", bucketName
                );

                if (!Directory.Exists(bucketPath))
                {
                    return null;
                }

                // 使用 git remote get-url origin 取得 URL
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "remote get-url origin",
                    WorkingDirectory = bucketPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = processStartInfo };
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    return output.Trim();
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error getting bucket URL for '{bucketName}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 正規化 Git URL（移除尾部斜線和 .git 後綴）
        /// </summary>
        private static string NormalizeGitUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            string normalized = url.TrimEnd('/');
            if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[..^4];
            }
            return normalized;
        }

        /// <summary>
        /// 確保 ValidBucketList.json 中定義的所有 bucket 都已新增到 scoop
        /// 如果 bucket 不存在，會自動執行 scoop bucket add 來新增
        /// </summary>
        private static async Task EnsureValidBucketsExistAsync()
        {
            try
            {
                Logger.Info("Checking ValidBucketList buckets...");

                // 取得目前已安裝的 bucket 列表
                var installedBuckets = await GetInstalledBucketsAsync();
                if (installedBuckets == null)
                {
                    Logger.Warn("Could not get installed bucket list, skipping ValidBucketList check");
                    return;
                }

                Logger.Debug($"Installed buckets: {string.Join(", ", installedBuckets)}");

                // 檢查 ValidBucketList 中的每個 bucket
                foreach (var bucket in CoreData.ValidBucketList)
                {
                    try
                    {
                        // 檢查 bucket 是否已存在（透過資料夾或名稱比對）
                        bool bucketExists = installedBuckets.Any(installed => 
                            installed.Equals(bucket.Name, StringComparison.OrdinalIgnoreCase));

                        // 也檢查 bucket 資料夾是否存在
                        string bucketPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            "scoop", "buckets", bucket.Name
                        );

                        if (!bucketExists && !Directory.Exists(bucketPath))
                        {
                            Logger.Info($"Bucket '{bucket.Name}' not found, adding...");
                            await AddBucketAsync(bucket.Name, bucket.Url);
                        }
                        else
                        {
                            Logger.Debug($"Bucket '{bucket.Name}' already exists");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error checking/adding bucket '{bucket.Name}': {ex.Message}");
                    }
                }

                Logger.Info("ValidBucketList check completed");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in EnsureValidBucketsExistAsync: {ex.Message}");
                Logger.Error(ex);
            }
        }

        /// <summary>
        /// 取得目前已安裝的 scoop bucket 列表
        /// </summary>
        private static async Task<List<string>?> GetInstalledBucketsAsync()
        {
            try
            {
                string? powerShellPath = ServiceManager.GetPowerShellPath();
                if (powerShellPath == null)
                {
                    Logger.Warn("PowerShell not found, cannot get bucket list");
                    return null;
                }

                var output = await ExecuteCommandAsync(
                    powerShellPath,
                    "-NoProfile -ExecutionPolicy Bypass -Command \"scoop bucket list | ForEach-Object { $_.Name }\""
                );

                if (string.IsNullOrEmpty(output))
                {
                    // 嘗試另一種方式：直接列出 buckets 資料夾
                    string bucketsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "scoop", "buckets"
                    );

                    if (Directory.Exists(bucketsPath))
                    {
                        return Directory.GetDirectories(bucketsPath)
                            .Select(Path.GetFileName)
                            .Where(name => !string.IsNullOrEmpty(name))
                            .Cast<string>()
                            .ToList();
                    }

                    return new List<string>();
                }

                return output
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrEmpty(line))
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error getting installed buckets: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 新增 scoop bucket
        /// </summary>
        private static async Task<bool> AddBucketAsync(string name, string url)
        {
            try
            {
                string? powerShellPath = ServiceManager.GetPowerShellPath();
                if (powerShellPath == null)
                {
                    Logger.Error("PowerShell not found, cannot add bucket");
                    return false;
                }

                Logger.Info($"Adding bucket: scoop bucket add {name} {url}");

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = powerShellPath,
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"scoop bucket add '{name}' '{url}'\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = processStartInfo };
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    Logger.Info($"Successfully added bucket '{name}'");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Logger.Debug($"Output: {output.Trim()}");
                    }
                    return true;
                }
                else
                {
                    // 檢查是否是因為 bucket 已存在
                    if (output.Contains("already exists") || error.Contains("already exists"))
                    {
                        Logger.Info($"Bucket '{name}' already exists");
                        return true;
                    }

                    Logger.Error($"Failed to add bucket '{name}'. Exit code: {process.ExitCode}");
                    if (!string.IsNullOrEmpty(error))
                    {
                        Logger.Error($"Error: {error.Trim()}");
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception adding bucket '{name}': {ex.Message}");
                Logger.Error(ex);
                return false;
            }
        }

        /// <summary>
        /// 檢查 PHISON_AIDAPTIV 環境變數是否存在
        /// </summary>
        private static Task<StartupRequirementResult> CheckPhisonAidaptivEnvVarAsync()
        {
            var result = new StartupRequirementResult
            {
                Name = "PHISON_AIDAPTIV Environment Variable",
                FixCommand = "setx PHISON_AIDAPTIV \"<your_value>\""
            };

            try
            {
                // 檢查環境變數是否存在
                string? envValue = Environment.GetEnvironmentVariable("PHISON_AIDAPTIV");

                if (!string.IsNullOrEmpty(envValue))
                {
                    result.Passed = true;
                    Logger.Debug($"PHISON_AIDAPTIV environment variable found with value: {envValue}");
                    return Task.FromResult(result);
                }

                // 也檢查系統環境變數
                envValue = Environment.GetEnvironmentVariable("PHISON_AIDAPTIV", EnvironmentVariableTarget.Machine);
                if (!string.IsNullOrEmpty(envValue))
                {
                    result.Passed = true;
                    Logger.Debug($"PHISON_AIDAPTIV system environment variable found with value: {envValue}");
                    return Task.FromResult(result);
                }

                // 也檢查使用者環境變數
                envValue = Environment.GetEnvironmentVariable("PHISON_AIDAPTIV", EnvironmentVariableTarget.User);
                if (!string.IsNullOrEmpty(envValue))
                {
                    result.Passed = true;
                    Logger.Debug($"PHISON_AIDAPTIV user environment variable found with value: {envValue}");
                    return Task.FromResult(result);
                }

                result.Passed = false;
                result.ErrorMessage = "PHISON_AIDAPTIV environment variable is not set";
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ErrorMessage = $"Error checking PHISON_AIDAPTIV environment variable: {ex.Message}";
                Logger.Error(ex);
            }

            return Task.FromResult(result);
        }

        /// <summary>
        /// 取得未通過檢查的項目
        /// </summary>
        public static IEnumerable<StartupRequirementResult> GetFailedRequirements()
        {
            return Results.Where(r => !r.Passed);
        }

        /// <summary>
        /// 取得格式化的錯誤訊息（用於顯示給使用者）
        /// </summary>
        public static string GetFormattedErrorMessage()
        {
            var failed = GetFailedRequirements().ToList();
            if (failed.Count == 0)
            {
                return string.Empty;
            }

            var lines = new List<string>
            {
                "The following startup requirements are not met:",
                ""
            };

            foreach (var item in failed)
            {
                lines.Add($"• {item.Name}: {item.ErrorMessage}");
                if (!string.IsNullOrEmpty(item.FixCommand))
                {
                    lines.Add($"  Fix: {item.FixCommand}");
                }
                lines.Add("");
            }

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// 在 PATH 中尋找可執行檔
        /// </summary>
        private static Task<(bool found, string path)> WhichAsync(string fileName)
        {
            try
            {
                // 檢查 PATH 環境變數
                string? pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrEmpty(pathEnv))
                {
                    return Task.FromResult((false, string.Empty));
                }

                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    string fullPath = Path.Combine(dir, fileName);
                    if (File.Exists(fullPath))
                    {
                        return Task.FromResult((true, fullPath));
                    }
                }

                return Task.FromResult((false, string.Empty));
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in WhichAsync for {fileName}: {ex.Message}");
                return Task.FromResult((false, string.Empty));
            }
        }

        /// <summary>
        /// 執行命令並取得輸出
        /// </summary>
        private static async Task<string> ExecuteCommandAsync(string fileName, string arguments)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = processStartInfo };
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                return output;
            }
            catch (Exception ex)
            {
                Logger.Debug($"ExecuteCommandAsync failed for {fileName} {arguments}: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
