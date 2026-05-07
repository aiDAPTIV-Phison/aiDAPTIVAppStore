using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UniGetUI.Core.Tools;
using UniGetUI.Pages.SettingsPages.GeneralPages;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace UniGetUI.Pages.SettingsPages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SettingsHomepage : Page, ISettingsPage
    {
        public bool CanGoBack => false;
        public string ShortTitle => CoreTools.Translate("WingetUI Settings");

        public event EventHandler? RestartRequired;

        public event EventHandler<Type>? NavigationRequested;

        public SettingsHomepage()
        {
            this.InitializeComponent();
        }
        // Hidden: 管理者權限和其他危險設定
        // public void Administrator(object s, RoutedEventArgs e) => NavigationRequested?.Invoke(this, typeof(Administrator));
        // Hidden: 備份與還原
        // public void Backup(object s, RoutedEventArgs e) => NavigationRequested?.Invoke(this, typeof(Backup));
        // Hidden: 實驗性設定與開發選項
        // public void Experimental(object s, RoutedEventArgs e) => NavigationRequested?.Invoke(this, typeof(Experimental));
        public void General(object s, RoutedEventArgs e) => NavigationRequested?.Invoke(this, typeof(General));
        public void Interface(object s, RoutedEventArgs e) => NavigationRequested?.Invoke(this, typeof(Interface_P));
        public void Notifications(object s, RoutedEventArgs e) => NavigationRequested?.Invoke(this, typeof(Notifications));
        // Hidden: 封裝操作偏好設定
        // public void Operations(object s, RoutedEventArgs e) => NavigationRequested?.Invoke(this, typeof(Operations));
        // Hidden: 更新工具
        // public void Startup(object s, RoutedEventArgs e) => NavigationRequested?.Invoke(this, typeof(Updates));
        // Hidden: 網路連線設定
        // private void Internet(object sender, RoutedEventArgs e) => NavigationRequested?.Invoke(this, typeof(Internet));
        // Hidden: 套件管理平台偏好設定
        // private void ManagersShortcut(object sender, RoutedEventArgs e) => NavigationRequested?.Invoke(this, typeof(ManagersHomepage));
    }
}
