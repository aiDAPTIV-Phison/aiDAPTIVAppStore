using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UniGetUI.Core.Logging;
using UniGetUI.Core.Tools;
using UniGetUI.Interface.Enums;
using UniGetUI.Interface.Widgets;
using UniGetUI.PackageEngine;
using UniGetUI.PackageEngine.Enums;
using UniGetUI.PackageEngine.Interfaces;
using UniGetUI.PackageEngine.PackageLoader;
using Windows.System;
using Windows.UI.Text;
using Microsoft.UI.Xaml.Controls.Primitives;
using UniGetUI.Interface.Telemetry;
using UniGetUI.Pages.DialogPages;
using Microsoft.UI.Xaml.Input;
using System.Linq;

namespace UniGetUI.Interface.SoftwarePages
{
    public partial class DiscoverSoftwarePage : AbstractPackagesPage
    {
        private BetterMenuItem? MenuInstall;
        private BetterMenuItem? MenuAsAdmin;
        private BetterMenuItem? MenuSkipHash;

        // Custom loader that only includes Scoop manager to avoid searching other sources (winget, npm, etc.)
        private static readonly DiscoverablePackagesLoader ScoopOnlyLoader = new DiscoverablePackagesLoader(new[] { PEInterface.Scoop });

        public DiscoverSoftwarePage()
        : base(new PackagesPageData
        {
            DisableAutomaticPackageLoadOnStart = true,
            DisableFilterOnQueryChange = true,
            MegaQueryBlockEnabled = true,
            PackagesAreCheckedByDefault = false,
            ShowLastLoadTime = false,
            DisableReload = false,
            DisableSuggestedResultsRadio = false,
            PageName = "Discover",

            Loader = ScoopOnlyLoader,  // Use Scoop-only loader
            PageRole = OperationType.Install,

            NoPackages_BackgroundText = CoreTools.Translate("No results were found matching the input criteria"),
            NoPackages_SourcesText = CoreTools.Translate("No packages were found"),
            NoPackages_SubtitleText_Base = CoreTools.Translate("No packages were found"),
            MainSubtitle_StillLoading = CoreTools.Translate("Loading packages"),
            NoMatches_BackgroundText = CoreTools.Translate("No results were found matching the input criteria"),

            PageTitle = CoreTools.Translate("Discover Packages"),
            Glyph = "\uF6FA"
        })
        {
            InstantSearchCheckbox.IsEnabled = false;
            InstantSearchCheckbox.Visibility = Visibility.Collapsed;

            // 移除置中搜尋套件元件 - 開始
            // MegaFindButton.Click += Event_SearchPackages;
            // MegaQueryBlock.KeyUp += (s, e) => { if (e.Key == VirtualKey.Enter) { Event_SearchPackages(s, e); } };
            // 移除置中搜尋套件元件 - 結束

            // Automatically load apps from aiDAPTIV-bucket when page is loaded
            Loaded += (s, e) => AutoLoadDefaultBucket();

            // Keep discover cards in sync when installed packages finish loading asynchronously.
            InstalledPackagesLoader.Instance.PackagesChanged += InstalledPackagesLoader_PackagesChanged;
            Unloaded += DiscoverSoftwarePage_Unloaded;
        }

        private void DiscoverSoftwarePage_Unloaded(object sender, RoutedEventArgs e)
        {
            InstalledPackagesLoader.Instance.PackagesChanged -= InstalledPackagesLoader_PackagesChanged;
            Unloaded -= DiscoverSoftwarePage_Unloaded;
        }

