using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using UniGetUI.Controls.OperationWidgets;
using UniGetUI.Core.Logging;
using UniGetUI.Core.Tools;
using UniGetUI.Interface.Widgets;
using UniGetUI.PackageEngine.Interfaces;
using UniGetUI.PackageEngine.Operations;
using UniGetUI.PackageEngine.PackageClasses;
using UniGetUI.PackageOperations;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace UniGetUI.Pages.DialogPages;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class OperationFailedDialog : Page
{
    public event EventHandler<EventArgs>? Close;
    Paragraph par;

    private static SolidColorBrush errorColor = null!;
    private static SolidColorBrush debugColor = null!;

    public OperationFailedDialog(AbstractOperation operation, OperationControl opControl)
    {
        this.InitializeComponent();

        errorColor ??= (SolidColorBrush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        debugColor ??= (SolidColorBrush)Application.Current.Resources["SystemFillColorNeutralBrush"];

        headerContent.Text = $"{operation.Metadata.FailureMessage}.\n"
           + CoreTools.Translate("Please see the Command-line Output or refer to the Operation History for further information about the issue.");

        // Load install error hint from package details if available
        // Use Loaded event to ensure UI is ready before async loading
        this.Loaded += async (_, _) =>
        {
            Logger.Warn("[ErrorHint] Page Loaded event fired, starting LoadInstallErrorHintAsync...");
            try
            {
                await LoadInstallErrorHintAsync(operation);
            }
            catch (Exception ex)
            {
                Logger.Error($"[ErrorHint] Exception in Loaded handler: {ex}");
            }
        };

        par = new Paragraph();
        foreach (var line in operation.GetOutput())
        {
            if (line.Item2 is AbstractOperation.LineType.Information)
            {
                par.Inlines.Add(new Run { Text = line.Item1 + "\x0a" });
            }
            else if (line.Item2 is AbstractOperation.LineType.VerboseDetails)
            {
                par.Inlines.Add(new Run { Text = line.Item1 + "\x0a", Foreground = debugColor });
            }
            else
            {
                par.Inlines.Add(new Run { Text = line.Item1 + "\x0a", Foreground = errorColor });
            }
        }

        CommandLineOutput.Blocks.Add(par);

        var CloseButton = new Button
        {
            Content = CoreTools.Translate("Close"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 30,
        };
        CloseButton.Click += (_, _) => Close?.Invoke(this, EventArgs.Empty);

        Control _retryButton;

        var retryOptions = opControl.GetRetryOptions(() => Close?.Invoke(this, EventArgs.Empty));
        if (retryOptions.Count != 0)
        {
            SplitButton RetryButton = new SplitButton
            {
                Content = CoreTools.Translate("Retry"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = 30,
            };
            RetryButton.Click += (_, _) =>
            {
                operation.Retry(AbstractOperation.RetryMode.Retry);
                Close?.Invoke(this, EventArgs.Empty);
            };
            BetterMenu menu = new();
            RetryButton.Flyout = menu;
            foreach (var opt in retryOptions)
            {
                menu.Items.Add(opt);
            }

            _retryButton = RetryButton;
        }
        else
        {
            var RetryButton = new Button
            {
                Content = CoreTools.Translate("Retry"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = 30,
            };
            RetryButton.Click += (_, _) =>
            {
                operation.Retry(AbstractOperation.RetryMode.Retry);
                Close?.Invoke(this, EventArgs.Empty);
            };
            _retryButton = RetryButton;
        }

        ButtonsLayout.Children.Add(CloseButton);
        ButtonsLayout.Children.Add(_retryButton);
        Grid.SetColumn(CloseButton, 1);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Loads the install error hint from package details if available and displays it in the InfoBar.
    /// Supports conditional hints based on error patterns in the command output.
    /// </summary>
    private async Task LoadInstallErrorHintAsync(AbstractOperation operation)
    {
        try
        {
            Logger.Warn("[ErrorHint] LoadInstallErrorHintAsync started");

            // Check if the operation is a PackageOperation
            if (operation is not PackageOperation packageOperation)
            {
                Logger.Warn("[ErrorHint] Operation is not a PackageOperation, skipping.");
                return;
            }

            var package = packageOperation.Package;
            Logger.Warn($"[ErrorHint] Loading hints for package {package.Id}, Manager: {package.Manager.Name}");

            // Get the command output for pattern matching
            var outputLines = operation.GetOutput();
            string commandOutput = string.Join("\n", outputLines.Select(l => l.Item1));
            Logger.Warn($"[ErrorHint] Command output length = {commandOutput.Length}");

            // Try to load error hints directly from manifest file (for Scoop)
            string? hintToShow = null;

            if (package.Manager.Name.Equals("Scoop", StringComparison.OrdinalIgnoreCase))
            {
                hintToShow = await LoadScoopErrorHintDirectly(package, commandOutput);
            }
            else
            {
                // For other managers, use the standard details loading
                var details = new PackageDetails(package);
                Logger.Warn("[ErrorHint] Calling details.Load()...");
                await details.Load();
                Logger.Warn($"[ErrorHint] details.Load() completed. IsPopulated = {details.IsPopulated}");

                hintToShow = FindMatchingHint(details.InstallErrorHints, details.InstallErrorHint, commandOutput);
            }

            // Check if there is any hint to show
            if (string.IsNullOrWhiteSpace(hintToShow))
            {
                Logger.Warn("[ErrorHint] No hint to show.");
                return;
            }

            Logger.Warn($"[ErrorHint] Showing hint: {hintToShow}");

            // Update UI directly (we're already on UI thread from Loaded event)
            InstallErrorHintBar.Title = CoreTools.Translate("Troubleshooting Hint");
            InstallErrorHintBar.Message = hintToShow;
            InstallErrorHintBar.IsOpen = true;
            InstallErrorHintBar.Visibility = Visibility.Visible;
            Logger.Warn("[ErrorHint] InfoBar updated successfully.");
        }
        catch (Exception ex)
        {
            Logger.Error($"[ErrorHint] Failed to load install error hint: {ex.Message}");
            Logger.Error(ex);
        }
    }

    /// <summary>
    /// Directly reads the Scoop manifest JSON file to get error hints.
    /// </summary>
    private async Task<string?> LoadScoopErrorHintDirectly(IPackage package, string commandOutput)
    {
        try
        {
            // Build the manifest file path
            // Format: %USERPROFILE%\scoop\buckets\{bucket_name}\bucket\{package_id}.json
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string bucketName = package.Source.Name;
            string manifestPath = Path.Combine(userProfile, "scoop", "buckets", bucketName, "bucket", $"{package.Id}.json");

            Logger.Warn($"[ErrorHint] Looking for Scoop manifest at: {manifestPath}");

            if (!File.Exists(manifestPath))
            {
                Logger.Warn($"[ErrorHint] Manifest file not found at: {manifestPath}");
                return null;
            }

            // Read and parse the JSON file
            string jsonContent = await File.ReadAllTextAsync(manifestPath);
            Logger.Warn($"[ErrorHint] Manifest file read, length = {jsonContent.Length}");

            if (JsonNode.Parse(jsonContent) is not JsonObject contents)
            {
                Logger.Warn("[ErrorHint] Failed to parse manifest JSON");
                return null;
            }

            // Load conditional error hints
            List<IPackageDetails.ErrorHintRule> errorHints = [];
            if (contents["install_error_hints"] is JsonArray hintsArray)
            {
                Logger.Warn($"[ErrorHint] Found install_error_hints array with {hintsArray.Count} items");
                foreach (var hintItem in hintsArray)
                {
                    if (hintItem is JsonObject hintObj)
                    {
                        string? pattern = hintObj["pattern"]?.ToString();
                        string? hint = null;

                        if (hintObj["hint"] is JsonArray hintLines)
                        {
                            hint = string.Join("\n", hintLines.Select(l => l?.ToString() ?? ""));
                        }
                        else
                        {
                            hint = hintObj["hint"]?.ToString();
                        }

                        if (!string.IsNullOrWhiteSpace(pattern) && !string.IsNullOrWhiteSpace(hint))
                        {
                            Logger.Warn($"[ErrorHint] Added error hint rule: pattern='{pattern}'");
                            errorHints.Add(new IPackageDetails.ErrorHintRule { Pattern = pattern, Hint = hint });
                        }
                    }
                }
            }

            // Load default error hint
            string? defaultHint = null;
            if (contents["install_error_hint"] is JsonArray defaultHintList)
            {
                defaultHint = string.Join("\n", defaultHintList.Select(l => l?.ToString() ?? ""));
                Logger.Warn($"[ErrorHint] Loaded default hint (array): {defaultHint}");
            }
            else if (contents["install_error_hint"] is not null)
            {
                defaultHint = contents["install_error_hint"]?.ToString();
                Logger.Warn($"[ErrorHint] Loaded default hint: {defaultHint}");
            }

            return FindMatchingHint(errorHints, defaultHint, commandOutput);
        }
        catch (Exception ex)
        {
            Logger.Error($"[ErrorHint] Error reading Scoop manifest directly: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Finds a matching hint based on command output patterns.
    /// </summary>
    private string? FindMatchingHint(IEnumerable<IPackageDetails.ErrorHintRule> hints, string? defaultHint, string commandOutput)
    {
        foreach (var rule in hints)
        {
            Logger.Warn($"[ErrorHint] Checking pattern '{rule.Pattern}'");
            if (!string.IsNullOrWhiteSpace(rule.Pattern) &&
                commandOutput.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warn($"[ErrorHint] Pattern '{rule.Pattern}' matched!");
                return rule.Hint;
            }
        }

        Logger.Warn($"[ErrorHint] No pattern matched, using default hint: {defaultHint ?? "(null)"}");
        return defaultHint;
    }
}
