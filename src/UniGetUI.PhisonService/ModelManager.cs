using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using UniGetUI.Core.Logging;
using UniGetUI.Core.Tools;

namespace UniGetUI.PhisonService
{
    #region Configuration Models

    public class SystemCheckConfig
    {
        [JsonPropertyName("modelFilesCheck")]
        public ModelFilesCheck? ModelFilesCheck { get; set; }
    }

    public class ModelFilesCheck
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("modelDirectory")]
        public string ModelDirectory { get; set; } = "";

        [JsonPropertyName("requiredModels")]
        public List<RequiredModel> RequiredModels { get; set; } = new();

        [JsonPropertyName("verifyChecksum")]
        public bool VerifyChecksum { get; set; }

        [JsonPropertyName("retryCount")]
        public int RetryCount { get; set; } = 3;
    }

    public class ModelFile
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("md5Hash")]
        public string Md5Hash { get; set; } = "";

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public class RequiredModel
    {
        [JsonPropertyName("modelType")]
        public string ModelType { get; set; } = "main";

        [JsonPropertyName("modelName")]
        public string ModelName { get; set; } = "";

        [JsonPropertyName("files")]
        public List<ModelFile> Files { get; set; } = new();

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";
    }

    public class DownloadMetadata
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = "";

        [JsonPropertyName("expectedSize")]
        public long ExpectedSize { get; set; }

        [JsonPropertyName("startedAt")]
        public DateTime StartedAt { get; set; }

        [JsonPropertyName("lastUpdatedAt")]
        public DateTime LastUpdatedAt { get; set; }

        [JsonPropertyName("retryCount")]
        public int RetryCount { get; set; }
    }

    #endregion

    #region Progress and Result Types

    public class ModelDownloadProgress
    {
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public double SpeedBytesPerSecond { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
        public double Percentage => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100.0 : 0;
        public bool IsResuming { get; set; }
        public int CurrentRetry { get; set; }
        public int MaxRetries { get; set; }
        public int CurrentFileIndex { get; set; }
        public int TotalFileCount { get; set; }
        public string CurrentFileName { get; set; } = "";
    }

    public enum ModelCheckAction
    {
        NoActionNeeded,
        SwitchAvailable,
        DownloadRequired,
        ConfigError,
    }

    #endregion

    [JsonSerializable(typeof(SystemCheckConfig))]
    [JsonSerializable(typeof(ModelFilesCheck))]
    [JsonSerializable(typeof(RequiredModel))]
    [JsonSerializable(typeof(List<RequiredModel>))]
    [JsonSerializable(typeof(ModelFile))]
    [JsonSerializable(typeof(List<ModelFile>))]
    [JsonSerializable(typeof(DownloadMetadata))]
    internal partial class ModelJsonContext : JsonSerializerContext
    {
    }

    /// <summary>
    /// AI model management service: handles checking, downloading, validating model files,
    /// and updating configuration. Supports cross-restart resume, stall detection, and disk space pre-check.
    /// </summary>
    public static class ModelManager
    {
        private const int BUFFER_SIZE = 8 * 1024 * 1024; // 8MB
        private const int PROGRESS_REPORT_INTERVAL_SECONDS = 2;
        private const int STALL_TIMEOUT_SECONDS = 120;
        private static readonly int[] RETRY_DELAYS_SECONDS = [10, 30, 60];

        private static readonly JsonSerializerOptions ReadSerializationOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            TypeInfoResolver = ModelJsonContext.Default
        };

        private static readonly JsonSerializerOptions WriteSerializationOptions = new()
        {
            WriteIndented = true,
            TypeInfoResolver = ModelJsonContext.Default
        };

        #region Path Resolution

        public static string GetSystemCheckConfigPath()
        {
            return Path.Combine(AppContext.BaseDirectory, "aidaptiv_system_check.json");
        }

        public static string GetAidaptivConfigPath()
        {
            return Path.Combine(AppContext.BaseDirectory, "appstore", "aiDAPTIV", "aidaptiv_config.json");
        }

        public static string ResolveModelDirectory(string modelDirectory)
        {
            string expanded = Environment.ExpandEnvironmentVariables(modelDirectory);
            if (!Path.IsPathRooted(expanded))
            {
                return Path.Combine(AppContext.BaseDirectory, expanded);
            }
            return expanded;
        }

        #endregion

        #region Config Management

        /// <summary>
        /// Reads the model-check configuration from the aidaptiv_system_check.json
        /// file shipped alongside the executable.
        /// </summary>
        public static SystemCheckConfig? LoadSystemCheckConfig()
        {
            try
            {
                string configPath = GetSystemCheckConfigPath();

                if (!File.Exists(configPath))
                {
                    Logger.Error($"aidaptiv_system_check.json not found at: {configPath}");
                    return null;
                }

                string json = File.ReadAllText(configPath);
                Logger.Info($"Loaded aidaptiv_system_check.json from: {configPath}");

                return JsonSerializer.Deserialize<SystemCheckConfig>(json, ReadSerializationOptions);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load aidaptiv_system_check.json: {ex.Message}");
                Logger.Error(ex);
                return null;
            }
        }

        #endregion

        #region Model Validation

        public static ModelCheckAction CheckModelStatus(RequiredModel model, string modelDir, string aidaptivConfigPath)
        {
            if (model.Files.Count == 0)
            {
                Logger.Warn($"Model {model.ModelName} has no files defined");
                return ModelCheckAction.ConfigError;
            }

            bool allFilesValid = model.Files.All(f =>
                ValidateModelFile(Path.Combine(modelDir, f.FileName), f.Size));

            if (allFilesValid)
            {
                // Read the config field that corresponds to this model type
                string configFieldName = model.ModelType.Equals("embedding", StringComparison.OrdinalIgnoreCase)
                    ? "embedding_model_path"
                    : "model_path";

                string currentModelPath = ReadCurrentModelPath(aidaptivConfigPath, configFieldName);
                string currentFileName = Path.GetFileName(currentModelPath);
                string firstFileName = model.Files[0].FileName;

                if (currentFileName.Equals(firstFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return ModelCheckAction.NoActionNeeded;
                }
                else
                {
                    return ModelCheckAction.SwitchAvailable;
                }
            }
            else
            {
                return ModelCheckAction.DownloadRequired;
            }
        }

        public static bool ValidateModelFile(string filePath, long expectedSize)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return false;
                }

                var fileInfo = new FileInfo(filePath);
                if (expectedSize > 0 && fileInfo.Length != expectedSize)
                {
                    Logger.Warn($"Model file size mismatch: expected {expectedSize}, got {fileInfo.Length}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error validating model file: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Config Read/Write

        public static string ReadCurrentModelPath(string configPath, string fieldName = "model_path")
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    Logger.Warn($"Config file not found: {configPath}");
                    return "";
                }

                string json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });

                if (doc.RootElement.TryGetProperty(fieldName, out var modelPathElement))
                {
                    return modelPathElement.GetString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to read {fieldName} from config: {ex.Message}");
            }

            return "";
        }

        public static bool UpdateModelPath(string configPath, string newModelPath)
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    Logger.Error($"Config file not found: {configPath}");
                    return false;
                }

                string json = File.ReadAllText(configPath);
                var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
                if (node == null)
                {
                    Logger.Error("Failed to parse aidaptiv_config.json");
                    return false;
                }

                string normalizedPath = newModelPath.Replace("\\", "/");
                node["model_path"] = normalizedPath;

                var writeOptions = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(configPath, node.ToJsonString(writeOptions));
                Logger.Info($"Updated model_path to: {normalizedPath}");

                UpdateIntegrityTreeHash(configPath);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to update model_path: {ex.Message}");
                Logger.Error(ex);
                return false;
            }
        }

        /// <summary>
        /// Updates both model_path and embedding_model_path in aidaptiv_config.json
        /// using the relative path format "./aidaptiv/models/&lt;fileName&gt;".
        /// Calls UpdateIntegrityTreeHash once after both fields are written.
        /// </summary>
        public static bool UpdateModelPaths(string configPath, string mainFileName, string embeddingFileName)
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    Logger.Error($"Config file not found: {configPath}");
                    return false;
                }

                string json = File.ReadAllText(configPath);
                var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
                if (node == null)
                {
                    Logger.Error("Failed to parse aidaptiv_config.json");
                    return false;
                }

                string mainRelPath = $"./aidaptiv/models/{mainFileName}";
                string embeddingRelPath = $"./aidaptiv/models/{embeddingFileName}";

                node["model_path"] = mainRelPath;
                node["embedding_model_path"] = embeddingRelPath;

                var writeOptions = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(configPath, node.ToJsonString(writeOptions));
                Logger.Info($"Updated model_path to: {mainRelPath}");
                Logger.Info($"Updated embedding_model_path to: {embeddingRelPath}");

                UpdateIntegrityTreeHash(configPath);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to update model paths: {ex.Message}");
                Logger.Error(ex);
                return false;
            }
        }

        /// <summary>
        /// After modifying a file under the application directory, recalculate its MD5
        /// and update the corresponding entry in IntegrityTree.json so the integrity
        /// checker does not report a false mismatch.
        /// </summary>
        public static void UpdateIntegrityTreeHash(string modifiedFilePath)
        {
            try
            {
                string appDir = AppContext.BaseDirectory;
                string integrityTreePath = Path.Combine(appDir, "IntegrityTree.json");

                if (!File.Exists(integrityTreePath))
                {
                    Logger.Debug("IntegrityTree.json not found, skipping hash update");
                    return;
                }

                string relativePath = Path.GetRelativePath(appDir, modifiedFilePath)
                    .Replace("\\", "/");

                string treeJson = File.ReadAllText(integrityTreePath);
                var treeNode = JsonNode.Parse(treeJson);
                if (treeNode == null)
                {
                    Logger.Warn("Failed to parse IntegrityTree.json for hash update");
                    return;
                }

                if (treeNode[relativePath] == null)
                {
                    Logger.Debug($"File {relativePath} not tracked in IntegrityTree.json, skipping");
                    return;
                }

                string newHash = ComputeMD5(modifiedFilePath);
                treeNode[relativePath] = newHash;

                File.WriteAllText(integrityTreePath,
                    treeNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                Logger.Info($"Updated IntegrityTree.json hash for {relativePath} to {newHash}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to update IntegrityTree.json: {ex.Message}");
            }
        }

        private static string ComputeMD5(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hashBytes = md5.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        #endregion

        #region Download Engine

        public static bool CheckDiskSpace(string directory, long requiredBytes)
        {
            try
            {
                string root = Path.GetPathRoot(directory) ?? "C:\\";
                var driveInfo = new DriveInfo(root);
                long needed = requiredBytes + (1L * 1024 * 1024 * 1024); // +1GB buffer

                if (driveInfo.AvailableFreeSpace < needed)
                {
                    Logger.Warn($"Insufficient disk space. Required: {FormatSize(needed)}, Available: {FormatSize(driveInfo.AvailableFreeSpace)}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to check disk space: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Orchestrates downloading all files for a multi-file model.
        /// Skips files already present and valid. Reports aggregate progress across all files.
        /// </summary>
        public static async Task<bool> DownloadAllModelFilesAsync(
            RequiredModel model,
            string destDir,
            int retryCount,
            IProgress<ModelDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(destDir);

            long totalSize = model.Files.Sum(f => f.Size);
            long completedSize = 0;

            long totalRemainingBytes = model.Files
                .Where(f => !ValidateModelFile(Path.Combine(destDir, f.FileName), f.Size))
                .Sum(f => f.Size);

            if (totalRemainingBytes > 0 && !CheckDiskSpace(destDir, totalRemainingBytes))
            {
                Logger.Error($"Insufficient disk space for model download. Need {FormatSize(totalRemainingBytes)}");
                return false;
            }

            for (int i = 0; i < model.Files.Count; i++)
            {
                var file = model.Files[i];
                string filePath = Path.Combine(destDir, file.FileName);

                if (ValidateModelFile(filePath, file.Size))
                {
                    Logger.Info($"File {file.FileName} already valid, skipping ({i + 1}/{model.Files.Count})");
                    completedSize += file.Size;
                    continue;
                }

                long capturedCompletedSize = completedSize;
                int fileIndex = i;
                var fileProgress = new Progress<ModelDownloadProgress>(p =>
                {
                    long aggregateDownloaded = capturedCompletedSize + p.BytesDownloaded;
                    long remaining = totalSize - aggregateDownloaded;
                    TimeSpan eta = p.SpeedBytesPerSecond > 0
                        ? TimeSpan.FromSeconds(remaining / p.SpeedBytesPerSecond)
                        : TimeSpan.Zero;

                    progress?.Report(new ModelDownloadProgress
                    {
                        BytesDownloaded = aggregateDownloaded,
                        TotalBytes = totalSize,
                        SpeedBytesPerSecond = p.SpeedBytesPerSecond,
                        EstimatedTimeRemaining = eta,
                        IsResuming = p.IsResuming,
                        CurrentRetry = p.CurrentRetry,
                        MaxRetries = p.MaxRetries,
                        CurrentFileIndex = fileIndex + 1,
                        TotalFileCount = model.Files.Count,
                        CurrentFileName = file.FileName,
                    });
                });

                bool success = await DownloadSingleFileAsync(
                    file, destDir, retryCount, fileProgress, cancellationToken);

                if (!success)
                {
                    Logger.Error($"Failed to download file {file.FileName} ({i + 1}/{model.Files.Count})");
                    return false;
                }

                completedSize += file.Size;
            }

            Logger.Info($"All {model.Files.Count} file(s) for model {model.ModelName} downloaded successfully");
            return true;
        }

        /// <summary>
        /// Downloads a single model file with retry and resume support.
        /// </summary>
        public static async Task<bool> DownloadSingleFileAsync(
            ModelFile file,
            string destDir,
            int retryCount,
            IProgress<ModelDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(destDir);

            string finalPath = Path.Combine(destDir, file.FileName);
            string tempPath = finalPath + ".downloading";
            string metaPath = finalPath + ".download-meta.json";

            DownloadMetadata? metadata = LoadDownloadMetadata(metaPath);
            int startRetry = 0;

            if (metadata != null && metadata.DownloadUrl != file.DownloadUrl)
            {
                Logger.Info("Download URL changed, starting fresh download");
                TryDeleteFile(tempPath);
                TryDeleteFile(metaPath);
                metadata = null;
            }

            if (metadata != null)
            {
                startRetry = metadata.RetryCount;
                Logger.Info($"Resuming download from previous session (retry count: {startRetry})");
            }

            for (int retry = startRetry; retry <= retryCount; retry++)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    long existingSize = 0;
                    if (File.Exists(tempPath))
                    {
                        existingSize = new FileInfo(tempPath).Length;

                        if (file.Size > 0 && existingSize >= file.Size)
                        {
                            Logger.Info("Temp file already has expected size, completing download");
                            File.Move(tempPath, finalPath, overwrite: true);
                            TryDeleteFile(metaPath);
                            return true;
                        }
                    }

                    long remainingBytes = file.Size > 0 ? file.Size - existingSize : file.Size;
                    if (remainingBytes > 0 && !CheckDiskSpace(destDir, remainingBytes))
                    {
                        Logger.Error($"Insufficient disk space for model download. Need {FormatSize(remainingBytes)}");
                        return false;
                    }

                    SaveDownloadMetadata(metaPath, new DownloadMetadata
                    {
                        FileName = file.FileName,
                        DownloadUrl = file.DownloadUrl,
                        ExpectedSize = file.Size,
                        StartedAt = metadata?.StartedAt ?? DateTime.UtcNow,
                        LastUpdatedAt = DateTime.UtcNow,
                        RetryCount = retry,
                    });

                    bool isResuming = existingSize > 0;
                    if (isResuming)
                    {
                        Logger.Info($"Resuming download from byte {existingSize} ({FormatSize(existingSize)})");
                    }

                    using var client = new HttpClient(CoreTools.GenericHttpClientParameters);
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("aiDAPTIVAppStore/1.0");

                    using var request = new HttpRequestMessage(HttpMethod.Get, file.DownloadUrl);
                    if (existingSize > 0)
                    {
                        request.Headers.Range = new RangeHeaderValue(existingSize, null);
                    }

                    using var response = await client.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                    FileMode fileMode;
                    long totalExpected = file.Size;

                    if (existingSize > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent)
                    {
                        fileMode = FileMode.Append;
                        Logger.Info($"Server supports resume, continuing from {FormatSize(existingSize)}");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.OK
                          || response.StatusCode == System.Net.HttpStatusCode.Found
                          || response.StatusCode == System.Net.HttpStatusCode.Redirect)
                    {
                        fileMode = FileMode.Create;
                        existingSize = 0;
                        if (response.Content.Headers.ContentLength.HasValue)
                        {
                            totalExpected = response.Content.Headers.ContentLength.Value;
                        }
                        Logger.Info($"Starting fresh download, total size: {FormatSize(totalExpected)}");
                    }
                    else
                    {
                        response.EnsureSuccessStatusCode();
                        return false;
                    }

                    await StreamDownloadToFile(
                        response, tempPath, fileMode, existingSize, totalExpected,
                        file, retry, retryCount, isResuming, metadata,
                        progress, cancellationToken);

                    long finalSize = new FileInfo(tempPath).Length;
                    if (totalExpected > 0 && finalSize < totalExpected)
                    {
                        throw new IOException($"Download incomplete: {finalSize}/{totalExpected} bytes");
                    }

                    File.Move(tempPath, finalPath, overwrite: true);
                    TryDeleteFile(metaPath);

                    Logger.Info($"File download completed successfully: {file.FileName} ({FormatSize(finalSize)})");
                    return true;
                }
                catch (OperationCanceledException)
                {
                    Logger.Info("Model download was cancelled");
                    throw;
                }
                catch (Exception ex) when (retry < retryCount)
                {
                    int delayIdx = Math.Min(retry, RETRY_DELAYS_SECONDS.Length - 1);
                    int delaySeconds = RETRY_DELAYS_SECONDS[delayIdx];
                    Logger.Warn($"Download attempt {retry + 1}/{retryCount + 1} failed: {ex.Message}. Retrying in {delaySeconds}s...");

                    progress?.Report(new ModelDownloadProgress
                    {
                        BytesDownloaded = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0,
                        TotalBytes = file.Size,
                        CurrentRetry = retry + 1,
                        MaxRetries = retryCount,
                    });

                    SaveDownloadMetadata(metaPath, new DownloadMetadata
                    {
                        FileName = file.FileName,
                        DownloadUrl = file.DownloadUrl,
                        ExpectedSize = file.Size,
                        StartedAt = metadata?.StartedAt ?? DateTime.UtcNow,
                        LastUpdatedAt = DateTime.UtcNow,
                        RetryCount = retry + 1,
                    });

                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Download failed after all retries: {ex.Message}");
                    Logger.Error(ex);
                    return false;
                }
            }

            return false;
        }

        private static async Task StreamDownloadToFile(
            HttpResponseMessage response,
            string tempPath,
            FileMode fileMode,
            long existingSize,
            long totalExpected,
            ModelFile file,
            int retry,
            int retryCount,
            bool isResuming,
            DownloadMetadata? metadata,
            IProgress<ModelDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(tempPath, fileMode, FileAccess.Write, FileShare.None, BUFFER_SIZE);

            byte[] buffer = new byte[BUFFER_SIZE];
            long totalRead = existingSize;
            DateTime lastProgressTime = DateTime.UtcNow;
            long lastProgressBytes = totalRead;
            DateTime lastReportTime = DateTime.UtcNow;

            var speedSamples = new Queue<(DateTime time, long bytes)>();
            speedSamples.Enqueue((DateTime.UtcNow, totalRead));
            DateTime latestSampleTime = DateTime.UtcNow;
            long latestSampleBytes = totalRead;

            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(
                       buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                DateTime now = DateTime.UtcNow;

                if (totalRead > lastProgressBytes)
                {
                    lastProgressTime = now;
                    lastProgressBytes = totalRead;
                }
                else if ((now - lastProgressTime).TotalSeconds >= STALL_TIMEOUT_SECONDS)
                {
                    await fileStream.FlushAsync(cancellationToken);
                    throw new TimeoutException($"Download stalled for {STALL_TIMEOUT_SECONDS} seconds");
                }

                if ((now - lastReportTime).TotalSeconds >= PROGRESS_REPORT_INTERVAL_SECONDS)
                {
                    speedSamples.Enqueue((now, totalRead));
                    latestSampleTime = now;
                    latestSampleBytes = totalRead;

                    while (speedSamples.Count > 1 && (now - speedSamples.Peek().time).TotalSeconds > 60)
                    {
                        speedSamples.Dequeue();
                    }

                    double speed = CalculateSpeed(speedSamples, latestSampleTime, latestSampleBytes);
                    TimeSpan eta = CalculateETA(totalRead, totalExpected, speed);

                    progress?.Report(new ModelDownloadProgress
                    {
                        BytesDownloaded = totalRead,
                        TotalBytes = totalExpected,
                        SpeedBytesPerSecond = speed,
                        EstimatedTimeRemaining = eta,
                        IsResuming = isResuming,
                        CurrentRetry = retry,
                        MaxRetries = retryCount,
                    });

                    isResuming = false;
                    lastReportTime = now;

                    SaveDownloadMetadata(metaPath: Path.Combine(
                        Path.GetDirectoryName(tempPath) ?? "",
                        file.FileName + ".download-meta.json"),
                        new DownloadMetadata
                        {
                            FileName = file.FileName,
                            DownloadUrl = file.DownloadUrl,
                            ExpectedSize = totalExpected,
                            StartedAt = metadata?.StartedAt ?? DateTime.UtcNow,
                            LastUpdatedAt = now,
                            RetryCount = retry,
                        });
                }
            }

            await fileStream.FlushAsync(cancellationToken);
        }

        #endregion

        #region Cleanup

        public static void CleanupOtherModels(string modelDir, IReadOnlyList<string> keepFileNames)
        {
            try
            {
                if (!Directory.Exists(modelDir))
                {
                    return;
                }

                var keepSet = new HashSet<string>(keepFileNames, StringComparer.OrdinalIgnoreCase);

                foreach (var file in Directory.GetFiles(modelDir))
                {
                    string fileName = Path.GetFileName(file);
                    if (keepSet.Contains(fileName)
                        || fileName.EndsWith(".downloading", StringComparison.OrdinalIgnoreCase)
                        || fileName.EndsWith(".download-meta.json", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        File.Delete(file);
                        Logger.Info($"Cleaned up old model: {fileName}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to delete old model {fileName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during model cleanup: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        private static DownloadMetadata? LoadDownloadMetadata(string metaPath)
        {
            try
            {
                if (!File.Exists(metaPath))
                {
                    return null;
                }

                string json = File.ReadAllText(metaPath);
                return JsonSerializer.Deserialize<DownloadMetadata>(json, ReadSerializationOptions);
            }
            catch
            {
                return null;
            }
        }

        private static void SaveDownloadMetadata(string metaPath, DownloadMetadata metadata)
        {
            try
            {
                File.WriteAllText(metaPath, JsonSerializer.Serialize(metadata, WriteSerializationOptions));
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to save download metadata: {ex.Message}");
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }

        private static double CalculateSpeed(
            Queue<(DateTime time, long bytes)> samples,
            DateTime latestTime, long latestBytes)
        {
            if (samples.Count < 2)
            {
                return 0;
            }

            var oldest = samples.Peek();
            double seconds = (latestTime - oldest.time).TotalSeconds;
            if (seconds <= 0)
            {
                return 0;
            }

            return (latestBytes - oldest.bytes) / seconds;
        }

        private static TimeSpan CalculateETA(long downloaded, long total, double speedBps)
        {
            if (speedBps <= 0 || total <= downloaded)
            {
                return TimeSpan.Zero;
            }

            double remainingSeconds = (total - downloaded) / speedBps;
            if (remainingSeconds > TimeSpan.MaxValue.TotalSeconds - 1)
            {
                return TimeSpan.MaxValue;
            }

            return TimeSpan.FromSeconds(remainingSeconds);
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 0)
            {
                return "0 B";
            }

            string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return $"{size:0.#} {suffixes[order]}";
        }

        public static string FormatSpeed(double bytesPerSecond)
        {
            return $"{FormatSize((long)bytesPerSecond)}/s";
        }

        public static string FormatETA(TimeSpan eta)
        {
            if (eta == TimeSpan.Zero || eta == TimeSpan.MaxValue)
            {
                return "Calculating...";
            }

            if (eta.TotalHours >= 1)
            {
                return $"{(int)eta.TotalHours}h {eta.Minutes}m";
            }

            if (eta.TotalMinutes >= 1)
            {
                return $"{(int)eta.TotalMinutes}m {eta.Seconds}s";
            }

            return $"{(int)eta.TotalSeconds}s";
        }

        #endregion
    }
}
