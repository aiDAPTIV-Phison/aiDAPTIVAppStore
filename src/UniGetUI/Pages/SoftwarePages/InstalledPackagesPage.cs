using System.Linq;
using Windows.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using UniGetUI.Core.Data;
using UniGetUI.Core.Logging;
using UniGetUI.Core.SettingsEngine;
using UniGetUI.Core.Tools;
using UniGetUI.Interface.Enums;
using UniGetUI.Interface.Telemetry;
using UniGetUI.Interface.Widgets;
using UniGetUI.PackageEngine;
using UniGetUI.PackageEngine.Enums;
using UniGetUI.PackageEngine.Interfaces;
using UniGetUI.PackageEngine.Managers.WingetManager;
using UniGetUI.PackageEngine.PackageLoader;
using UniGetUI.Pages.DialogPages;
using UniGetUI.PhisonService;
using UniGetUI.Services;
using UniGetUI.Core.Language;

namespace UniGetUI.Interface.SoftwarePages
{
    public partial class InstalledPackagesPage : AbstractPackagesPage
    {
        private static bool HasDoneBackup;

        private BetterMenuItem? MenuAsAdmin;
        private BetterMenuItem? MenuRemoveData;
        private BetterMenuItem? MenuReinstallPackage;
        private BetterMenuItem? MenuUninstallThenReinstall;
        private BetterMenuItem? MenuIgnoreUpdates;
        private BetterMenuItem? MenuPackageDetails;
        private BetterMenuItem? MenuOpenInstallLocation;
        private BetterMenuItem? MenuStartService;
        private BetterMenuItem? MenuStopService;

        public InstalledPackagesPage()
        : base(new PackagesPageData
        {
            DisableAutomaticPackageLoadOnStart = false,
            DisableFilterOnQueryChange = false,
            MegaQueryBlockEnabled = false,
            ShowLastLoadTime = false,
            DisableReload = false,
            PackagesAreCheckedByDefault = false,
            DisableSuggestedResultsRadio = true,
            PageName = "Installed",

            Loader = InstalledPackagesLoader.Instance,
            PageRole = OperationType.Uninstall,

            NoPackages_BackgroundText = CoreTools.Translate("No results were found matching the input criteria"),
            NoPackages_SourcesText = CoreTools.Translate("No packages were found"),
            NoPackages_SubtitleText_Base = CoreTools.Translate("No packages were found"),
            MainSubtitle_StillLoading = CoreTools.Translate("Loading packages"),
            NoMatches_BackgroundText = CoreTools.Translate("No results were found matching the input criteria"),

            PageTitle = CoreTools.Translate("Installed Packages"),
            Glyph = "\uE977"
        })
        {
            // Override ReloadButton behavior to also refresh sources tree
            Loaded += (_, _) =>
            {
                ReloadButton.Click += async (_, _) =>
                {
                    await LoadPackages();
                    RebuildSourcesTree();
                    SelectAiDAPTIVBucketSource();
                    FilterPackages();
                };
            };
        }

