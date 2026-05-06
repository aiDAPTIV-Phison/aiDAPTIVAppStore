using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using Windows.UI;
using UniGetUI.Core.Classes;
using UniGetUI.Core.Tools;
using UniGetUI.Interface;
using UniGetUI.Interface.Enums;
using UniGetUI.PackageEngine.Interfaces;
using UniGetUI.PhisonService;

namespace UniGetUI.PackageEngine.PackageClasses
{
    /// <summary>
    /// A wrapper for packages to be able to show in ItemCollections
    /// </summary>
    public partial class PackageWrapper : IIndexableListItem, INotifyPropertyChanged, IDisposable
    {
        private static readonly ConcurrentDictionary<long, Uri?> CachedPackageIcons = new();
        private static readonly Brush ReinstallBlockedBrush = new SolidColorBrush(Color.FromArgb(40, 255, 193, 7));
        private static readonly Brush TransparentBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

        public static void ResetIconCache()
        {
            CachedPackageIcons.Clear();
        }

        public bool IsChecked
        {
            get => Package.IsChecked;
            set
            {
                if (!CanBeSelected && value)
                {
                    return;
                }
                Package.IsChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                _page.UpdatePackageCount();
            }
        }

        public bool IconWasLoaded;
        public bool AlternateIdIconVisible;
        public bool ShowCustomPackageIcon;
        public bool ShowDefaultPackageIcon = true;
        public string VersionComboString;
        public IconType MainIconId = IconType.Id;
        public IconType AlternateIconId = IconType.Id;
        public ImageSource? MainIconSource;

        public Uri? PackageIcon
        {
            set
            {
                CachedPackageIcons[Package.GetHash()] = value;
                UpdatePackageIcon();
            }
        }

        public string ListedNameTooltip = "";
        public readonly string ExtendedTooltip = "";
        public float ListedOpacity = 1.0f;