        private void InstalledPackagesLoader_PackagesChanged(object? sender, PackagesChangedEvent e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                foreach (IPackage package in Loader.Packages)
                {
                    if (package.Tag is PackageTag.OnQueue or PackageTag.BeingProcessed or PackageTag.Failed or PackageTag.Unavailable or PackageTag.Pinned)
                    {
                        continue;
                    }

                    PackageTag targetTag = package.GetUpgradablePackage() is not null
                        ? PackageTag.IsUpgradable
                        : (package.GetInstalledPackages().Any() ? PackageTag.AlreadyInstalled : PackageTag.Default);

                    if (package.Tag != targetTag)
                    {
                        package.SetTag(targetTag);
                    }
                }

                // Rebind list items so first-load installed state/highlight is reflected immediately.
                FilterPackages();
            });
        }

        private async void AutoLoadDefaultBucket()
        {
            // Wait for page to fully initialize
            await Task.Delay(500);

            // Ensure only aiDAPTIV-bucket is selected on first load
            ForceSelectOnlyAiDAPTIVBucket();

            // Search with wildcard, but only searches Scoop (due to ScoopOnlyLoader)
            // The source filter on the left will automatically show only aiDAPTIV-bucket packages
            _ = (Loader as DiscoverablePackagesLoader)?.ReloadPackages(".");
        }


        public override void SearchBox_QuerySubmitted(object? sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            base.SearchBox_QuerySubmitted(sender, args);
            Event_SearchPackages(sender, new());
        }

        public override BetterMenu GenerateContextMenu()
        {
            BetterMenu menu = new();

            MenuInstall = new BetterMenuItem
            {
                Text = CoreTools.AutoTranslated("Install"),
                IconName = IconType.Download,
                KeyboardAcceleratorTextOverride = "Ctrl+Enter"
            };
            MenuInstall.Click += MenuInstall_Invoked;
            menu.Items.Add(MenuInstall);

            menu.Items.Add(new MenuFlyoutSeparator { Height = 5 });

            MenuAsAdmin = new BetterMenuItem
            {
                Text = CoreTools.AutoTranslated("Install as administrator"),
                IconName = IconType.UAC
            };
            MenuAsAdmin.Click += MenuAsAdmin_Invoked;
            menu.Items.Add(MenuAsAdmin);

            MenuSkipHash = new BetterMenuItem
            {
                Text = CoreTools.AutoTranslated("Skip hash check"),
                IconName = IconType.Checksum
            };
            MenuSkipHash.Click += MenuSkipHash_Invoked;
            menu.Items.Add(MenuSkipHash);

            menu.Items.Add(new MenuFlyoutSeparator { Height = 5 });

            BetterMenuItem menuDetails = new()
            {
                Text = CoreTools.AutoTranslated("Package details"),
                IconName = IconType.Info_Round,
                KeyboardAcceleratorTextOverride = "Enter"
            };
            menuDetails.Click += MenuDetails_Invoked;
            menu.Items.Add(menuDetails);

            return menu;
        }

        public override void GenerateToolBar()
        {
            BetterMenuItem InstallAsAdmin = new();
            BetterMenuItem InstallSkipHash = new();
            BetterMenuItem InstallInteractive = new();
            BetterMenuItem DownloadInstallers = new();

            MainToolbarButtonDropdown.Flyout = new BetterMenu()
            {
                Items = {
                    InstallAsAdmin,
                    InstallSkipHash,
                    InstallInteractive,
                    new MenuFlyoutSeparator(),
                    DownloadInstallers,
                    },
                Placement = FlyoutPlacementMode.Bottom
            };
            MainToolbarButtonIcon.Icon = IconType.Download;
            MainToolbarButtonText.Text = CoreTools.Translate("Install selection");

            AppBarButton InstallationSettings = new();

            AppBarButton PackageDetails = new();
            AppBarButton SharePackage = new();

            // 隱藏套件組合按鈕 - 移除 ExportSelection 按鈕
            // AppBarButton ExportSelection = new();

            AppBarButton HelpButton = new();

            ToolBar.PrimaryCommands.Add(new AppBarSeparator());
            ToolBar.PrimaryCommands.Add(InstallationSettings);
            ToolBar.PrimaryCommands.Add(new AppBarSeparator());
            ToolBar.PrimaryCommands.Add(PackageDetails);
            ToolBar.PrimaryCommands.Add(SharePackage);
            // 隱藏套件組合按鈕 - 移除 ExportSelection 按鈕
            // ToolBar.PrimaryCommands.Add(new AppBarSeparator());
            // ToolBar.PrimaryCommands.Add(ExportSelection);
            ToolBar.PrimaryCommands.Add(new AppBarSeparator());
            ToolBar.PrimaryCommands.Add(HelpButton);

            Dictionary<DependencyObject, string> Labels = new()
            {   // Entries with a trailing space are collapsed
                // Their texts will be used as the tooltip
                { InstallAsAdmin,         CoreTools.Translate("Install as administrator") },
                { InstallSkipHash,        CoreTools.Translate("Skip integrity checks") },
                { InstallInteractive,     CoreTools.Translate("Interactive installation") },
                { DownloadInstallers,     CoreTools.Translate("Download selected installers") },
                { InstallationSettings,   CoreTools.Translate("Install options") },
                { PackageDetails,         " " + CoreTools.Translate("Package details") },
                { SharePackage,           " " + CoreTools.Translate("Share") },
                // 隱藏套件組合按鈕
                // { ExportSelection,        CoreTools.Translate("Add selection to bundle") },
                { HelpButton,             CoreTools.Translate("Help") }
            };

            Dictionary<DependencyObject, IconType> Icons = new()
            {
                { InstallAsAdmin,       IconType.UAC },
                { InstallSkipHash,      IconType.Checksum },
                { InstallationSettings, IconType.Options },
                { DownloadInstallers,   IconType.Download },
                { InstallInteractive,   IconType.Interactive },
                { PackageDetails,       IconType.Info_Round },
                { SharePackage,         IconType.Share },
                // 隱藏套件組合按鈕
                // { ExportSelection,      IconType.AddTo },
                { HelpButton,           IconType.Help }
            };

            ApplyTextAndIconsToToolbar(Labels, Icons);

            PackageDetails.Click += (_, _) => ShowDetailsForPackage(SelectedItem, TEL_InstallReferral.DIRECT_SEARCH);
            // 隱藏套件組合按鈕
            // ExportSelection.Click += ExportSelection_Click;
            HelpButton.Click += (_, _) => MainApp.Instance.MainWindow.NavigationPage.ShowHelp();
            InstallationSettings.Click += (_, _) => _ = ShowInstallationOptionsForPackage(SelectedItem);

            MainToolbarButton.Click += (_, _) => MainApp.Operations.Install(GetInstallableCheckedPackages(), TEL_InstallReferral.DIRECT_SEARCH);
            InstallAsAdmin.Click += (_, _) => MainApp.Operations.Install(GetInstallableCheckedPackages(), TEL_InstallReferral.DIRECT_SEARCH, elevated: true);
            InstallSkipHash.Click += (_, _) => MainApp.Operations.Install(GetInstallableCheckedPackages(), TEL_InstallReferral.DIRECT_SEARCH, no_integrity: true);
            InstallInteractive.Click += (_, _) => MainApp.Operations.Install(GetInstallableCheckedPackages(), TEL_InstallReferral.DIRECT_SEARCH, interactive: true);
            DownloadInstallers.Click += (_, _) => _ = MainApp.Operations.Download(FilteredPackages.GetCheckedPackages(), TEL_InstallReferral.DIRECT_SEARCH);

            SharePackage.Click += (_, _) => DialogHelper.SharePackage(SelectedItem);
        }

        public override async Task LoadPackages()
        {
            if (QueryBlock.Text.Trim() != "")
            {
                await LoadPackages(ReloadReason.External);
            }
        }

        private void Event_SearchPackages(object sender, RoutedEventArgs e)
        {
            if (QueryBlock.Text.Trim() != "")
            {
                _ = (Loader as DiscoverablePackagesLoader)?.ReloadPackages(QueryBlock.Text.Trim());
            }
            else
            {
                Loader.StopLoading();
            }
        }

        protected override void WhenPackageCountUpdated()
        { }

        protected override void WhenPackagesLoaded(ReloadReason reason)
        { }

        protected override void WhenShowingContextMenu(IPackage package)
        {
            if (MenuInstall is null || MenuAsAdmin is null || MenuSkipHash is null)
            {
                Logger.Warn("MenuItems are null on DiscoverPackagesPage");
                return;
            }

            bool isBlocked = IsPackageActionBlocked(package);
            MenuInstall.IsEnabled = !isBlocked;
            MenuAsAdmin.IsEnabled = package.Manager.Capabilities.CanRunAsAdmin && !isBlocked;
            MenuSkipHash.IsEnabled = package.Manager.Capabilities.CanSkipIntegrityChecks && !isBlocked;
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

        private void MenuDetails_Invoked(object sender, RoutedEventArgs e)
        {
            ShowDetailsForPackage(SelectedItem, TEL_InstallReferral.DIRECT_SEARCH);
        }

        private void MenuInstall_Invoked(object sender, RoutedEventArgs e)
            => PerformMainPackageAction(SelectedItem);

        private void MenuSkipHash_Invoked(object sender, RoutedEventArgs e)
        {
            if (SelectedItem is null || IsPackageActionBlocked(SelectedItem))
            {
                return;
            }
            _ = MainApp.Operations.Install(SelectedItem, TEL_InstallReferral.DIRECT_SEARCH, no_integrity: true);
        }

        private void MenuAsAdmin_Invoked(object sender, RoutedEventArgs e)
        {
            if (SelectedItem is null || IsPackageActionBlocked(SelectedItem))
            {
                return;
            }
            _ = MainApp.Operations.Install(SelectedItem, TEL_InstallReferral.DIRECT_SEARCH, elevated: true);
        }

        public override bool IsPackageActionBlocked(IPackage package)
            => package.Tag == PackageTag.AlreadyInstalled || package.GetInstalledPackages().Count > 0;

        private List<IPackage> GetInstallableCheckedPackages()
            => FilteredPackages.GetCheckedPackages().Where(package => !IsPackageActionBlocked(package)).ToList();

    }
}