        public override BetterMenu GenerateContextMenu()
        {
            BetterMenu menu = new();

            // Service management menu items (only for aiDAPTIV-bucket packages)
            // Determine text based on current language: Chinese shows "啟動程式"/"關閉程式", others show "Start"/"Stop"
            string currentLocale = LanguageEngine.SelectedLocale;
            bool isChinese = currentLocale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            string startServiceText = isChinese ? "啟動程式" : "Start";
            string stopServiceText = isChinese ? "關閉程式" : "Stop";

            // Create Start Service menu item (directly starts aiDAPTIV mode)
            MenuStartService = new()
            {
                Text = CoreTools.AutoTranslated(startServiceText),
                Icon = new FontIcon
                {
                    Glyph = "\uE768", // Play icon from Segoe Fluent Icons
                    FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
                    FontSize = 24
                }
            };
            MenuStartService.Click += MenuStartService_Invoked;
            menu.Items.Add(MenuStartService);

            MenuStopService = new()
            {
                Text = CoreTools.AutoTranslated(stopServiceText),
                Icon = new FontIcon
                {
                    Glyph = "\uE71A", // Stop icon from Segoe Fluent Icons
                    FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
                    FontSize = 24
                }
            };
            MenuStopService.Click += MenuStopService_Invoked;
            menu.Items.Add(MenuStopService);

            BetterMenuItem menuUninstall = new()
            {
                Text = CoreTools.AutoTranslated("Uninstall"),
                IconName = IconType.Delete,
                KeyboardAcceleratorTextOverride = "Ctrl+Enter"
            };
            menuUninstall.Click += MenuUninstall_Invoked;
            menu.Items.Add(menuUninstall);

            menu.Items.Add(new MenuFlyoutSeparator { Height = 5 });

            MenuOpenInstallLocation = new()
            {
                Text = CoreTools.AutoTranslated("Open install location"),
                IconName = IconType.Launch,
            };
            MenuOpenInstallLocation.Click += (_, _) => OpenPackageInstallLocation(SelectedItem); ;
            menu.Items.Add(MenuOpenInstallLocation);

            menu.Items.Add(new MenuFlyoutSeparator());

            MenuAsAdmin = new BetterMenuItem
            {
                Text = CoreTools.AutoTranslated("Uninstall as administrator"),
                IconName = IconType.UAC
            };
            MenuAsAdmin.Click += MenuAsAdmin_Invoked;
            menu.Items.Add(MenuAsAdmin);

            MenuRemoveData = new BetterMenuItem
            {
                Text = CoreTools.AutoTranslated("Uninstall and remove data"),
                IconName = IconType.Close_Round
            };
            MenuRemoveData.Click += MenuRemoveData_Invoked;
            menu.Items.Add(MenuRemoveData);

            menu.Items.Add(new MenuFlyoutSeparator());

            MenuReinstallPackage = new()
            {
                Text = CoreTools.AutoTranslated("Reinstall package"),
                IconName = IconType.Download
            };
            MenuReinstallPackage.Click += MenuReinstall_Invoked;
            menu.Items.Add(MenuReinstallPackage);

            MenuUninstallThenReinstall = new()
            {
                Text = CoreTools.AutoTranslated("Uninstall package, then reinstall it"),
                IconName = IconType.Undelete
            };
            MenuUninstallThenReinstall.Click += MenuUninstallThenReinstall_Invoked;
            menu.Items.Add(MenuUninstallThenReinstall);
            menu.Items.Add(new MenuFlyoutSeparator());

            MenuIgnoreUpdates = new()
            {
                Text = CoreTools.AutoTranslated("Ignore updates for this package"),
                IconName = IconType.Pin
            };
            MenuIgnoreUpdates.Click += MenuIgnorePackage_Invoked;
            menu.Items.Add(MenuIgnoreUpdates);

            menu.Items.Add(new MenuFlyoutSeparator());

            MenuPackageDetails = new()
            {
                Text = CoreTools.AutoTranslated("Package details"),
                IconName = IconType.Info_Round,
                KeyboardAcceleratorTextOverride = "Enter"
            };
            MenuPackageDetails.Click += MenuDetails_Invoked;
            menu.Items.Add(MenuPackageDetails);

            return menu;
        }

