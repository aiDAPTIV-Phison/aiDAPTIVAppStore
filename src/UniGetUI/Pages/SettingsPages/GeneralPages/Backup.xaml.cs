using System.Data;
using System.Diagnostics;
using System.Security.Authentication;
using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using UniGetUI.Core.Data;
using UniGetUI.Core.Logging;
using UniGetUI.Core.SettingsEngine;
using UniGetUI.Core.Tools;
using UniGetUI.Interface.SoftwarePages;
using UniGetUI.PackageEngine.Enums;
using UniGetUI.Pages.DialogPages;
using UniGetUI.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace UniGetUI.Pages.SettingsPages.GeneralPages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class Backup : Page, ISettingsPage
    {
        // === GitHub 登入功能已停用 ===
        // private readonly GitHubAuthService _authService;
        // private readonly GitHubBackupService _backupService;
        // private bool _isLoggedIn;
        // private bool _isLoading;
        public Backup()
        {
            this.InitializeComponent();

            // === GitHub 登入功能已停用 ===
            // _authService = new GitHubAuthService();
            // _backupService = new GitHubBackupService(_authService);

            EnablePackageBackupUI(Settings.Get(Settings.K.EnablePackageBackup_LOCAL));
            ResetBackupDirectory.Content = CoreTools.Translate("Reset");
            OpenBackupDirectory.Content = CoreTools.Translate("Open");

            // === GitHub 登入功能已停用 ===
            // GitHubAuthService.AuthStatusChanged += (_, _) => _ = UpdateGitHubLoginStatus();
            // EnablePackageBackupCheckBox_CLOUD.StateChanged += EnablePackageBackupCheckBox_CLOUD_StateChanged;
            // _ = UpdateGitHubLoginStatus();
        }



        public bool CanGoBack => true;

        public string ShortTitle => CoreTools.Translate("Backup and Restore");

        public event EventHandler? RestartRequired;
        public event EventHandler<Type>? NavigationRequested;

        public void ShowRestartBanner(object? sender, EventArgs e)
            => RestartRequired?.Invoke(this, e);

        private void ChangeBackupDirectory_Click(object sender, EventArgs e)
        {
            ExternalLibraries.Pickers.FolderPicker openPicker = new(MainApp.Instance.MainWindow.GetWindowHandle());
            string folder = openPicker.Show();
            if (folder != string.Empty)
            {
                Settings.SetValue(Settings.K.ChangeBackupOutputDirectory, folder);
                BackupDirectoryLabel.Text = folder;
                ResetBackupDirectory.IsEnabled = true;
            }
        }

        public void EnablePackageBackupUI(bool enabled)
        {
            EnableBackupTimestampingCheckBox.IsEnabled = enabled;
            ChangeBackupFileNameTextBox.IsEnabled = enabled;
            ChangeBackupDirectory.IsEnabled = enabled;
            BackupNowButton_LOCAL.IsEnabled = enabled;

            if (enabled)
            {
                if (!Settings.Get(Settings.K.ChangeBackupOutputDirectory))
                {
                    BackupDirectoryLabel.Text = CoreData.UniGetUI_DefaultBackupDirectory;
                    ResetBackupDirectory.IsEnabled = false;
                }
                else
                {
                    BackupDirectoryLabel.Text = Settings.GetValue(Settings.K.ChangeBackupOutputDirectory);
                    ResetBackupDirectory.IsEnabled = true;
                }
            }
        }

        private void ResetBackupPath_Click(object sender, RoutedEventArgs e)
        {
            BackupDirectoryLabel.Text = CoreData.UniGetUI_DefaultBackupDirectory;
            Settings.Set(Settings.K.ChangeBackupOutputDirectory, false);
            ResetBackupDirectory.IsEnabled = false;
        }

        private void OpenBackupPath_Click(object sender, RoutedEventArgs e)
        {
            string directory = Settings.GetValue(Settings.K.ChangeBackupOutputDirectory);
            if (directory == "") directory = CoreData.UniGetUI_DefaultBackupDirectory;

            directory = directory.Replace("/", "\\");
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            CoreTools.Launch(directory);
        }

        private void DoBackup_LOCAL_Click(object sender, EventArgs e) => _ = _doBackup_LOCAL_Click();
        private static async Task _doBackup_LOCAL_Click()
        {
            int loadingId = DialogHelper.ShowLoadingDialog(CoreTools.Translate("Performing backup, please wait..."));
            await InstalledPackagesPage.BackupPackages_LOCAL();
            DialogHelper.HideLoadingDialog(loadingId);
        }

        /* === GitHub 登入功能已停用 - Cloud Backup 方法已註解 ===
         *
         *       BEGIN CLOUD BACKUP METHODS (已停用)
         *
         */
        // private async Task UpdateGitHubLoginStatus()
        // {
        //     GitHubAuthService authService = new();
        //     if (authService.IsAuthenticated())
        //     {
        //         try
        //         {
        //             await GenerateLogoutUI(authService);
        //         }
        //         catch (Exception ex)
        //         {
        //             Logger.Error("An error occurred while attempting to generate settings login UI: ");
        //             Logger.Error(ex);
        //             GenerateLoginUI();
        //         }
        //     }
        //     else
        //     {
        //         GenerateLoginUI();
        //     }
        //     UpdateCloudControlsEnabled();
        // }

        // private void GenerateLoginUI()
        // {
        //     _isLoggedIn = false;
        //     LogInButton.Visibility = Visibility.Visible;
        //     LogOutButton.Visibility = Visibility.Collapsed;
        //     GitHubUserTitle.Text = CoreTools.Translate("Current status: Not logged in");
        //     GitHubUserSubtitle.Text = CoreTools.Translate("Log in to enable cloud backup");
        //     GitHubImage.ProfilePicture = null;
        // }

        // private async Task GenerateLogoutUI(GitHubAuthService authService)
        // {
        //     var client = authService.CreateGitHubClient();
        //     if (client is null) throw new AuthenticationException("How can it be authenticated and fail to create a client?");
        //     var user = await client.User.Current();
        //
        //     _isLoggedIn = true;
        //     LogInButton.Visibility = Visibility.Collapsed;
        //     LogOutButton.Visibility = Visibility.Visible;
        //     GitHubUserTitle.Text = CoreTools.Translate("You are logged in as {0} (@{1})", user.Name, user.Login);
        //     GitHubUserSubtitle.Text = CoreTools.Translate("Nice! Backups will be uploaded to a private gist on your account");
        //     GitHubImage.Initials = "";
        //     GitHubImage.ProfilePicture = new BitmapImage(new Uri(user.AvatarUrl));
        // }

        // private void UpdateCloudControlsEnabled()
        // {
        //     LogInButton.IsEnabled = !_isLoading;
        //     LogOutButton.IsEnabled = !_isLoading;
        //     if (_isLoggedIn && !_isLoading)
        //     {
        //         EnablePackageBackupCheckBox_CLOUD.IsEnabled = true;
        //         RestorePackagesFromGitHubButton.IsEnabled = true;
        //         BackupNowButton_Cloud.IsEnabled = Settings.Get(Settings.K.EnablePackageBackup_CLOUD);
        //     }
        //     else
        //     {
        //         EnablePackageBackupCheckBox_CLOUD.IsEnabled = false;
        //         BackupNowButton_Cloud.IsEnabled = false;
        //         RestorePackagesFromGitHubButton.IsEnabled = false;
        //     }
        // }

        // === GitHub 登入按鈕事件處理程式 - 保留但不執行任何操作 ===
        private void LoginWithGitHubButton_Click(object sender, RoutedEventArgs e)
        {
            // GitHub 登入功能已停用
        }

        private void LogoutGitHubButton_Click(object sender, RoutedEventArgs e)
        {
            // GitHub 登出功能已停用
        }

        private void RestoreFromGitHubButton_Click(object sender, EventArgs e)
        {
            // GitHub 還原功能已停用
        }

        private void BackupToGitHubButton_Click(object sender, EventArgs e)
        {
            // GitHub 備份功能已停用
        }

        // private void EnablePackageBackupCheckBox_CLOUD_StateChanged(object? sender, EventArgs e)
        // {
        //     ShowRestartBanner(sender, e);
        //     UpdateCloudControlsEnabled();
        // }

        private void MoreInfoBtn_OnClick(object sender, RoutedEventArgs e)
        {
            // GitHub 登入功能已停用 - 不顯示說明
            // MainApp.Instance.MainWindow.NavigationPage.ShowHelp("cloud-backup-overview/");
        }
        /* === GitHub 登入功能已停用 - Cloud Backup 方法結束 === */
    }
}
