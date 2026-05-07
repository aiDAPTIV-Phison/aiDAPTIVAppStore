using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using UniGetUI.Core.Logging;
using UniGetUI.Core.Tools;
using UniGetUI.Interface.Enums;
using UniGetUI.PhisonService;

namespace UniGetUI;

/// <summary>
/// UI-layer AI model update manager.
/// Collaborates with ModelManager (PhisonService) to handle InfoBar display, Toast notifications, and user interaction.
/// Both main model and embedding model must be valid before updating aidaptiv_config.json and restarting.
/// </summary>
public static class ModelUpdater
{
    public static Window Window = null!;
    public static InfoBar Banner = null!;
    public static bool ModelSwitchRequested;

    private static CancellationTokenSource? _downloadCts;

    private static RequiredModel? _pendingMainModel;
    private static RequiredModel? _pendingEmbeddingModel;
    private static string _pendingModelDir = "";
    private static string _aidaptivConfigPath = "";

    public static async Task ModelCheckLoop(Window window, InfoBar banner)
    {
        Window = window;
        Banner = banner;

        await Task.Delay(TimeSpan.FromSeconds(5));

        try
        {
            await CheckAndProcessModel();
        }
        catch (Exception ex)
        {
            Logger.Error("Error in ModelCheckLoop:");
            Logger.Error(ex);
        }
    }

    private static async Task CheckAndProcessModel()
    {
        var config = await Task.Run(ModelManager.LoadSystemCheckConfig);
        if (config?.ModelFilesCheck == null || !config.ModelFilesCheck.Enabled)
        {
            Logger.Info("Model files check is disabled or config not found");
            return;
        }

        var check = config.ModelFilesCheck;
        string modelDir = ModelManager.ResolveModelDirectory(check.ModelDirectory);
        string aidaptivConfigPath = ModelManager.GetAidaptivConfigPath();

        _pendingModelDir = modelDir;
        _aidaptivConfigPath = aidaptivConfigPath;

        // Require both model types to be defined
        var mainModel = check.RequiredModels.FirstOrDefault(m =>
            m.ModelType.Equals("main", StringComparison.OrdinalIgnoreCase));
        var embeddingModel = check.RequiredModels.FirstOrDefault(m =>
            m.ModelType.Equals("embedding", StringComparison.OrdinalIgnoreCase));

        if (mainModel == null)
        {
            Logger.Error("No model with modelType='main' found in aidaptiv_system_check.json");
            ShowMessage_ThreadSafe(
                CoreTools.Translate("AI model configuration error"),
                CoreTools.Translate("Main model definition is missing from configuration"),
                InfoBarSeverity.Error,
                true);
            return;
        }

        if (embeddingModel == null)
        {
            Logger.Error("No model with modelType='embedding' found in aidaptiv_system_check.json");
            ShowMessage_ThreadSafe(
                CoreTools.Translate("AI model configuration error"),
                CoreTools.Translate("Embedding model definition is missing from configuration"),
                InfoBarSeverity.Error,
                true);
            return;
        }

        if (!ValidateModelDefinition(mainModel) || !ValidateModelDefinition(embeddingModel))
        {
            return;
        }

        _pendingMainModel = mainModel;
        _pendingEmbeddingModel = embeddingModel;

        var mainAction = await Task.Run(() =>
            ModelManager.CheckModelStatus(mainModel, modelDir, aidaptivConfigPath));
        var embeddingAction = await Task.Run(() =>
            ModelManager.CheckModelStatus(embeddingModel, modelDir, aidaptivConfigPath));

        Logger.Info($"Model check: main={mainAction}, embedding={embeddingAction}");

        bool mainNeedsDownload = mainAction == ModelCheckAction.DownloadRequired;
        bool embeddingNeedsDownload = embeddingAction == ModelCheckAction.DownloadRequired;

        // Both are up to date and config paths already correct
        if (mainAction == ModelCheckAction.NoActionNeeded &&
            embeddingAction == ModelCheckAction.NoActionNeeded)
        {
            Logger.Info("Both models are up to date, cleaning up other models");
            var keepFiles = mainModel.Files.Select(f => f.FileName)
                .Concat(embeddingModel.Files.Select(f => f.FileName))
                .ToList();
            await Task.Run(() => ModelManager.CleanupOtherModels(modelDir, keepFiles));
            return;
        }

        // Download any models that need it (sequential: main first, then embedding)
        if (mainNeedsDownload)
        {
            bool success = await DownloadAndNotify(mainModel, modelDir, check.RetryCount);
            if (!success)
            {
                return;
            }
        }

        if (embeddingNeedsDownload)
        {
            bool success = await DownloadAndNotify(embeddingModel, modelDir, check.RetryCount);
            if (!success)
            {
                return;
            }
        }

        // All models are ready (downloaded or already valid), prompt user to switch
        NotifyAllModelsReady(mainModel, embeddingModel);
    }