        public override void GenerateToolBar()
        {
            BetterMenuItem UninstallAsAdmin = new();
            BetterMenuItem UninstallInteractive = new();
            BetterMenuItem DownloadInstallers = new();

            MainToolbarButtonDropdown.Flyout = new BetterMenu()
            {
                Items =
                {
                    UninstallAsAdmin,
                    UninstallInteractive,
                    new MenuFlyoutSeparator(),
                    DownloadInstallers,
                },
                Placement = FlyoutPlacementMode.Bottom
            };
            MainToolbarButtonIcon.Icon = IconType.Delete;
            MainToolbarButtonText.Text = CoreTools.Translate("Uninstall selection");

            AppBarButton InstallationSettings = new();

            AppBarButton PackageDetails = new();
            AppBarButton SharePackage = new();

            AppBarButton IgnoreSelected = new();
            AppBarButton ManageIgnored = new();
            // 隱藏套件組合按鈕 - 移除 ExportSelection 按鈕
            // AppBarButton ExportSelection = new();

            AppBarButton HelpButton = new();

            ToolBar.PrimaryCommands.Add(new AppBarSeparator());
            ToolBar.PrimaryCommands.Add(InstallationSettings);
            ToolBar.PrimaryCommands.Add(new AppBarSeparator());
            ToolBar.PrimaryCommands.Add(PackageDetails);
            ToolBar.PrimaryCommands.Add(SharePackage);
            ToolBar.PrimaryCommands.Add(new AppBarSeparator());
            ToolBar.PrimaryCommands.Add(IgnoreSelected);
            ToolBar.PrimaryCommands.Add(ManageIgnored);
            // 隱藏套件組合按鈕 - 移除 ExportSelection 按鈕
            // ToolBar.PrimaryCommands.Add(new AppBarSeparator());
            // ToolBar.PrimaryCommands.Add(ExportSelection);
            ToolBar.PrimaryCommands.Add(new AppBarSeparator());
            ToolBar.PrimaryCommands.Add(HelpButton);

            Dictionary<DependencyObject, string> Labels = new()
            { // Entries with a trailing space are collapsed
              // Their texts will be used as the tooltip
                { UninstallAsAdmin,     CoreTools.Translate("Uninstall as administrator") },
                { UninstallInteractive, CoreTools.Translate("Interactive uninstall") },
                { DownloadInstallers,   CoreTools.Translate("Download selected installers") },
                { InstallationSettings, " " + CoreTools.Translate("Uninstall options") },
                { PackageDetails,       " " + CoreTools.Translate("Package details") },
                { SharePackage,         " " + CoreTools.Translate("Share") },
                { IgnoreSelected,       CoreTools.Translate("Ignore selected packages") },
                { ManageIgnored,        CoreTools.Translate("Manage ignored updates") },
                // 隱藏套件組合按鈕
                // { ExportSelection,      CoreTools.Translate("Add selection to bundle") },
                { HelpButton,           CoreTools.Translate("Help") }
            };

            Dictionary<DependencyObject, IconType> Icons = new()
            {
                { UninstallAsAdmin,       IconType.UAC },
                { UninstallInteractive,   IconType.Interactive },
                { DownloadInstallers,     IconType.Download },
                { InstallationSettings,   IconType.Options },
                { PackageDetails,         IconType.Info_Round },
                { SharePackage,           IconType.Share },
                { IgnoreSelected,         IconType.Pin },
                { ManageIgnored,          IconType.ClipboardList },
                // 隱藏套件組合按鈕
                // { ExportSelection,        IconType.AddTo },
                { HelpButton,             IconType.Help }
            };

            ApplyTextAndIconsToToolbar(Labels, Icons);

            PackageDetails.Click += (_, _) => ShowDetailsForPackage(SelectedItem, TEL_InstallReferral.ALREADY_INSTALLED);

            // 隱藏套件組合按鈕
            // ExportSelection.Click += ExportSelection_Click;
            HelpButton.Click += (_, _) => MainApp.Instance.MainWindow.NavigationPage.ShowHelp();
            InstallationSettings.Click += (_, _) => _ = ShowInstallationOptionsForPackage(SelectedItem);
            ManageIgnored.Click += async (_, _) => await DialogHelper.ManageIgnoredUpdates();
            IgnoreSelected.Click += async (_, _) =>
            {
                foreach (IPackage package in FilteredPackages.GetCheckedPackages())
                {
                    if (!package.Source.IsVirtualManager)
                    {
                        UpgradablePackagesLoader.Instance.Remove(package);
                        await package.AddToIgnoredUpdatesAsync();
                    }
                }
            };

            MainToolbarButton.Click += (_, _) => _ = MainApp.Operations.ConfirmAndUninstall(FilteredPackages.GetCheckedPackages());
            UninstallAsAdmin.Click += (_, _) => _ = MainApp.Operations.ConfirmAndUninstall(FilteredPackages.GetCheckedPackages(), elevated: true);
            UninstallInteractive.Click += (_, _) => _ = MainApp.Operations.ConfirmAndUninstall(FilteredPackages.GetCheckedPackages(), interactive: true);
            DownloadInstallers.Click += (_, _) => _ = MainApp.Operations.Download(FilteredPackages.GetCheckedPackages(), TEL_InstallReferral.ALREADY_INSTALLED);
            SharePackage.Click += (_, _) => DialogHelper.SharePackage(SelectedItem);
        }

        protected override void WhenPackageCountUpdated()
        {
            return;
        }

