using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace LauncherLogout
{
    public partial class MainWindow : Window
    {
        private static readonly string AppDataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Outlogger");

        private static readonly string SteamBackupDir     = Path.Combine(AppDataDir, "SteamBackup");
        private static readonly string EpicBackupDir      = Path.Combine(AppDataDir, "EpicBackup");
        private static readonly string FirstRunFlagPath   = Path.Combine(AppDataDir, "initialized.flag");
        private static readonly string AutoLogoutFlagPath = Path.Combine(AppDataDir, "auto_logout.flag");

        private Forms.NotifyIcon _trayIcon = null!;
        private bool _reallyClose;

        public MainWindow()
        {
            InitializeComponent();
            InitTrayIcon();
        }

        private void InitTrayIcon()
        {
            _trayIcon = new Forms.NotifyIcon
            {
                Text = "Outlogger",
                Icon = System.Drawing.SystemIcons.Application,
                Visible = true
            };

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open", null, (_, _) => ShowWindow());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => { _reallyClose = true; Close(); });
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (_, _) => ShowWindow();
        }

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_reallyClose)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            AutoLogoutToggle.IsChecked = File.Exists(AutoLogoutFlagPath);
            StartupToggle.IsChecked = IsStartupEnabled();
            RefreshCredentialsDisplay();

            if (!File.Exists(FirstRunFlagPath))
            {
                var dlg = new WelcomeDialog { Owner = this };
                dlg.ShowDialog();
                Directory.CreateDirectory(AppDataDir);
                File.WriteAllText(FirstRunFlagPath, "1");
            }
        }

        // ───────────────────────── Shared helpers ─────────────────────────

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogText.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
                LogScroller.ScrollToEnd();
            });
        }

        private void SetStatus(TextBlock label, string text, bool success)
        {
            Dispatcher.Invoke(() =>
            {
                label.Text = text;
                label.Foreground = success
                    ? (Brush)FindResource("SuccessGreenBrush")
                    : (Brush)FindResource("ErrorRedBrush");
            });
        }

        private void ResetStatus(TextBlock label)
        {
            Dispatcher.Invoke(() =>
            {
                label.Text = "Working...";
                label.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xC0, 0x40));
            });
        }

        private static bool KillProcess(string processName)
        {
            bool killed = false;
            foreach (var proc in Process.GetProcessesByName(processName))
            {
                try { proc.Kill(); proc.WaitForExit(5000); killed = true; }
                catch { }
            }
            return killed;
        }

        private void SetButtonsEnabled(bool enabled)
        {
            Dispatcher.Invoke(() =>
            {
                SteamLogoutBtn.IsEnabled = enabled;
                SteamLoginBtn.IsEnabled  = enabled;
                SteamSaveBtn.IsEnabled   = enabled;
                SteamClearBtn.IsEnabled  = enabled;
                EpicLogoutBtn.IsEnabled  = enabled;
                EpicLoginBtn.IsEnabled   = enabled;
                EpicSaveBtn.IsEnabled    = enabled;
                EpicClearBtn.IsEnabled   = enabled;
                LogoutAllBtn.IsEnabled   = enabled;
            });
        }

        private void RefreshCredentialsDisplay()
        {
            // Steam
            string usernameFile = Path.Combine(SteamBackupDir, "username.txt");
            if (File.Exists(usernameFile))
            {
                SteamCredentialText.Text = File.ReadAllText(usernameFile).Trim();
                SteamCredentialText.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
                SteamClearBtn.Visibility = Visibility.Visible;
            }
            else
            {
                SteamCredentialText.Text = "Not saved";
                SteamCredentialText.Foreground = (Brush)FindResource("TextMutedBrush");
                SteamClearBtn.Visibility = Visibility.Collapsed;
            }

            // Epic
            bool epicSaved = Directory.Exists(EpicBackupDir) &&
                             Directory.GetFiles(EpicBackupDir).Length > 0;
            if (epicSaved)
            {
                EpicCredentialText.Text = "Account saved";
                EpicCredentialText.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
                EpicClearBtn.Visibility = Visibility.Visible;
            }
            else
            {
                EpicCredentialText.Text = "Not saved";
                EpicCredentialText.Foreground = (Brush)FindResource("TextMutedBrush");
                EpicClearBtn.Visibility = Visibility.Collapsed;
            }
        }

        // ───────────────────────── Event handlers ─────────────────────────

        private async void LogoutAll_Click(object sender, RoutedEventArgs e)
        {
            SetButtonsEnabled(false);
            Log("── Logging out of all launchers ──");
            bool steam = await LogoutSteam();
            bool epic  = await LogoutEpic();
            if (steam && epic)
                Log("All launchers logged out successfully.");
            else
                Log("Some launchers had issues - check details above.");
            SetButtonsEnabled(true);
        }

        private void AutoLogoutToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (AutoLogoutToggle.IsChecked == true)
            {
                Directory.CreateDirectory(AppDataDir);
                File.WriteAllText(AutoLogoutFlagPath, "1");
            }
            else
            {
                if (File.Exists(AutoLogoutFlagPath)) File.Delete(AutoLogoutFlagPath);
            }
        }

        private void StartupToggle_Changed(object sender, RoutedEventArgs e)
        {
            SetStartupEnabled(StartupToggle.IsChecked == true);
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                return key?.GetValue("Outlogger") != null;
            }
            catch { return false; }
        }

        private static void SetStartupEnabled(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (key == null) return;
                if (enable)
                {
                    string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
                    key.SetValue("Outlogger", $"\"{exePath}\"", RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue("Outlogger", throwOnMissingValue: false);
                }
            }
            catch { }
        }
    }
}
