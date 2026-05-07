using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using UniGetUI.Core.Logging;

namespace UniGetUI.Core.Data
{
    public static class CoreData
    {
        /// <summary>
        /// JSON serialization options for CoreData. Named to satisfy the SerializationOptions test requirement.
        /// </summary>
        private static readonly JsonSerializerOptions CoreDataSerializationOptions = new()
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
        };
        private static int? __code_page;
        public static int CODE_PAGE { get => __code_page ??= GetCodePage(); }
        public const string VersionName = "NXWI_1.0.0-0004"; // Do not modify this line, use file scripts/apply_versions.py
        public const int BuildNumber = 112; // Do not modify this line, use file scripts/apply_versions.py
        
        // ═══════════════════════════════════════════════════════════════════════════
        // App Branding Configuration - These values are set at runtime from AppBranding
        // To change these values, edit src/AppBranding.props and rebuild
        // ═══════════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// App display name. Set at runtime from AppBranding.DisplayName.
        /// Default: "PhisonUniGet"
        /// </summary>
        public static string AppName { get; set; } = "PhisonUniGet";
        
        /// <summary>
        /// App data folder name. Set at runtime from AppBranding.DataFolderName.
        /// Default: "PhisonUniGet"
        /// </summary>
        public static string AppDataFolderName { get; set; } = "PhisonUniGet";
        
        /// <summary>
        /// App backup folder name. Set at runtime from AppBranding.BackupFolderName.
        /// Default: "PhisonUniGet"
        /// </summary>
        public static string AppBackupFolderName { get; set; } = "PhisonUniGet";
        
        /// <summary>
        /// App identifier. Set at runtime from AppBranding.Identifier.
        /// Default: "Phison.PhisonUniGet"
        /// </summary>
        public static string AppIdentifierValue { get; set; } = "Phison.PhisonUniGet";
        
        // ═══════════════════════════════════════════════════════════════════════════
        
        public const string PublicWebsiteUrl = "https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore";
        public const string HelpBaseUrl = "https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore/wiki";
        public static string UserAgentString => $"{AppName}/{VersionName} ({PublicWebsiteUrl}; support@phison.com)";

        public static string AppIdentifier => AppIdentifierValue;
        public static string MainWindowIdentifier => $"{AppIdentifierValue}.MainInterface";

        private static bool? IS_PORTABLE;
        private static string? PORTABLE_PATH;
        public static bool IsPortable { get => IS_PORTABLE ?? false; }

        public static string? TEST_DataDirectoryOverride { private get; set; }