    private static bool ValidateModelDefinition(RequiredModel model)
    {
        if (model.Files.Count == 0 || model.Files.Any(f =>
                string.IsNullOrEmpty(f.FileName) || string.IsNullOrEmpty(f.DownloadUrl)))
        {
            Logger.Error($"Model '{model.ModelName}' (type={model.ModelType}) has missing or incomplete file definitions");
            ShowMessage_ThreadSafe(
                CoreTools.Translate("AI model configuration error"),
                CoreTools.Translate("Model {0} has incomplete file definitions", model.ModelName),
                InfoBarSeverity.Error,
                true);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Downloads all files for a model and reports progress via InfoBar.
    /// Returns true on success, false on failure or cancellation.
    /// </summary>
    private static async Task<bool> DownloadAndNotify(RequiredModel model, string modelDir, int retryCount)
    {
        string displayName = model.ModelName;
        string fileCountInfo = model.Files.Count > 1
            ? $" ({model.Files.Count} files)"
            : "";

        ShowMessage_ThreadSafe(
            CoreTools.Translate("Downloading AI model {0}", displayName + fileCountInfo),
            CoreTools.Translate("This may take a while"),
            InfoBarSeverity.Informational,
            false);

        _downloadCts = new CancellationTokenSource();
        var progress = new Progress<ModelDownloadProgress>(p => OnDownloadProgress(p, model));

        try
        {
            bool success = await ModelManager.DownloadAllModelFilesAsync(
                model, modelDir, retryCount, progress, _downloadCts.Token);

            if (!success)
            {
                Logger.Error($"Model {displayName} download failed");

                var retryButton = new Button { Content = CoreTools.Translate("Retry") };
                retryButton.Click += async (_, _) =>
                {
                    await DownloadAndNotify(model, modelDir, retryCount);
                };

                ShowMessage_ThreadSafe(
                    CoreTools.Translate("AI model download failed"),
                    CoreTools.Translate("Please check your network connection and try again"),
                    InfoBarSeverity.Error,
                    true,
                    retryButton);
            }

            return success;
        }
        catch (OperationCanceledException)
        {
            Logger.Info($"Model download cancelled: {displayName}");
            ShowMessage_ThreadSafe(
                CoreTools.Translate("AI model download cancelled"),
                "",
                InfoBarSeverity.Warning,
                true);
            return false;
        }
    }

    private static void OnDownloadProgress(ModelDownloadProgress p, RequiredModel model)
    {
        string title;
        string message;

        string fileIndicator = p.TotalFileCount > 1
            ? $" [{p.CurrentFileIndex}/{p.TotalFileCount}]"
            : "";

        if (p.CurrentRetry > 0 && p.SpeedBytesPerSecond <= 0)
        {
            title = CoreTools.Translate("AI model download interrupted, retrying ({0}/{1})...",
                p.CurrentRetry.ToString(), p.MaxRetries.ToString());
            message = $"{ModelManager.FormatSize(p.BytesDownloaded)} / {ModelManager.FormatSize(p.TotalBytes)} ({p.Percentage:0.0}%)";
        }
        else if (p.IsResuming)
        {
            title = CoreTools.Translate("Resuming AI model download") + fileIndicator;
            message = $"{ModelManager.FormatSize(p.BytesDownloaded)} / {ModelManager.FormatSize(p.TotalBytes)} ({p.Percentage:0.0}%) - {ModelManager.FormatSpeed(p.SpeedBytesPerSecond)}";
        }
        else
        {
            title = CoreTools.Translate("Downloading AI model {0}", model.ModelName) + fileIndicator;
            string etaStr = p.EstimatedTimeRemaining > TimeSpan.Zero
                ? $" - {CoreTools.Translate("Est. remaining")}: {ModelManager.FormatETA(p.EstimatedTimeRemaining)}"
                : "";
            message = $"{ModelManager.FormatSize(p.BytesDownloaded)} / {ModelManager.FormatSize(p.TotalBytes)} ({p.Percentage:0.0}%) - {ModelManager.FormatSpeed(p.SpeedBytesPerSecond)}{etaStr}";
        }

        ShowMessage_ThreadSafe(title, message, InfoBarSeverity.Informational, false);
    }

    private static void NotifyAllModelsReady(RequiredModel mainModel, RequiredModel embeddingModel)
    {
        Window.DispatcherQueue.TryEnqueue(() =>
        {
            var switchButton = new Button { Content = CoreTools.Translate("Update now") };
            switchButton.Click += (_, _) => PerformModelSwitch(mainModel, embeddingModel);

            ShowMessage_ThreadSafe(
                CoreTools.Translate("AI models are ready"),
                CoreTools.Translate("New models available: {0}, {1}", mainModel.ModelName, embeddingModel.ModelName),
                InfoBarSeverity.Success,
                true,
                switchButton);

            try
            {
                var builder = new AppNotificationBuilder()
                    .SetScenario(AppNotificationScenario.Default)
                    .SetTag("ModelUpdateAvailable")
                    .AddText(CoreTools.Translate("AI model update available"))
                    .SetAttributionText(CoreTools.Translate("New AI models are ready: {0}, {1}",
                        mainModel.ModelName, embeddingModel.ModelName))
                    .AddArgument("action", NotificationArguments.Show)
                    .AddButton(new AppNotificationButton(CoreTools.Translate("Update now"))
                        .AddArgument("action", NotificationArguments.SwitchModel));

                AppNotification notification = builder.BuildNotification();
                notification.ExpiresOnReboot = true;
                AppNotificationManager.Default.Show(notification);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to show toast notification: {ex.Message}");
            }
        });
    }

    public static void PerformModelSwitch()
    {
        if (_pendingMainModel != null && _pendingEmbeddingModel != null)
        {
            PerformModelSwitch(_pendingMainModel, _pendingEmbeddingModel);
        }
    }

    private static void PerformModelSwitch(RequiredModel mainModel, RequiredModel embeddingModel)
    {
        try
        {
            string mainFileName = mainModel.Files[0].FileName;
            string embeddingFileName = embeddingModel.Files[0].FileName;

            Logger.Info($"Switching models: main={mainFileName}, embedding={embeddingFileName}");

            bool updated = ModelManager.UpdateModelPaths(_aidaptivConfigPath, mainFileName, embeddingFileName);
            if (!updated)
            {
                ShowMessage_ThreadSafe(
                    CoreTools.Translate("Failed to update model configuration"),
                    CoreTools.Translate("Please try again later"),
                    InfoBarSeverity.Error,
                    true);
                return;
            }

            var keepFiles = mainModel.Files.Select(f => f.FileName)
                .Concat(embeddingModel.Files.Select(f => f.FileName))
                .ToList();
            ModelManager.CleanupOtherModels(_pendingModelDir, keepFiles);

            ShowMessage_ThreadSafe(
                CoreTools.Translate("Model updated successfully, restarting..."),
                "",
                InfoBarSeverity.Success,
                false);

            Task.Delay(1500).ContinueWith(_ =>
            {
                Window.DispatcherQueue.TryEnqueue(() =>
                {
                    MainApp.Instance.KillAndRestart();
                });
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"Error switching models: {ex.Message}");
            Logger.Error(ex);
            ShowMessage_ThreadSafe(
                CoreTools.Translate("Failed to switch model"),
                ex.Message,
                InfoBarSeverity.Error,
                true);
        }
    }

    private static void ShowMessage_ThreadSafe(
        string title, string message, InfoBarSeverity severity,
        bool closable, Button? actionButton = null)
    {
        try
        {
            if (Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread() is null)
            {
                Window.DispatcherQueue.TryEnqueue(() =>
                    ShowMessage_ThreadSafe(title, message, severity, closable, actionButton));
                return;
            }

            Banner.Title = title;
            Banner.Message = message;
            Banner.Severity = severity;
            Banner.IsClosable = closable;
            Banner.ActionButton = actionButton;
            Banner.IsOpen = true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
        }
    }
}