        // 啟動狀態燈號（預設為灰燈，詳細判斷邏輯由外部處理）
        private StartupStatusLight _startupStatusLight = StartupStatusLight.Gray;
        public StartupStatusLight StartupStatusLight
        {
            get => _startupStatusLight;
            set
            {
                if (_startupStatusLight != value)
                {
                    _startupStatusLight = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StartupStatusLight)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StartupStatusLightColor)));
                }
            }
        }

        // 用於 XAML 綁定的顏色屬性
        public Brush StartupStatusLightColor
        {
            get
            {
                return StartupStatusLight switch
                {
                    StartupStatusLight.Gray => new SolidColorBrush(Color.FromArgb(255, 128, 128, 128)), // 灰色
                    StartupStatusLight.Green => new SolidColorBrush(Color.FromArgb(255, 0, 200, 83)),   // 綠色
                    StartupStatusLight.Red => new SolidColorBrush(Color.FromArgb(255, 232, 17, 35)),    // 紅色
                    _ => new SolidColorBrush(Color.FromArgb(255, 128, 128, 128))
                };
            }
        }

        // 判斷套件是否來自有效的 bucket（用於控制燈號顯示）
        public bool IsFromValidBucket
        {
            get
            {
                return ServiceManager.IsFromValidBucket(Package);
            }
        }

        // 判斷套件是否已安裝（用於控制燈號顯示）
        public bool IsInstalled
        {
            get
            {
                // 檢查 Tag 是否為 AlreadyInstalled，或檢查是否有已安裝的套件
                return Package.Tag == PackageTag.AlreadyInstalled || 
                       Package.GetInstalledPackages().Count > 0;
            }
        }

        // 判斷是否應該顯示燈號（來自有效 bucket 且已安裝）
        public bool ShouldShowStatusLight
        {
            get
            {
                return IsFromValidBucket && IsInstalled;
            }
        }

        // 根據是否應該顯示燈號返回套件名稱的 Margin（用於調整燈號位置 - List 模式）
        public Thickness PackageNameMargin
        {
            get
            {
                // 如果應該顯示燈號，套件名稱需要為燈號留出空間（8px 燈號 + 4px 間距 = 12px）
                return ShouldShowStatusLight ? new Thickness(52, -2, 0, 0) : new Thickness(40, -2, 0, 0);
            }
        }

        // 根據是否應該顯示燈號返回套件名稱的 Margin（用於調整燈號位置 - Grid 模式）
        public Thickness PackageNameMarginGrid
        {
            get
            {
                // 如果應該顯示燈號，套件名稱需要為燈號留出空間（8px 燈號 + 4px 間距 = 12px）
                return ShouldShowStatusLight ? new Thickness(12, 0, 0, 0) : new Thickness(0, 0, 0, 0);
            }
        }

        // 根據是否應該顯示燈號返回套件名稱的 Margin（用於調整燈號位置 - Icons 模式）
        public Thickness PackageNameMarginIcons
        {
            get
            {
                // 如果應該顯示燈號，套件名稱需要為燈號留出空間（8px 燈號 + 4px 間距 = 12px）
                return ShouldShowStatusLight ? new Thickness(12, 0, 0, 0) : new Thickness(0, 0, 0, 0);
            }
        }

        public int NewVersionLabelWidth { get => Package.IsUpgradable ? 125 : 0; }
        public int NewVersionIconWidth { get => Package.IsUpgradable ? 24 : 0; }
        public bool IsActionBlocked => _page.IsPackageActionBlocked(Package);
        public bool CanBeSelected => !IsActionBlocked;
        public Brush ItemHighlightBrush => IsActionBlocked ? ReinstallBlockedBrush : TransparentBrush;

        public int Index { get; set; }
        public event PropertyChangedEventHandler? PropertyChanged;

        public IPackage Package { get; private set; }
        public PackageWrapper Self { get; private set; }

        private readonly AbstractPackagesPage _page;
        private PackageStatusMonitor? _statusMonitor;

        public PackageWrapper(IPackage package, AbstractPackagesPage page)
        {
            Package = package;
            Self = this;
            _page = page;
            WhenTagHasChanged();
            Package.PropertyChanged += Package_PropertyChanged;
            UpdatePackageIcon();
            VersionComboString = package.IsUpgradable ? $"{package.VersionString} -> {package.NewVersionString}" : package.VersionString;

            if(package.Name.ToLower() != package.Id.ToLower())
                ExtendedTooltip = $"{package.Name} ({package.Id} from {package.Source.AsString_DisplayName})";
            else
                ExtendedTooltip = $"{package.Name} (from {package.Source.AsString_DisplayName})";

            // 如果是來自 aiDAPTIV-bucket 且已安裝的套件，啟動狀態監控
            if (ShouldShowStatusLight)
            {
                _statusMonitor = new PackageStatusMonitor(Package);
                _statusMonitor.StatusChanged += (status) =>
                {
                    StartupStatusLight = status;
                };
                _statusMonitor.StartMonitoring();
            }
        }

        public void PackageItemContainer_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
            => _page.PackageItemContainer_DoubleTapped(sender, e);

        public void PackageItemContainer_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
            => _page.PackageItemContainer_PreviewKeyDown(sender, e);

        public void PackageItemContainer_RightTapped(object sender, RightTappedRoutedEventArgs e)
            => _page.PackageItemContainer_RightTapped(sender, e);


        public async Task RightClick()
        {
            await _page.ShowContextMenu(this);
        }

        public void Package_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (e.PropertyName == nameof(Package.Tag))
                {
                    WhenTagHasChanged();
                    if (IsActionBlocked && Package.IsChecked)
                    {
                        Package.IsChecked = false;
                    }
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ListedOpacity)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AlternateIconId)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MainIconId)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AlternateIdIconVisible)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ListedNameTooltip)));
                    // 當 Tag 改變時，IsFromValidBucket、IsInstalled 和 ShouldShowStatusLight 狀態可能也會改變
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFromValidBucket)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInstalled)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShouldShowStatusLight)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PackageNameMargin)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PackageNameMarginGrid)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PackageNameMarginIcons)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActionBlocked)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanBeSelected)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ItemHighlightBrush)));
                }
                else if (e.PropertyName == nameof(Package.IsChecked))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                }
                else
                {
                    PropertyChanged?.Invoke(this, e);
                }
            }
            catch (COMException)
            {
                // ignore
            }
        }

        public void Dispose()
        {
            Package.PropertyChanged -= Package_PropertyChanged;
            // 停止狀態監控
            _statusMonitor?.Dispose();
        }

        /// <summary>
        /// Updates the fields that change how the item template is rendered.
        /// </summary>
        public void WhenTagHasChanged()
        {
            MainIconId = Package.Tag switch
            {
                PackageTag.Default => IconType.Id,
                PackageTag.AlreadyInstalled => IconType.Installed,
                PackageTag.IsUpgradable => IconType.Upgradable,
                PackageTag.Pinned => IconType.Pin,
                PackageTag.OnQueue => IconType.SandClock,
                PackageTag.BeingProcessed => IconType.Loading,
                PackageTag.Failed => IconType.Warning,
                PackageTag.Unavailable => IconType.Help,
                _ => throw new ArgumentException($"Unknown tag {Package.Tag}"),
            };

            AlternateIconId = Package.Tag switch
            {
                PackageTag.Default => IconType.Empty,
                PackageTag.AlreadyInstalled => IconType.Installed_Filled,
                PackageTag.IsUpgradable => IconType.Upgradable_Filled,
                PackageTag.Pinned => IconType.Pin_Filled,
                PackageTag.OnQueue => IconType.Empty,
                PackageTag.BeingProcessed => IconType.Loading_Filled,
                PackageTag.Failed => IconType.Warning_Filled,
                PackageTag.Unavailable => IconType.Empty,
                _ => throw new ArgumentException($"Unknown tag {Package.Tag}"),
            };
            AlternateIdIconVisible = AlternateIconId != IconType.Empty;

            ListedNameTooltip = Package.Tag switch
            {
                PackageTag.Default => "",
                PackageTag.AlreadyInstalled => CoreTools.Translate("This package is already installed") + " - ",
                PackageTag.IsUpgradable => CoreTools.Translate("This package can be upgraded to version {0}",
                    Package.GetUpgradablePackage()?.NewVersionString ?? "-1") + " - ",
                PackageTag.Pinned => CoreTools.Translate("Updates for this package are ignored") + " - ",
                PackageTag.OnQueue => CoreTools.Translate("This package is on the queue" + " - "),
                PackageTag.BeingProcessed => CoreTools.Translate("This package is being processed") + " - ",
                PackageTag.Failed => CoreTools.Translate("An error occurred while processing this package") + " - ",
                PackageTag.Unavailable => CoreTools.Translate("This package is not available") + " - ",
                _ => throw new ArgumentException($"Unknown tag {Package.Tag}"),
            } + Package.Name;

            ListedOpacity = Package.Tag switch
            {
                PackageTag.Default => 1,
                PackageTag.AlreadyInstalled => 1,
                PackageTag.IsUpgradable => 1,
                PackageTag.Pinned => 1,
                PackageTag.OnQueue => .5F,
                PackageTag.BeingProcessed => .5F,
                PackageTag.Failed => 1,
                PackageTag.Unavailable => .5F,
                _ => throw new ArgumentException($"Unknown tag {Package.Tag}"),
            };
#pragma warning restore CS8524

        }

        public void UpdatePackageIcon()
        {
            if (CachedPackageIcons.TryGetValue(Package.GetHash(), out Uri? icon))
            {
                MainIconSource = new BitmapImage
                {
                    UriSource = icon,
                    DecodePixelWidth = 64,
                    DecodePixelType = DecodePixelType.Logical,
                };
                ShowCustomPackageIcon = true;
                ShowDefaultPackageIcon = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MainIconSource)));
            }
            else
            {
                ShowCustomPackageIcon = false;
                ShowDefaultPackageIcon = true;
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowCustomPackageIcon)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDefaultPackageIcon)));
        }

    }
}