        /// <summary>
        /// The directory where all the user data is stored. The directory is automatically created if it does not exist.
        /// </summary>
        public static string UniGetUIDataDirectory
        {
            get
            {
                if (TEST_DataDirectoryOverride is not null)
                {
                    return TEST_DataDirectoryOverride;
                }

                if (IS_PORTABLE is null)
                {
                    IS_PORTABLE = File.Exists(Path.Join(UniGetUIExecutableDirectory, "ForceUniGetUIPortable"));

                    if (IS_PORTABLE is true)
                    {
                        string path = Path.Join(UniGetUIExecutableDirectory, "Settings");
                        try
                        {
                            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                            var testfilepath = Path.Join(path, "PermissionTestFile");
                            File.WriteAllText(testfilepath, "https://www.youtube.com/watch?v=dQw4w9WgXcQ");
                            PORTABLE_PATH = path;
                            return path;
                        }
                        catch (Exception ex)
                        {
                            IS_PORTABLE = false;
                            Logger.Error(
                                $"Could not acces/write path {path}. UniGetUI will NOT be run in portable mode, and User settings will be used instead");
                            Logger.Error(ex);
                        }
                    }
                }
                else if (IS_PORTABLE is true)
                {
                    return PORTABLE_PATH ?? throw new Exception("This shouldn't be possible");
                }

                string old_path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wingetui");
                string new_path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDataFolderName);
                return GetNewDataDirectoryOrMoveOld(old_path, new_path);
            }
        }

        /// <summary>
        /// The directory where the user configurations are stored. The directory is automatically created if it does not exist.
        /// </summary>
        public static string UniGetUIUserConfigurationDirectory
        {
            get
            {
                string oldConfigPath = UniGetUIDataDirectory;
                string newConfigPath = Path.Join(UniGetUIDataDirectory, "Configuration");

                if (Directory.Exists(oldConfigPath) && !Directory.Exists(newConfigPath))
                {
                    //Migration case
                    try
                    {
                        Logger.Info($"Moving configuration files from '{oldConfigPath}' to '{newConfigPath}'");
                        Directory.CreateDirectory(newConfigPath);

                        HashSet<string> appFiles = LoadApplicationFileNames(oldConfigPath);

                        foreach (string file in Directory.GetFiles(oldConfigPath, "*.*", SearchOption.TopDirectoryOnly))
                        {
                            string fileName = Path.GetFileName(file);
                            string fileExtension = Path.GetExtension(file);
                            bool isConfigFile = string.IsNullOrEmpty(fileExtension) || fileExtension.ToLowerInvariant() == ".json";

                            if (!isConfigFile || IsApplicationFile(fileName, appFiles))
                            {
                                Logger.Debug($"Skipping application/non-config file '{fileName}' during migration.");
                                continue;
                            }

                            string newFile = Path.Join(newConfigPath, fileName);
                            if (!File.Exists(newFile))
                            {
                                File.Move(file, newFile);
                                Logger.Debug($"Moved configuration file '{fileName}' to '{newFile}'");
                            }
                            else
                            {
                                Logger.Warn($"Configuration file '{newFile}' already exists, skipping move from '{file}'.");
                                File.Delete(file);
                            }
                        }
                        Logger.Info($"Configuration files moved successfully to '{newConfigPath}'");
                    }
                    catch (Exception ex)
                    {
                        // Fallback to old path if migration fails to not break functionality
                        Logger.Error($"Error moving configuration files from '{oldConfigPath}' to '{newConfigPath}'. Using old path for now. Manual migration might be needed.");
                        Logger.Error(ex);
                        return oldConfigPath;
                    }
                }
                else if (!Directory.Exists(newConfigPath))
                {
                    //New install case, migration not needed
                    Directory.CreateDirectory(newConfigPath);
                }
                return newConfigPath;
            }
        }

        /// <summary>
        /// Loads the set of top-level application file names from IntegrityTree.json.
        /// These files are part of the application package and must not be moved during config migration.
        /// </summary>
        private static HashSet<string> LoadApplicationFileNames(string directory)
        {
            var appFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "IntegrityTree.json"
            };

            string integrityTreePath = Path.Join(directory, "IntegrityTree.json");
            try
            {
                if (File.Exists(integrityTreePath))
                {
                    string rawData = File.ReadAllText(integrityTreePath);
                    var tree = JsonSerializer.Deserialize<Dictionary<string, string>>(rawData, CoreDataSerializationOptions);
                    if (tree != null)
                    {
                        foreach (var key in tree.Keys)
                        {
                            if (!key.Contains('/') && !key.Contains('\\'))
                            {
                                appFiles.Add(key);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not load IntegrityTree.json for migration exclusion: {ex.Message}");
            }

            return appFiles;
        }

        /// <summary>
        /// Determines whether a file is an application runtime file that should not be
        /// moved during configuration migration.
        /// </summary>
        private static bool IsApplicationFile(string fileName, HashSet<string> appFiles)
        {
            return appFiles.Contains(fileName)
                || fileName.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The directory where the installation options are stored. The directory is automatically created if it does not exist.
        /// </summary>
        public static string UniGetUIInstallationOptionsDirectory
        {
            get
            {
                string path = Path.Join(UniGetUIDataDirectory, "InstallationOptions");
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                return path;
            }
        }

        /// <summary>
        /// The directory where the metadata cache is stored. The directory is automatically created if it does not exist.
        /// </summary>
        public static string UniGetUICacheDirectory_Data
        {
            get
            {
                string path = Path.Join(UniGetUIDataDirectory, "CachedMetadata");
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                return path;
            }
        }

        /// <summary>
        /// The directory where the cached icons and screenshots are saved. The directory is automatically created if it does not exist.
        /// </summary>
        public static string UniGetUICacheDirectory_Icons
        {
            get
            {
                string path = Path.Join(UniGetUIDataDirectory, "CachedMedia");
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                return path;
            }
        }

        /// <summary>
        /// The directory where the cached language files are stored. The directory is automatically created if it does not exist.
        /// </summary>
        public static string UniGetUICacheDirectory_Lang
        {
            get
            {
                string path = Path.Join(UniGetUIDataDirectory, "CachedLanguageFiles");
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                return path;
            }
        }

        /// <summary>
        /// The directory where package backups will be saved by default.
        /// </summary>
        public static string UniGetUI_DefaultBackupDirectory
        {
            get
            {
                string old_dir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WingetUI");
                string new_dir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppBackupFolderName);
                return GetNewDataDirectoryOrMoveOld(old_dir, new_dir);
            }
        }

        public static bool IsDaemon;
        public static bool WasDaemon;

        /// <summary>
        /// The ID of the notification that is used to inform the user that updates are available
        /// </summary>
        public const int UpdatesAvailableNotificationTag = 1234;
        /// <summary>
        /// The ID of the notification that is used to inform the user that UniGetUI can be updated
        /// </summary>
        public const int UniGetUICanBeUpdated = 1235;
        /// <summary>
        /// The ID of the notification that is used to inform the user that shortcuts are available for deletion
        /// </summary>
        public const int NewShortcutsNotificationTag = 1236;

        // ═══════════════════════════════════════════════════════════════════════════
        // Valid Bucket List Configuration
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The default valid bucket used as fallback when no configuration is found.
        /// </summary>
        public static readonly ValidBucketInfo DefaultValidBucket = new()
        {
            Name = "aiDAPTIV-bucket",
            Url = "https://github.com/aiDAPTIV-Phison/aiDAPTIV-bucket"
        };

        private static List<ValidBucketInfo>? _validBucketList;
        private static HashSet<string>? _validBucketUrlsCache;
        private static readonly object _validBucketListLock = new();
        private const string ValidBucketListFileName = "ValidBucketList.json";

        /// <summary>
        /// Gets the list of valid buckets with name and URL. The list is loaded once and cached in memory.
        /// Priority: User configuration folder > Default from application assets.
        /// </summary>
        public static List<ValidBucketInfo> ValidBucketList
        {
            get
            {
                if (_validBucketList is not null)
                {
                    return _validBucketList;
                }

                lock (_validBucketListLock)
                {
                    // Double-check after acquiring lock
                    if (_validBucketList is not null)
                    {
                        return _validBucketList;
                    }

                    _validBucketList = LoadValidBucketList();
                    // Build URL cache for fast lookup
                    _validBucketUrlsCache = _validBucketList
                        .Select(b => b.NormalizedUrl.ToLowerInvariant())
                        .ToHashSet();
                    return _validBucketList;
                }
            }
        }

        /// <summary>
        /// Checks if the given URL is a valid bucket URL.
        /// Uses HashSet for O(1) lookup performance.
        /// </summary>
        /// <param name="url">The URL to check (can include or exclude trailing slash)</param>
        /// <returns>True if the URL is in the valid bucket list, false otherwise</returns>
        public static bool IsValidBucket(string? url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            // Ensure cache is initialized
            _ = ValidBucketList;

            string normalizedUrl = url.TrimEnd('/').ToLowerInvariant();
            return _validBucketUrlsCache?.Contains(normalizedUrl) ?? false;
        }

        /// <summary>
        /// Checks if the given URI is a valid bucket URL.
        /// </summary>
        /// <param name="uri">The URI to check</param>
        /// <returns>True if the URI is in the valid bucket list, false otherwise</returns>
        public static bool IsValidBucket(Uri? uri)
        {
            return uri is not null && IsValidBucket(uri.ToString());
        }

        /// <summary>
        /// Gets the bucket info for the given URL.
        /// </summary>
        /// <param name="url">The URL to look up</param>
        /// <returns>The ValidBucketInfo if found, null otherwise</returns>
        public static ValidBucketInfo? GetBucketInfo(string? url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            string normalizedUrl = url.TrimEnd('/');
            return ValidBucketList.FirstOrDefault(bucket =>
                bucket.NormalizedUrl.Equals(normalizedUrl, StringComparison.OrdinalIgnoreCase));
        }

        private static List<ValidBucketInfo> LoadValidBucketList()
        {
            // Default bucket list (fallback)
            var defaultList = new List<ValidBucketInfo> { DefaultValidBucket };

            try
            {
                // Priority 1: Check user configuration folder
                string userConfigPath = Path.Join(UniGetUIUserConfigurationDirectory, ValidBucketListFileName);
                if (File.Exists(userConfigPath))
                {
                    var userList = LoadBucketListFromFile(userConfigPath);
                    if (userList is not null && userList.Count > 0)
                    {
                        Logger.Info($"Loaded ValidBucketList from user configuration: {userConfigPath}");
                        return userList;
                    }
                }

                // Priority 2: Check application assets folder
                string assetsPath = Path.Join(UniGetUIExecutableDirectory, "Assets", "Data", ValidBucketListFileName);
                if (File.Exists(assetsPath))
                {
                    var assetList = LoadBucketListFromFile(assetsPath);
                    if (assetList is not null && assetList.Count > 0)
                    {
                        Logger.Info($"Loaded ValidBucketList from assets: {assetsPath}");
                        return assetList;
                    }
                }

                // Fallback: Use default list
                Logger.Warn($"ValidBucketList file not found, using default list");
                return defaultList;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading ValidBucketList, using default list: {ex.Message}");
                Logger.Error(ex);
                return defaultList;
            }
        }

        private static List<ValidBucketInfo>? LoadBucketListFromFile(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                var list = JsonSerializer.Deserialize<List<ValidBucketInfo>>(json, CoreDataSerializationOptions);
                if (list is not null && list.All(b => !string.IsNullOrEmpty(b.Name) && !string.IsNullOrEmpty(b.Url)))
                {
                    return list;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error reading ValidBucketList from {filePath}: {ex.Message}");
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A path pointing to the location where the app is installed
        /// </summary>
        public static string UniGetUIExecutableDirectory
        {
            get
            {
                string? dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (dir is not null)
                {
                    return dir;
                }

                Logger.Error("System.Reflection.Assembly.GetExecutingAssembly().Location returned an empty path");

                return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
            }
        }

        /// <summary>
        /// A path pointing to the executable file of the app
        /// </summary>
        public static string UniGetUIExecutableFile
        {
            get
            {
                string? filename = Process.GetCurrentProcess().MainModule?.FileName;
                if (filename is not null)
                {
                    return filename.Replace(".dll", ".exe");
                }

                Logger.Error("System.Reflection.Assembly.GetExecutingAssembly().Location returned an empty path");

                return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName, AppName + ".exe");
            }
        }

        public static string ElevatorPath = "";

        /// <summary>
        /// This method will return the most appropriate data directory.
        /// If the new directory exists, it will be used.
        /// If the new directory does not exist, but the old directory does, it will be moved to the new location, and the new location will be used.
        /// If none exist, the new directory will be created.
        /// </summary>
        /// <param name="old_path">The old/legacy directory</param>
        /// <param name="new_path">The new directory</param>
        /// <returns>The path to an existing, valid directory</returns>
        private static string GetNewDataDirectoryOrMoveOld(string old_path, string new_path)
        {
            if (Directory.Exists(new_path) && !Directory.Exists(old_path))
            {
                return new_path;
            }

            if (Directory.Exists(new_path) && Directory.Exists(old_path))
            {
                try
                {
                    foreach (string old_subdir in Directory.GetDirectories(old_path, "*", SearchOption.AllDirectories))
                    {
                        string new_subdir = old_subdir.Replace(old_path, new_path);
                        if (!Directory.Exists(new_subdir))
                        {
                            Logger.Debug("New directory: " + new_subdir);
                            Directory.CreateDirectory(new_subdir);
                        }
                        else
                        {
                            Logger.Debug("Directory " + new_subdir + " already exists");
                        }
                    }

                    foreach (string old_file in Directory.GetFiles(old_path, "*", SearchOption.AllDirectories))
                    {
                        string new_file = old_file.Replace(old_path, new_path);
                        if (!File.Exists(new_file))
                        {
                            Logger.Info("Copying " + old_file);
                            File.Move(old_file, new_file);
                        }
                        else
                        {
                            Logger.Debug("File " + new_file + " already exists.");
                            File.Delete(old_file);
                        }
                    }

                    foreach (string old_subdir in Directory.GetDirectories(old_path, "*", SearchOption.AllDirectories))
                    {
                        if (!Directory.EnumerateFiles(old_subdir).Any() && !Directory.EnumerateDirectories(old_subdir).Any())
                        {
                            Logger.Debug("Deleting old empty subdirectory " + old_subdir);
                            Directory.Delete(old_subdir);
                        }
                    }

                    if (!Directory.EnumerateFiles(old_path).Any() && !Directory.EnumerateDirectories(old_path).Any())
                    {
                        Logger.Info("Deleting old Chocolatey directory " + old_path);
                        Directory.Delete(old_path);
                    }

                    return new_path;
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                    return new_path;
                }
            }

            if (/*Directory.Exists(new_path)*/Directory.Exists(old_path))
            {
                try
                {
                    Directory.Move(old_path, new_path);
                    Task.Delay(100).Wait();
                    return new_path;
                }
                catch (Exception e)
                {
                    Logger.Error("Cannot move old data directory to new location. Directory to move: " + old_path + ". Destination: " + new_path);
                    Logger.Error(e);
                    return old_path;
                }
            }

            try
            {
                Logger.Debug("Creating non-existing data directory at: " + new_path);
                Directory.CreateDirectory(new_path);
                return new_path;
            }
            catch (Exception e)
            {
                Logger.Error("Could not create new directory. You may perhaps need to disable Controlled Folder Access from Windows Settings or make an exception for UniGetUI.");
                Logger.Error(e);
                return new_path;
            }
        }

        private static int GetCodePage()
        {
            try
            {
                using Process p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "chcp.com",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };
                p.Start();
                string contents = p.StandardOutput.ReadToEnd();
                string purifiedString = "";

                foreach (var c in contents.Split(':')[^1].Trim())
                {
                    if (c >= '0' && c <= '9')
                    {
                        purifiedString += c;
                    }
                }

                return int.Parse(purifiedString);
            }
            catch (Exception e)
            {
                Logger.Error(e);
                return 0;
            }
        }

        public static readonly string PowerShell5 = Path.Join(Environment.SystemDirectory, "windowspowershell\\v1.0\\powershell.exe");
    }
}