        protected override void WhenPackagesLoaded(ReloadReason reason)
        {
            if (!HasDoneBackup)
            {
                if (Settings.Get(Settings.K.EnablePackageBackup_LOCAL))
                {
                    _ = BackupPackages_LOCAL();
                }

                if (Settings.Get(Settings.K.EnablePackageBackup_CLOUD))
                {
                    _ = BackupPackages_CLOUD();
                }
            }

            if (WinGet.NO_PACKAGES_HAVE_BEEN_LOADED/* && !Settings.Get(Settings.K.DisableWinGetMalfunctionDetector)*/)
            {
                var infoBar = MainApp.Instance.MainWindow.WinGetWarningBanner;
                infoBar.IsOpen = true;
                infoBar.Title = CoreTools.Translate("WinGet malfunction detected");
                infoBar.Message = CoreTools.Translate("It looks like WinGet is not working properly. Do you want to attempt to repair WinGet?");
                var button = new Button { Content = CoreTools.Translate("Repair WinGet") };
                infoBar.ActionButton = button;
                button.Click += (_, _) => _ = DialogHelper.HandleBrokenWinGet();
            }

            // Rebuild the sources tree asynchronously to avoid blocking the UI
            // This ensures all configured Scoop buckets are shown
            // This is critical because we want to show aiDAPTIV-bucket even if no packages are installed from it
            _ = RebuildSourcesTreeAsync();
        }

        protected override void WhenShowingContextMenu(IPackage package)
            => _ = _whenShowingContextMenu(package);

        private async Task _whenShowingContextMenu(IPackage package)
        {
            if (MenuAsAdmin is null
                || MenuRemoveData is null
                || MenuUninstallThenReinstall is null
                || MenuReinstallPackage is null
                || MenuIgnoreUpdates is null
                || MenuPackageDetails is null
                || MenuOpenInstallLocation is null
                || MenuStartService is null
                || MenuStopService is null)
            {
                Logger.Error("Menu items are null on InstalledPackagesTab");
                return;
            }

            MenuAsAdmin.IsEnabled = package.Manager.Capabilities.CanRunAsAdmin;
            MenuRemoveData.IsEnabled = package.Manager.Capabilities.CanRemoveDataOnUninstall;

            bool IS_LOCAL = package.Source.IsVirtualManager;

            MenuReinstallPackage.IsEnabled = !IS_LOCAL;
            MenuUninstallThenReinstall.IsEnabled = !IS_LOCAL;
            MenuIgnoreUpdates.IsEnabled = false; // Will be set on the lines below;
            MenuPackageDetails.IsEnabled = !IS_LOCAL;

            MenuOpenInstallLocation.IsEnabled = package.Manager.DetailsHelper.GetInstallLocation(package) is not null;
            if (!IS_LOCAL)
            {
                if (await package.HasUpdatesIgnoredAsync())
                {
                    MenuIgnoreUpdates.Text = CoreTools.Translate("Do not ignore updates for this package anymore");
                    MenuIgnoreUpdates.Icon = new FontIcon { Glyph = "\uE77A" };
                }
                else
                {
                    MenuIgnoreUpdates.Text = CoreTools.Translate("Ignore updates for this package");
                    MenuIgnoreUpdates.Icon = new FontIcon { Glyph = "\uE718" };
                }
                MenuIgnoreUpdates.IsEnabled = true;
            }

            // Control visibility of service management menu items
            // Only show for packages from valid bucket source
            bool isFromValidBucket = ServiceManager.IsFromValidBucket(package);
            if (isFromValidBucket)
            {
                // 直接從 PackageWrapper 取得目前的燈號狀態，而不是重新執行 status.ps1
                var wrapper = GetPackageWrapper(package);
                bool isAppRunning = wrapper?.StartupStatusLight == StartupStatusLight.Green;

                MenuStartService.Visibility = !isAppRunning ? Visibility.Visible : Visibility.Collapsed;
                MenuStopService.Visibility = isAppRunning ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                MenuStartService.Visibility = Visibility.Collapsed;
                MenuStopService.Visibility = Visibility.Collapsed;
            }
        }

        // 隱藏套件組合按鈕 - 停用匯出功能
        // private void ExportSelection_Click(object sender, RoutedEventArgs e) => _ = _exportSelection_Click();
        // private async Task _exportSelection_Click()
        // {
        //     MainApp.Instance.MainWindow.NavigationPage.NavigateTo(PageType.Bundles);
        //     int loadingId = DialogHelper.ShowLoadingDialog(CoreTools.Translate("Please wait..."));
        //     await PackageBundlesLoader.Instance.AddPackagesAsync(FilteredPackages.GetCheckedPackages());
        //     DialogHelper.HideLoadingDialog(loadingId);
        // }

