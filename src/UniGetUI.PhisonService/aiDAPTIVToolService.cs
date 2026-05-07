using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using UniGetUI.Core.Data;
using UniGetUI.Core.Logging;

namespace UniGetUI.PhisonService
{
    /// <summary>
    /// JSON Serializer Context for aiDAPTIV API responses
    /// </summary>
    [JsonSerializable(typeof(aiDAPTIVToolService.StartResponse))]
    [JsonSerializable(typeof(aiDAPTIVToolService.StopResponse))]
    [JsonSerializable(typeof(aiDAPTIVToolService.StatusResponse))]
    [JsonSerializable(typeof(aiDAPTIVToolService.ErrorInfo))]
    [JsonSerializable(typeof(aiDAPTIVToolService.StorageResponse))]
    [JsonSerializable(typeof(aiDAPTIVToolService.DiskInfo))]
    [JsonSerializable(typeof(aiDAPTIVToolService.KvCacheInfo))]
    [JsonSerializable(typeof(aiDAPTIVToolService.CacheGroupInfo))]
    [JsonSerializable(typeof(aiDAPTIVToolService.KvCacheSummary))]
    internal partial class AiDAPTIVJsonContext : JsonSerializerContext
    {
    }

    /// <summary>
    /// aiDAPTIV Tool Service Management
    /// </summary>
    public class aiDAPTIVToolService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(300)
        };
        private const string BaseUrlLlma = "http://127.0.0.1:13140/llama";
        private const string BaseUrlEmbedding = "http://127.0.0.1:13140/embedding";

        /// <summary>
        /// JSON serialization options for aiDAPTIV API responses
        /// </summary>
        private static readonly JsonSerializerOptions SerializationOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            TypeInfoResolver = AiDAPTIVJsonContext.Default
        };

        /// <summary>
        /// Response model for aiDAPTIV service start operation
        /// </summary>
        public class StartResponse
        {
            public bool Ok { get; set; }
            public bool Started { get; set; }
            public bool Ready { get; set; }
            public ErrorInfo? Error { get; set; }
        }

        /// <summary>
        /// Response model for aiDAPTIV service stop operation
        /// </summary>
        public class StopResponse
        {
            public bool Ok { get; set; }
            public bool Stopped { get; set; }
            public int TimeoutSeconds { get; set; }
        }

        /// <summary>
        /// Response model for aiDAPTIV service status query
        /// </summary>
        public class StatusResponse
        {
            public bool Ok { get; set; }
            public bool Running { get; set; }
            public bool Ready { get; set; }
            public int Port { get; set; }
        }

        /// <summary>
        /// Error information model
        /// </summary>
        public class ErrorInfo
        {
            public string Code { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public bool Retryable { get; set; }
        }

        /// <summary>
        /// Response model for GET /system/storage
        /// </summary>
        public class StorageResponse
        {
            public bool Ok { get; set; }
            [JsonPropertyName("kv_cache_path")]
            public string KvCachePath { get; set; } = string.Empty;
            public DiskInfo? Disk { get; set; }
            [JsonPropertyName("kv_cache")]
            public KvCacheInfo? KvCache { get; set; }
            public string Source { get; set; } = string.Empty;
            public ErrorInfo? Error { get; set; }
        }

        public class DiskInfo
        {
            public string Drive { get; set; } = string.Empty;
            [JsonPropertyName("total_bytes")]
            public long TotalBytes { get; set; }
            [JsonPropertyName("used_bytes")]
            public long UsedBytes { get; set; }
            [JsonPropertyName("free_bytes")]
            public long FreeBytes { get; set; }
            [JsonPropertyName("usage_percent")]
            public double UsagePercent { get; set; }
        }

        public class KvCacheInfo
        {
            [JsonPropertyName("size_bytes")]
            public long SizeBytes { get; set; }
            [JsonPropertyName("file_count")]
            public int FileCount { get; set; }
            [JsonPropertyName("cache_groups")]
            public List<CacheGroupInfo> CacheGroups { get; set; } = new();
            public KvCacheSummary? Summary { get; set; }
        }

        public class CacheGroupInfo
        {
            public string Hash { get; set; } = string.Empty;
            public string Location { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public bool Complete { get; set; }
            [JsonPropertyName("size_bytes")]
            public long SizeBytes { get; set; }
        }

        public class KvCacheSummary
        {
            [JsonPropertyName("total_groups")]
            public int TotalGroups { get; set; }
            [JsonPropertyName("complete_groups")]
            public int CompleteGroups { get; set; }
            [JsonPropertyName("incomplete_groups")]
            public int IncompleteGroups { get; set; }
        }

        /// <summary>
        /// Starts the aiDAPTIV service (legacy)
        /// </summary>
        /// <returns>A tuple containing the operation result, error message, and error code (if any)</returns>
        public static async Task<(bool success, string? errorMessage, string? errorCode)> aiDAPTIVStartLegacy()
        {
            string url = $"{BaseUrlLlma}/start_with_legacy";
            Logger.Info($"Starting aiDAPTIV service at: {url}");

            try
            {
                var response = await _httpClient.PostAsync(url, null);
                string jsonResponse = await response.Content.ReadAsStringAsync();
                Logger.Info($"aiDAPTIV start response: {jsonResponse}");

                var result = JsonSerializer.Deserialize<StartResponse>(jsonResponse, SerializationOptions);

                if (result != null)
                {
                    if (result.Ok)
                    {
                        Logger.Info("aiDAPTIV service started successfully");
                        return (true, null, null);
                    }
                    else
                    {
                        string errorCode = string.IsNullOrWhiteSpace(result.Error?.Code)
                            ? $"HTTP_{(int)response.StatusCode}"
                            : result.Error!.Code;
                        string errorMessage = result.Error?.Message ?? "Unknown error";
                        Logger.Error($"Failed to start aiDAPTIV service: [{errorCode}] {errorMessage}");
                        return (false, errorMessage, errorCode);
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorResult = JsonSerializer.Deserialize<ErrorInfo>(jsonResponse, SerializationOptions);
                    string errorCode = !string.IsNullOrWhiteSpace(errorResult?.Code)
                        ? errorResult!.Code
                        : $"HTTP_{(int)response.StatusCode}";
                    string errorMessage = errorResult?.Message ?? response.ReasonPhrase ?? "Request failed";
                    Logger.Error($"Failed to start aiDAPTIV service: [{errorCode}] {errorMessage}");
                    return (false, errorMessage, errorCode);
                }
                else
                {
                    Logger.Error("Failed to start aiDAPTIV service: [PARSE_ERROR] Failed to parse response");
                    return (false, "Failed to parse response", "PARSE_ERROR");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception when starting aiDAPTIV service: [EXCEPTION] {ex.Message}");
                return (false, ex.Message, "EXCEPTION");
            }
        }

        /// <summary>
        /// Starts the aiDAPTIV service
        /// </summary>
        /// <returns>A tuple containing the operation result, error message, and error code (if any)</returns>
        public static async Task<(bool success, string? errorMessage, string? errorCode)> aiDAPTIVStart()
        {
            string url = $"{BaseUrlLlma}/start_from_config";
            Logger.Info($"Starting aiDAPTIV service at: {url}");

            try
            {
                var response = await _httpClient.PostAsync(url, null);
                string jsonResponse = await response.Content.ReadAsStringAsync();
                Logger.Info($"aiDAPTIV start response: {jsonResponse}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorResult = JsonSerializer.Deserialize<ErrorInfo>(jsonResponse, SerializationOptions);
                    string errorCode = !string.IsNullOrWhiteSpace(errorResult?.Code)
                        ? errorResult!.Code
                        : $"HTTP_{(int)response.StatusCode}";
                    string errorMessage = errorResult?.Message ?? response.ReasonPhrase ?? "Request failed";
                    Logger.Error($"Failed to start aiDAPTIV service: [{errorCode}] {errorMessage}");
                    return (false, errorMessage, errorCode);
                }

                var result = JsonSerializer.Deserialize<StartResponse>(jsonResponse, SerializationOptions);

                if (result != null && result.Ok)
                {
                    Logger.Info("aiDAPTIV service started successfully");
                    return (true, null, null);
                }

                string responseCode = !string.IsNullOrWhiteSpace(result?.Error?.Code)
                    ? result!.Error!.Code
                    : "UNEXPECTED_RESPONSE";
                string responseMessage = result?.Error?.Message ?? "Unexpected response format";
                Logger.Error($"Failed to start aiDAPTIV service: [{responseCode}] {responseMessage}");
                return (false, responseMessage, responseCode);
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception when starting aiDAPTIV service: [EXCEPTION] {ex.Message}");
                return (false, ex.Message, "EXCEPTION");
            }
        }

        /// <summary>
        /// Stops the aiDAPTIV service
        /// </summary>
        /// <returns>A tuple containing the operation result and error message (if any)</returns>
        public static async Task<(bool success, string? errorMessage)> aiDAPTIVStop()
        {
            string url = $"{BaseUrlLlma}/stop";
            Logger.Info($"Stopping aiDAPTIV service at: {url}");

            try
            {
                var response = await _httpClient.PostAsync(url, null);
                string jsonResponse = await response.Content.ReadAsStringAsync();
                Logger.Info($"aiDAPTIV stop response: {jsonResponse}");

                var result = JsonSerializer.Deserialize<StopResponse>(jsonResponse, SerializationOptions);

                if (result != null)
                {
                    if (result.Ok)
                    {
                        Logger.Info("aiDAPTIV service stop initiated successfully");
                        return (true, null);
                    }
                    else
                    {
                        string errorMessage = "Failed to stop aiDAPTIV service";
                        Logger.Error(errorMessage);
                        return (false, errorMessage);
                    }
                }
                else
                {
                    string errorMessage = "Failed to parse stop response";
                    Logger.Error(errorMessage);
                    return (false, errorMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception when stopping aiDAPTIV service: {ex.Message}");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Queries the status of aiDAPTIV service
        /// </summary>
        /// <returns>Service status response, or null if failed</returns>
        public static async Task<StatusResponse?> aiDAPTIVStatus()
        {
            string url = $"{BaseUrlLlma}/status";
            Logger.Info($"Checking aiDAPTIV service status at: {url}");

            try
            {
                var response = await _httpClient.GetAsync(url);
                string jsonResponse = await response.Content.ReadAsStringAsync();
                Logger.Info($"aiDAPTIV status response: {jsonResponse}");

                var result = JsonSerializer.Deserialize<StatusResponse>(jsonResponse, SerializationOptions);

                if (result != null)
                {
                    Logger.Info($"aiDAPTIV service status - Ok: {result.Ok}, Running: {result.Running}, Ready: {result.Ready}, Port: {result.Port}");
                }
                else
                {
                    Logger.Error("Failed to deserialize status response");
                }

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception when checking aiDAPTIV service status: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Starts the aiDAPTIV embedding service
        /// </summary>
        /// <returns>A tuple containing the operation result and error message (if any)</returns>
        public static async Task<(bool success, string? errorMessage)> aiDAPTIVEmbeddingStart()
        {
            string url = $"{BaseUrlEmbedding}/start_from_config";
            Logger.Info($"Starting aiDAPTIV embedding service at: {url}");

            try
            {
                var response = await _httpClient.PostAsync(url, null);
                string jsonResponse = await response.Content.ReadAsStringAsync();
                Logger.Info($"aiDAPTIV embedding start response: {jsonResponse}");

                // Check for non-success HTTP status codes (e.g., 409 Conflict)
                if (!response.IsSuccessStatusCode)
                {
                    var errorResult = JsonSerializer.Deserialize<ErrorInfo>(jsonResponse, SerializationOptions);
                    string errorMessage = errorResult?.Message ?? $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                    Logger.Error($"Failed to start aiDAPTIV embedding service: {errorMessage}");
                    return (false, errorMessage);
                }

                var result = JsonSerializer.Deserialize<StartResponse>(jsonResponse, SerializationOptions);

                if (result != null && result.Ok)
                {
                    Logger.Info("aiDAPTIV embedding service started successfully");
                    return (true, null);
                }

                // Handle unexpected response format
                string unexpectedError = result?.Error?.Message ?? "Unexpected response format";
                Logger.Error($"Failed to start aiDAPTIV embedding service: {unexpectedError}");
                return (false, unexpectedError);
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception when starting aiDAPTIV embedding service: {ex.Message}");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Stops the aiDAPTIV embedding service
        /// </summary>
        /// <returns>A tuple containing the operation result and error message (if any)</returns>
        public static async Task<(bool success, string? errorMessage)> aiDAPTIVEmbeddingStop()
        {
            string url = $"{BaseUrlEmbedding}/stop";
            Logger.Info($"Stopping aiDAPTIV embedding service at: {url}");

            try
            {
                var response = await _httpClient.PostAsync(url, null);
                string jsonResponse = await response.Content.ReadAsStringAsync();
                Logger.Info($"aiDAPTIV embedding stop response: {jsonResponse}");

                var result = JsonSerializer.Deserialize<StopResponse>(jsonResponse, SerializationOptions);

                if (result != null)
                {
                    if (result.Ok)
                    {
                        Logger.Info("aiDAPTIV embedding service stop initiated successfully");
                        return (true, null);
                    }
                    else
                    {
                        string errorMessage = "Failed to stop aiDAPTIV embedding service";
                        Logger.Error(errorMessage);
                        return (false, errorMessage);
                    }
                }
                else
                {
                    string errorMessage = "Failed to parse embedding stop response";
                    Logger.Error(errorMessage);
                    return (false, errorMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception when stopping aiDAPTIV embedding service: {ex.Message}");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Queries the status of aiDAPTIV embedding service
        /// </summary>
        /// <returns>Embedding service status response, or null if failed</returns>
        public static async Task<StatusResponse?> aiDAPTIVEmbeddingStatus()
        {
            string url = $"{BaseUrlEmbedding}/status";
            Logger.Info($"Checking aiDAPTIV embedding service status at: {url}");

            try
            {
                var response = await _httpClient.GetAsync(url);
                string jsonResponse = await response.Content.ReadAsStringAsync();
                Logger.Info($"aiDAPTIV embedding status response: {jsonResponse}");

                var result = JsonSerializer.Deserialize<StatusResponse>(jsonResponse, SerializationOptions);

                if (result != null)
                {
                    Logger.Info($"aiDAPTIV embedding service status - Ok: {result.Ok}, Running: {result.Running}, Ready: {result.Ready}, Port: {result.Port}");
                }
                else
                {
                    Logger.Error("Failed to deserialize embedding status response");
                }

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception when checking aiDAPTIV embedding service status: {ex.Message}");
                return null;
            }
        }

        private const string BaseUrlSystem = "http://127.0.0.1:13140/system";

        /// <summary>
        /// Queries storage information from aiDAPTIV service
        /// </summary>
        /// <returns>Storage response, or null if failed</returns>
        public static async Task<StorageResponse?> GetStorageInfo()
        {
            string url = $"{BaseUrlSystem}/storage";
            Logger.Info($"Checking storage info at: {url}");

            try
            {
                var response = await _httpClient.GetAsync(url);
                string jsonResponse = await response.Content.ReadAsStringAsync();
                Logger.Info($"Storage info response: {jsonResponse}");

                var result = JsonSerializer.Deserialize<StorageResponse>(jsonResponse, SerializationOptions);

                if (result != null)
                {
                    Logger.Info($"Storage info - Ok: {result.Ok}, Free: {result.Disk?.FreeBytes} bytes, Cache groups: {result.KvCache?.CacheGroups?.Count ?? 0}");
                }
                else
                {
                    Logger.Error("Failed to deserialize storage response");
                }

                return result;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Exception when querying storage info: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads offload_size from local aidaptiv_config.json
        /// </summary>
        /// <returns>Offload size in GB, or 0 if unavailable</returns>
        public static int ReadOffloadSizeFromConfig()
        {
            try
            {
                string configPath = Path.Combine(AppContext.BaseDirectory, "appstore", "aiDAPTIV", "aidaptiv_config.json");
                Logger.Info($"Reading offload_size from config: {configPath}");

                if (!File.Exists(configPath))
                {
                    Logger.Warn($"Config file not found: {configPath}");
                    return 0;
                }

                string jsonContent = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(jsonContent);

                if (doc.RootElement.TryGetProperty("offload_size", out var offloadElement))
                {
                    int offloadSize = offloadElement.GetInt32();
                    Logger.Info($"offload_size from config: {offloadSize} GB");

                    if (offloadSize <= 0)
                    {
                        Logger.Info($"offload_size is {offloadSize}, SSD offload space check will be skipped");
                        return 0;
                    }

                    return offloadSize;
                }

                Logger.Info("offload_size not found in config, SSD offload space check will be skipped");
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Exception when reading offload_size from config: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Validates that available storage meets the SSD offload requirement.
        /// Formula: dynamic_cache_total + disk_free_bytes >= offload_size_gb * 1024^3
        /// Fails open: returns (true, null) if the check cannot be performed.
        /// </summary>
        /// <returns>A tuple: (sufficient, errorMessage). sufficient=true means pass or skip.</returns>
        public static async Task<(bool sufficient, string? errorMessage)> ValidateSsdOffloadSpace()
        {
            Logger.Info("Starting SSD offload space validation...");

            int offloadSizeGb = ReadOffloadSizeFromConfig();
            if (offloadSizeGb <= 0)
            {
                Logger.Info("SSD offload space check skipped (offload_size is 0 or unavailable)");
                return (true, null);
            }

            var storageInfo = await GetStorageInfo();
            if (storageInfo == null)
            {
                Logger.Warn("SSD offload space check skipped (storage API unavailable)");
                return (true, null);
            }

            if (!storageInfo.Ok)
            {
                Logger.Warn($"SSD offload space check skipped (storage API returned error: {storageInfo.Error?.Message ?? "unknown"})");
                return (true, null);
            }

            if (storageInfo.Disk == null)
            {
                Logger.Warn("SSD offload space check skipped (no disk info in response)");
                return (true, null);
            }

            long dynamicTotal = storageInfo.KvCache?.CacheGroups?
                .Where(g => string.Equals(g.Status, "dynamic", StringComparison.OrdinalIgnoreCase))
                .Sum(g => g.SizeBytes) ?? 0;

            long freeBytes = storageInfo.Disk.FreeBytes;
            long availableBytes = dynamicTotal + freeBytes;
            long requiredBytes = (long)offloadSizeGb * 1024L * 1024L * 1024L;

            double dynamicGb = dynamicTotal / (1024.0 * 1024.0 * 1024.0);
            double freeGb = freeBytes / (1024.0 * 1024.0 * 1024.0);
            double availableGb = availableBytes / (1024.0 * 1024.0 * 1024.0);

            Logger.Info($"SSD offload space check - Required: {offloadSizeGb} GB, Available: {availableGb:F2} GB (Dynamic cache: {dynamicGb:F2} GB + Free disk: {freeGb:F2} GB)");

            if (availableBytes >= requiredBytes)
            {
                Logger.Info("SSD offload space check passed");
                return (true, null);
            }

            string errorMessage = $"Insufficient disk space for SSD offload. Required: {offloadSizeGb} GB, Available: {availableGb:F2} GB (Dynamic cache: {dynamicGb:F2} GB + Free disk: {freeGb:F2} GB).";
            Logger.Error(errorMessage);
            return (false, errorMessage);
        }
    }
}