        public static Task<string> GenerateBackupContents()
        {
            Logger.Debug("Starting package backup");
            List<IPackage> packagesToExport = [];
            foreach (IPackage package in InstalledPackagesLoader.Instance.Packages)
            {
                packagesToExport.Add(package);
            }

            return PackageBundlesPage.CreateBundle(packagesToExport.ToArray());
        }

        // === GitHub 登入功能已停用 - Cloud Backup 已停用 ===
        public static Task BackupPackages_CLOUD()
        {
            // GitHub 備份功能已停用
            return Task.CompletedTask;
        }
        /* === 原始 BackupPackages_CLOUD 程式碼 (已註解) ===
        public static async Task BackupPackages_CLOUD()
        {
            try
            {
                await CoreTools.WaitForInternetConnection();
                string backupContents = await GenerateBackupContents();
                var authService = new GitHubAuthService();
                var backupService = new GitHubBackupService(authService);
                await backupService.UploadPackageBundle(backupContents);
                Logger.ImportantInfo("Cloud backup succeeded");
            }
            catch (Exception ex)
            {
                Logger.Error("An error occurred while performing a CLOUD backup");
                Logger.Error(ex);
            }
        }
        === 原始 BackupPackages_CLOUD 程式碼結束 ===
        */

        public static async Task BackupPackages_LOCAL()
        {
            try
            {
                string backupContents = await GenerateBackupContents();
                string dirName = Settings.GetValue(Settings.K.ChangeBackupOutputDirectory);
                if (dirName == "")
                {
                    dirName = CoreData.UniGetUI_DefaultBackupDirectory;
                }

                if (!Directory.Exists(dirName))
                {
                    Directory.CreateDirectory(dirName);
                }

                string fileName = Settings.GetValue(Settings.K.ChangeBackupFileName);
                if (fileName == "")
                {
                    fileName = CoreTools.Translate("{pcName} installed packages", new Dictionary<string, object?> { { "pcName", Environment.MachineName } });
                }

                if (Settings.Get(Settings.K.EnableBackupTimestamping))
                {
                    fileName += " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
                }

                fileName += ".ubundle";

                string filePath = Path.Combine(dirName, fileName);
                await File.WriteAllTextAsync(filePath, backupContents);
                HasDoneBackup = true;
                Logger.ImportantInfo("Backup saved to " + filePath);
            }
            catch (Exception ex)
            {
                Logger.Error("An error occurred while performing a LOCAL backup");
                Logger.Error(ex);
            }
        }

        private void MenuUninstall_Invoked(object sender, RoutedEventArgs args)
            => _ = MainApp.Operations.ConfirmAndUninstall(SelectedItem);

        private void MenuAsAdmin_Invoked(object sender, RoutedEventArgs args)
            => _ = MainApp.Operations.ConfirmAndUninstall(SelectedItem, elevated: true);

        private void MenuRemoveData_Invoked(object sender, RoutedEventArgs args)
            => _ = MainApp.Operations.ConfirmAndUninstall(SelectedItem, remove_data: true);

        private void MenuReinstall_Invoked(object sender, RoutedEventArgs args)
            => _ = MainApp.Operations.Install(SelectedItem, TEL_InstallReferral.ALREADY_INSTALLED);

        private void MenuUninstallThenReinstall_Invoked(object sender, RoutedEventArgs args)
            => _ = MainApp.Operations.UninstallThenReinstall(SelectedItem, TEL_InstallReferral.ALREADY_INSTALLED);

        private void MenuIgnorePackage_Invoked(object sender, RoutedEventArgs args) => _ = _menuIgnorePackage_Invoked();
        private async Task _menuIgnorePackage_Invoked()
        {
            IPackage? package = SelectedItem;
            if (package is null) return;

            if (await package.HasUpdatesIgnoredAsync())
            {
                await package.RemoveFromIgnoredUpdatesAsync();
            }
            else
            {
                await package.AddToIgnoredUpdatesAsync();
                UpgradablePackagesLoader.Instance.Remove(package);
            }
        }

        private void MenuDetails_Invoked(object sender, RoutedEventArgs args)
        {
            ShowDetailsForPackage(SelectedItem, TEL_InstallReferral.ALREADY_INSTALLED);
        }

        private async void MenuStartService_Invoked(object sender, RoutedEventArgs args)
        {
            await StartServiceAsync();
        }

        /// <summary>
        /// Starts the aiDAPTIV service
        /// </summary>
        private async Task StartServiceAsync()
        {
            if (SelectedItem != null)
            {
                await ShowServiceOperationProgressAsync(async () =>
                {
                    var (errorCode, errorMessage) = await ServiceManager.StartServiceAsync(SelectedItem);
                    return (errorCode == UniGetUI.PhisonService.ServiceErrorCode.Success, errorMessage);
                });
            }
        }

        private async void MenuStopService_Invoked(object sender, RoutedEventArgs args)
        {
            if (SelectedItem != null)
            {
                await ShowServiceOperationProgressAsync(async () =>
                {
                    var (errorCode, errorMessage) = await ServiceManager.StopServiceAsync(SelectedItem);
                    return (errorCode == UniGetUI.PhisonService.ServiceErrorCode.Success, errorMessage);
                });
            }
        }

        /// <summary>
        /// Shows a progress dialog with status updates while performing a service operation
        /// </summary>
        private async Task ShowServiceOperationProgressAsync(Func<Task<(bool success, string? errorMessage)>> serviceOperation)
        {
            ContentDialog progressDialog = null!;
            TextBlock statusTextBlock = null!;
            Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue = null!;
            Microsoft.UI.Dispatching.DispatcherQueueTimer? statusTimer = null;
            bool operationCompleted = false;
            string finalStatus = string.Empty;

            try
            {
                // Get the dispatcher queue from the main window
                dispatcherQueue = MainApp.Instance.MainWindow.DispatcherQueue;

                // Create the progress dialog on the UI thread
                dispatcherQueue.TryEnqueue(async () =>
                {
                    statusTextBlock = new TextBlock
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        Text = CoreTools.Translate("Initializing...")
                    };

                    progressDialog = new ContentDialog
                    {
                        Title = CoreTools.Translate("Service Operation"),
                        Content = new StackPanel
                        {
                            Orientation = Orientation.Vertical,
                            VerticalAlignment = VerticalAlignment.Stretch,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            Spacing = 20,
                            Children =
                            {
                                new ProgressRing
                                {
                                    IsIndeterminate = true,
                                    HorizontalAlignment = HorizontalAlignment.Center,
                                    VerticalAlignment = VerticalAlignment.Center,
                                    Width = 60,
                                    Height = 60
                                },
                                statusTextBlock
                            }
                        },
                        XamlRoot = MainApp.Instance.MainWindow.Content.XamlRoot,
                        RequestedTheme = MainApp.Instance.MainWindow.MainContentGrid.RequestedTheme
                    };

                    // Create and start the status update timer
                    statusTimer = dispatcherQueue.CreateTimer();
                    statusTimer.Interval = TimeSpan.FromMilliseconds(100);
                    statusTimer.Tick += async (_, _) =>
                    {
                        try
                        {
                            var (status, errorCode) = await ServiceManager.GetServiceStatusAsync();

                            // Update the status text
                            statusTextBlock.Text = status;

                            // Check if operation is completed based on errorCode
                            // Stop when errorCode is Success (0) or Fail (-1)
                            if (errorCode == ServiceErrorCode.Success || errorCode == ServiceErrorCode.Fail)
                            {
                                operationCompleted = true;
                                finalStatus = status;
                                statusTimer?.Stop();

                                // Close the progress dialog
                                progressDialog?.Hide();

                                if (errorCode == ServiceErrorCode.Fail)
                                {
                                    // Show the final status dialog with OK button
                                    await ShowFinalStatusDialogAsync(finalStatus);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error updating service status: {ex.Message}");
                        }
                    };
                    statusTimer.Start();

                    // Show the dialog (non-blocking, but we'll manage its lifecycle)
                    _ = progressDialog.ShowAsync();
                });

                // Start the service operation in the background
                var (success, errorMessage) = await serviceOperation();

                // Wait for the status to reach a terminal state (max 10 seconds additional wait)
                int waitCount = 0;
                while (!operationCompleted && waitCount < 100) // 100 * 100ms = 10 seconds
                {
                    await Task.Delay(100);
                    waitCount++;
                }

                // Ensure the timer is stopped and dialog is closed
                dispatcherQueue.TryEnqueue(() =>
                {
                    statusTimer?.Stop();
                    progressDialog?.Hide();
                });

                if (!success && !string.IsNullOrEmpty(errorMessage))
                {
                    Logger.Error($"Service operation failed: {errorMessage}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error showing service operation progress: {ex.Message}");

                // Ensure cleanup
                dispatcherQueue.TryEnqueue(() =>
                {
                    statusTimer?.Stop();
                    progressDialog?.Hide();
                });
            }
        }

        /// <summary>
        /// Shows a dialog displaying the final status of the service operation
        /// </summary>
        private async Task ShowFinalStatusDialogAsync(string status)
        {
            try
            {
                var statusDialog = new ContentDialog
                {
                    Title = CoreTools.Translate("Service Operation Result"),
                    Content = new TextBlock
                    {
                        Text = status,
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    PrimaryButtonText = "OK",
                    XamlRoot = MainApp.Instance.MainWindow.Content.XamlRoot,
                    RequestedTheme = MainApp.Instance.MainWindow.MainContentGrid.RequestedTheme
                };

                await statusDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error showing final status dialog: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when entering the Installed Packages page.
        /// Automatically selects aiDAPTIV-bucket source if it exists.
        /// If aiDAPTIV-bucket is not found or has no installed packages, shows blank page.
        /// </summary>
        public override void OnEnter()
        {
            Visibility = Visibility.Visible;
            IsEnabled = true;

            // Check if packages are still loading
            if (Loader.IsLoading)
            {
                // If still loading, just show the loading indicator
                // The rebuild and selection will happen in Loader_FinishedLoading
                LoadingProgressBar.Visibility = Visibility.Visible;
                Logger.Debug("[InstalledPackages] OnEnter: Packages are loading, showing progress bar");
                return;
            }

            // Packages are already loaded, rebuild the sources tree asynchronously
            _ = RebuildSourcesTreeAsync();
        }

        /// <summary>
        /// Rebuilds the sources tree view to reflect the current manager configuration asynchronously.
        /// This ensures that sources removed from manager configuration (e.g., Scoop buckets)
        /// are also removed from the tree view, even if packages from those sources are still installed.
        /// Additionally, this method adds all configured sources even if no packages are installed from them.
        /// </summary>
        private async Task RebuildSourcesTreeAsync()
        {
            try
            {
                Logger.Debug("[InstalledPackages] Starting async rebuild of sources tree");

                // Show loading indicator immediately
                DispatcherQueue.TryEnqueue(() =>
                {
                    LoadingProgressBar.Visibility = Visibility.Visible;
                });

                // Prepare data structures for UI update (collect all sources on background thread)
                var sourcesData = await Task.Run(() =>
                {
                    Logger.Debug("[InstalledPackages] Collecting sources data on background thread");

                    var data = new Dictionary<IPackageManager, List<IManagerSource>>();

                    // Collect all sources from managers (this is the heavy I/O operation)
                    foreach (var manager in PEInterface.Managers)
                    {
                        if (manager.Capabilities.SupportsCustomSources &&
                            manager.Properties.Name.Equals("Scoop", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                // This I/O operation now runs on background thread
                                var availableSources = manager.SourcesHelper.GetSources();
                                data[manager] = new List<IManagerSource>(availableSources);
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"[InstalledPackages] Failed to get sources from {manager.Properties.Name}: {ex.Message}");
                            }
                        }
                    }

                    return data;
                });

                // Update UI quickly on UI thread with pre-collected data
                DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        // Clear the sources tree view
                        SourcesTreeView.SelectedNodes.Clear();
                        SourcesTreeView.RootNodes.Clear();

                        // Clear the tracking dictionaries
                        UsedManagers.Clear();
                        UsedSourcesForManager.Clear();
                        RootNodeForManager.Clear();
                        NodesForSources.Clear();

                        // Rebuild the tree by re-adding all packages
                        // The AddPackageToSourcesList method already contains logic to skip sources
                        // that don't exist in the manager configuration
                        foreach (var package in Loader.Packages)
                        {
                            AddPackageToSourcesList(package);
                        }

                        // Add all configured sources from collected data
                        foreach (var kvp in sourcesData)
                        {
                            var manager = kvp.Key;
                            var availableSources = kvp.Value;

                            // Add manager root node if not already present
                            if (!UsedManagers.Contains(manager))
                            {
                                UsedManagers.Add(manager);
                                TreeViewNode ManagerNode = new() { Content = manager.Properties.Name, IsExpanded = true };
                                UsedSourcesForManager[manager] = [];
                                RootNodeForManager[manager] = ManagerNode;
                                SourcesTreeView.RootNodes.Add(ManagerNode);
                            }

                            // Add each source if not already present
                            foreach (var source in availableSources)
                            {
                                // Check if source already exists by comparing Name and URL (not object reference)
                                bool sourceAlreadyExists = UsedSourcesForManager.ContainsKey(manager) &&
                                    UsedSourcesForManager[manager].Any(s =>
                                        s.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase) &&
                                        s.Url?.ToString().TrimEnd('/') == source.Url?.ToString().TrimEnd('/')
                                    );

                                if (!sourceAlreadyExists)
                                {
                                    if (!UsedSourcesForManager.ContainsKey(manager))
                                    {
                                        UsedSourcesForManager[manager] = [];
                                    }
                                    UsedSourcesForManager[manager].Add(source);
                                    TreeViewNode item = new() { Content = source.Name + "                                                                                    ." };
                                    NodesForSources.TryAdd(source, item);
                                    RootNodeForManager[manager].Children.Add(item);

                                    Logger.Debug($"[InstalledPackages] Added source '{source.Name}' (URL: {source.Url}) to tree view");
                                }
                            }
                        }

                        Logger.Debug($"[InstalledPackages] Sources tree rebuilt. Total sources: {NodesForSources.Count}");

                        // Auto-select aiDAPTIV-bucket source after rebuilding
                        SelectAiDAPTIVBucketSource();

                        // Re-apply filters to update the displayed packages
                        FilterPackages();

                        // Hide loading indicator
                        LoadingProgressBar.Visibility = Visibility.Collapsed;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"[InstalledPackages] Failed to rebuild sources tree: {ex.Message}");
                        Logger.Error(ex);
                        LoadingProgressBar.Visibility = Visibility.Collapsed;
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"[InstalledPackages] Failed to rebuild sources tree asynchronously: {ex.Message}");
                Logger.Error(ex);
                DispatcherQueue.TryEnqueue(() =>
                {
                    LoadingProgressBar.Visibility = Visibility.Collapsed;
                });
            }
        }

        /// <summary>
        /// Rebuilds the sources tree view to reflect the current manager configuration.
        /// This ensures that sources removed from manager configuration (e.g., Scoop buckets)
        /// are also removed from the tree view, even if packages from those sources are still installed.
        /// Additionally, this method adds all configured sources even if no packages are installed from them.
        /// </summary>
        [Obsolete("Use RebuildSourcesTreeAsync() instead")]
        private void RebuildSourcesTree()
        {
            // This method is kept for backward compatibility but should not be used
            // Use RebuildSourcesTreeAsync() instead
            _ = RebuildSourcesTreeAsync();
        }

        /// <summary>
        /// Selects only the valid bucket source (from ValidBucketList.json) if it exists in the tree view.
        /// The source is identified by URL from the ValidBucketList configuration.
        /// If not found, clears all selections to show blank page.
        /// Note: RebuildSourcesTree() must be called first to ensure the tree reflects current configuration.
        /// </summary>
        private void SelectAiDAPTIVBucketSource()
        {
            try
            {
                Logger.Debug($"[InstalledPackages] Attempting to select valid bucket. Total sources in NodesForSources: {NodesForSources.Count}");

                // Clear all current selections first
                SourcesTreeView.SelectedNodes.Clear();

                // Try to find valid bucket source in the tree view (using ValidBucketList.json)
                var validBucketSource = NodesForSources.FirstOrDefault(kvp =>
                    kvp.Key.Manager.Properties.Name.Equals("Scoop", StringComparison.OrdinalIgnoreCase) &&
                    kvp.Key.Url != null &&
                    CoreData.IsValidBucket(kvp.Key.Url)
                );

                if (validBucketSource.Value != null)
                {
                    // Found - select it
                    SourcesTreeView.SelectedNodes.Add(validBucketSource.Value);

                    // Ensure the Scoop parent node is expanded
                    var scoopManager = validBucketSource.Key.Manager;
                    if (RootNodeForManager.TryGetValue(scoopManager, out var scoopNode))
                    {
                        scoopNode.IsExpanded = true;
                    }

                    Logger.ImportantInfo($"[InstalledPackages] Successfully auto-selected valid bucket source: {validBucketSource.Key.Name}");
                }
                else
                {
                    // Not found - show blank page
                    Logger.Warn("[InstalledPackages] Valid bucket not found in tree view, showing blank page");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[InstalledPackages] Failed to auto-select valid bucket: {ex.Message}");
                Logger.Error(ex);
                // On error, clear selections to show blank page
                SourcesTreeView.SelectedNodes.Clear();
            }
        }

    }
}
