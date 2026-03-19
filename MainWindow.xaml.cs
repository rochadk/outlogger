using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
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

        private static readonly string SteamBackupDir    = Path.Combine(AppDataDir, "SteamBackup");
        private static readonly string EpicBackupDir     = Path.Combine(AppDataDir, "EpicBackup");
        private static readonly string FirstRunFlagPath  = Path.Combine(AppDataDir, "initialized.flag");
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

        // ───────────────────────── Helpers ─────────────────────────

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
                SteamLogoutBtn.IsEnabled  = enabled;
                SteamLoginBtn.IsEnabled   = enabled;
                SteamSaveBtn.IsEnabled    = enabled;
                SteamClearBtn.IsEnabled   = enabled;
                EpicLogoutBtn.IsEnabled   = enabled;
                EpicLoginBtn.IsEnabled    = enabled;
                EpicSaveBtn.IsEnabled     = enabled;
                EpicClearBtn.IsEnabled    = enabled;
                LogoutAllBtn.IsEnabled    = enabled;
            });
        }

        private void RefreshCredentialsDisplay()
        {
            // Steam
            string usernameFile = Path.Combine(SteamBackupDir, "username.txt");
            bool steamSaved = File.Exists(usernameFile);
            if (steamSaved)
            {
                string username = File.ReadAllText(usernameFile).Trim();
                SteamCredentialText.Text = username;
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

        // ───────────────────────── Steam ─────────────────────────

        private void BackupSteamCredentials()
        {
            try
            {
                Directory.CreateDirectory(SteamBackupDir);

                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
                string? username = key?.GetValue("AutoLoginUser") as string;
                if (!string.IsNullOrEmpty(username))
                    File.WriteAllText(Path.Combine(SteamBackupDir, "username.txt"), username);

                string? steamDir = FindSteamDirectory();
                if (steamDir != null)
                {
                    int copied = 0;
                    foreach (string file in Directory.GetFiles(steamDir, "ssfn*"))
                    {
                        File.Copy(file, Path.Combine(SteamBackupDir, Path.GetFileName(file)), overwrite: true);
                        copied++;
                    }

                    string vdfPath = Path.Combine(steamDir, "config", "loginusers.vdf");
                    if (File.Exists(vdfPath))
                        File.Copy(vdfPath, Path.Combine(SteamBackupDir, "loginusers.vdf"), overwrite: true);

                    Log($"Steam: Saved default account '{username}' ({copied} session file(s)).");
                }
            }
            catch (Exception ex)
            {
                Log($"Steam: Backup warning - {ex.Message}");
            }
        }

        private async Task<bool> LogoutSteam()
        {
            ResetStatus(SteamStatus);
            bool allGood = true;

            try
            {
                string? steamExe = FindSteamExe();
                bool wasRunning = Process.GetProcessesByName("steam").Length > 0;

                if (wasRunning)
                {
                    if (steamExe != null)
                    {
                        Log("Steam: Sending shutdown command...");
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = steamExe,
                                Arguments = "-shutdown",
                                UseShellExecute = false
                            });
                            await Task.Delay(4000);
                        }
                        catch { Log("Steam: Graceful shutdown failed, force-killing..."); }
                    }

                    if (Process.GetProcessesByName("steam").Length > 0)
                    {
                        KillProcess("steam");
                        Log("Steam: Process terminated.");
                        await Task.Delay(1000);
                    }
                    else
                    {
                        Log("Steam: Shut down gracefully.");
                    }
                }
                else
                {
                    Log("Steam: Not currently running.");
                }

                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam", writable: true);
                    if (key != null)
                    {
                        key.SetValue("AutoLoginUser", "", RegistryValueKind.String);
                        key.SetValue("RememberPassword", 0, RegistryValueKind.DWord);
                        Log("Steam: Cleared auto-login registry keys.");
                    }
                    else
                    {
                        Log("Steam: Registry key not found.");
                        allGood = false;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Steam: Registry error - {ex.Message}");
                    allGood = false;
                }

                string? steamDir = FindSteamDirectory();
                if (steamDir != null)
                {
                    string vdfPath = Path.Combine(steamDir, "config", "loginusers.vdf");
                    if (File.Exists(vdfPath))
                    {
                        try
                        {
                            string content = File.ReadAllText(vdfPath);
                            string updated = Regex.Replace(
                                content,
                                @"""AllowAutoLogin""\s+""1""",
                                @"""AllowAutoLogin""		""0""");
                            File.WriteAllText(vdfPath, updated);
                            Log("Steam: Disabled auto-login in loginusers.vdf.");
                        }
                        catch (Exception ex)
                        {
                            Log($"Steam: Could not edit loginusers.vdf - {ex.Message}");
                            allGood = false;
                        }
                    }
                }
                else
                {
                    Log("Steam: Could not locate Steam directory.");
                    allGood = false;
                }

                SetStatus(SteamStatus, allGood ? "Logged out" : "Partial - check log", allGood);
            }
            catch (Exception ex)
            {
                Log($"Steam: Unexpected error - {ex.Message}");
                SetStatus(SteamStatus, "Error", false);
                return false;
            }

            RefreshCredentialsDisplay();
            return allGood;
        }

        private async Task<bool> LoginSteam()
        {
            ResetStatus(SteamStatus);

            bool backupExists = Directory.Exists(SteamBackupDir) &&
                                Directory.GetFiles(SteamBackupDir).Length > 0;
            if (!backupExists)
            {
                Log("Steam: No default account saved. Use 'Save Current' to save credentials first.");
                SetStatus(SteamStatus, "No account saved", false);
                return false;
            }

            try
            {
                if (Process.GetProcessesByName("steam").Length > 0)
                {
                    string? steamExe = FindSteamExe();
                    if (steamExe != null)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = steamExe,
                            Arguments = "-shutdown",
                            UseShellExecute = false
                        });
                        await Task.Delay(4000);
                    }
                    KillProcess("steam");
                    Log("Steam: Closed running instance.");
                    await Task.Delay(1000);
                }

                string? steamDir = FindSteamDirectory();
                if (steamDir != null)
                {
                    int restored = 0;
                    foreach (string file in Directory.GetFiles(SteamBackupDir, "ssfn*"))
                    {
                        File.Copy(file, Path.Combine(steamDir, Path.GetFileName(file)), overwrite: true);
                        restored++;
                    }
                    Log($"Steam: Restored {restored} session file(s).");

                    string vdfBackup = Path.Combine(SteamBackupDir, "loginusers.vdf");
                    if (File.Exists(vdfBackup))
                    {
                        File.Copy(vdfBackup, Path.Combine(steamDir, "config", "loginusers.vdf"), overwrite: true);
                        Log("Steam: Restored loginusers.vdf.");
                    }
                }

                string usernameFile = Path.Combine(SteamBackupDir, "username.txt");
                if (File.Exists(usernameFile))
                {
                    string username = File.ReadAllText(usernameFile).Trim();
                    using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam", writable: true);
                    if (key != null)
                    {
                        key.SetValue("AutoLoginUser", username, RegistryValueKind.String);
                        key.SetValue("RememberPassword", 1, RegistryValueKind.DWord);
                        Log($"Steam: Restored auto-login for '{username}'.");
                    }
                }

                string? steamExePath = FindSteamExe();
                if (steamExePath != null)
                {
                    Process.Start(new ProcessStartInfo { FileName = steamExePath, UseShellExecute = true });
                    string username = File.Exists(Path.Combine(SteamBackupDir, "username.txt"))
                        ? File.ReadAllText(Path.Combine(SteamBackupDir, "username.txt")).Trim()
                        : "default";
                    Log("Steam: Launched.");
                    SetStatus(SteamStatus, $"Logged in as {username}", true);
                }
                else
                {
                    Log("Steam: Could not find steam.exe.");
                    SetStatus(SteamStatus, "Error", false);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log($"Steam: Login error - {ex.Message}");
                SetStatus(SteamStatus, "Error", false);
                return false;
            }
        }

        private static string? FindSteamDirectory()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
                string? path = key?.GetValue("SteamPath") as string;
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    return path;
            }
            catch { }

            foreach (string p in new[] {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
                @"D:\Steam",
                @"D:\Program Files (x86)\Steam"
            })
                if (Directory.Exists(p)) return p;

            return null;
        }

        private static string? FindSteamExe()
        {
            string? dir = FindSteamDirectory();
            if (dir == null) return null;
            string exe = Path.Combine(dir, "steam.exe");
            return File.Exists(exe) ? exe : null;
        }

        // ───────────────────── Epic Games ──────────────────────

        private static string EpicConfigFile =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "EpicGamesLauncher", "Saved", "Config", "Windows", "GameUserSettings.ini");

        private static string EpicDataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "EpicGamesLauncher", "Saved", "Data");

        private static string EpicSavedDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "EpicGamesLauncher", "Saved");

        private void BackupEpicCredentials()
        {
            try
            {
                Directory.CreateDirectory(EpicBackupDir);

                if (Directory.Exists(EpicDataDir))
                {
                    foreach (string file in Directory.GetFiles(EpicDataDir, "*.dat"))
                        File.Copy(file, Path.Combine(EpicBackupDir, Path.GetFileName(file)), overwrite: true);
                }

                if (File.Exists(EpicConfigFile))
                {
                    string ini = File.ReadAllText(EpicConfigFile);

                    var rememberMe = Regex.Match(ini, @"\[RememberMe\].*?(?=\n\[|\z)", RegexOptions.Singleline);
                    var offline    = Regex.Match(ini, @"\[Offline\].*?(?=\n\[|\z)", RegexOptions.Singleline);

                    if (rememberMe.Success)
                        File.WriteAllText(Path.Combine(EpicBackupDir, "rememberme.txt"), rememberMe.Value);
                    if (offline.Success)
                        File.WriteAllText(Path.Combine(EpicBackupDir, "offline.txt"), offline.Value);
                }

                Log("Epic: Saved default account.");
            }
            catch (Exception ex)
            {
                Log($"Epic: Backup warning - {ex.Message}");
            }
        }

        private async Task<bool> LogoutEpic()
        {
            ResetStatus(EpicStatus);
            bool allGood = true;

            try
            {
                bool wasRunning =
                    Process.GetProcessesByName("EpicGamesLauncher").Length > 0 ||
                    Process.GetProcessesByName("EpicWebHelper").Length > 0;

                if (wasRunning)
                {
                    KillProcess("EpicGamesLauncher");
                    KillProcess("EpicWebHelper");
                    Log("Epic: Launcher processes terminated.");
                    await Task.Delay(1500);
                }
                else
                {
                    Log("Epic: Launcher not currently running.");
                }

                if (Directory.Exists(EpicSavedDir))
                {
                    try
                    {
                        int cleaned = 0;
                        foreach (string dir in Directory.GetDirectories(EpicSavedDir, "webcache*"))
                        {
                            try { Directory.Delete(dir, recursive: true); cleaned++; } catch { }
                        }
                        foreach (string file in Directory.GetFiles(EpicSavedDir, "*.dat"))
                        {
                            try { File.Delete(file); cleaned++; } catch { }
                        }
                        if (Directory.Exists(EpicDataDir))
                        {
                            foreach (string file in Directory.GetFiles(EpicDataDir, "*.dat"))
                            {
                                try { File.Delete(file); cleaned++; } catch { }
                            }
                        }
                        Log($"Epic: Cleared {cleaned} cache/credential item(s).");
                    }
                    catch (Exception ex)
                    {
                        Log($"Epic: Could not fully clear credentials - {ex.Message}");
                        allGood = false;
                    }
                }
                else
                {
                    Log("Epic: Saved directory not found.");
                    allGood = false;
                }

                if (File.Exists(EpicConfigFile))
                {
                    try
                    {
                        string ini = File.ReadAllText(EpicConfigFile);
                        ini = Regex.Replace(ini, @"(?m)^AutoLogin\s*=.*$", "AutoLogin=false");
                        ini = Regex.Replace(ini, @"(?m)^LastLoginUserDisplayName\s*=.*$", "LastLoginUserDisplayName=");
                        ini = Regex.Replace(ini, @"(?m)^Enable\s*=.*$", "Enable=False");
                        ini = Regex.Replace(ini, @"(?m)^Data\s*=.*$", "Data=");
                        File.WriteAllText(EpicConfigFile, ini);
                        Log("Epic: Cleared auth tokens.");
                    }
                    catch (Exception ex)
                    {
                        Log($"Epic: Could not update GameUserSettings.ini - {ex.Message}");
                    }
                }

                SetStatus(EpicStatus, allGood ? "Logged out" : "Partial - check log", allGood);
            }
            catch (Exception ex)
            {
                Log($"Epic: Unexpected error - {ex.Message}");
                SetStatus(EpicStatus, "Error", false);
                return false;
            }

            RefreshCredentialsDisplay();
            return allGood;
        }

        private async Task<bool> LoginEpic()
        {
            ResetStatus(EpicStatus);

            bool backupExists = Directory.Exists(EpicBackupDir) &&
                                Directory.GetFiles(EpicBackupDir).Length > 0;
            if (!backupExists)
            {
                Log("Epic: No default account saved. Use 'Save Current' to save credentials first.");
                SetStatus(EpicStatus, "No account saved", false);
                return false;
            }

            try
            {
                bool wasRunning =
                    Process.GetProcessesByName("EpicGamesLauncher").Length > 0 ||
                    Process.GetProcessesByName("EpicWebHelper").Length > 0;

                if (wasRunning)
                {
                    KillProcess("EpicGamesLauncher");
                    KillProcess("EpicWebHelper");
                    Log("Epic: Closed running launcher.");
                    await Task.Delay(1500);
                }

                Directory.CreateDirectory(EpicDataDir);
                int restored = 0;
                foreach (string file in Directory.GetFiles(EpicBackupDir, "*.dat"))
                {
                    File.Copy(file, Path.Combine(EpicDataDir, Path.GetFileName(file)), overwrite: true);
                    restored++;
                }
                Log($"Epic: Restored {restored} credential file(s).");

                string rememberMeBackup = Path.Combine(EpicBackupDir, "rememberme.txt");
                string offlineBackup    = Path.Combine(EpicBackupDir, "offline.txt");

                if (File.Exists(EpicConfigFile) && File.Exists(rememberMeBackup))
                {
                    string ini = File.ReadAllText(EpicConfigFile);
                    string rememberMeSection = File.ReadAllText(rememberMeBackup);

                    var dataMatch   = Regex.Match(rememberMeSection, @"(?m)^Data\s*=(.*)$");
                    var enableMatch = Regex.Match(rememberMeSection, @"(?m)^Enable\s*=(.*)$");

                    if (dataMatch.Success)
                        ini = Regex.Replace(ini, @"(?m)^Data\s*=.*$", $"Data={dataMatch.Groups[1].Value.Trim()}");
                    if (enableMatch.Success)
                        ini = Regex.Replace(ini, @"(?m)^Enable\s*=.*$", $"Enable={enableMatch.Groups[1].Value.Trim()}");

                    if (File.Exists(offlineBackup))
                    {
                        string offlineSection = File.ReadAllText(offlineBackup);
                        ini = Regex.Replace(ini, @"\[Offline\].*?(?=\n\[|\z)", offlineSection.TrimEnd(),
                                            RegexOptions.Singleline);
                    }

                    File.WriteAllText(EpicConfigFile, ini);
                    Log("Epic: Restored auth tokens.");
                }

                string? epicExe = FindEpicExe();
                if (epicExe != null)
                {
                    Process.Start(new ProcessStartInfo { FileName = epicExe, UseShellExecute = true });
                    Log("Epic: Launcher started.");
                    SetStatus(EpicStatus, "Logged in", true);
                }
                else
                {
                    Log("Epic: Could not find EpicGamesLauncher.exe.");
                    SetStatus(EpicStatus, "Partial - check log", false);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log($"Epic: Login error - {ex.Message}");
                SetStatus(EpicStatus, "Error", false);
                return false;
            }
        }

        private static string? FindEpicExe()
        {
            string[] candidates = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                             "Epic Games", "Launcher", "Portal", "Binaries", "Win32", "EpicGamesLauncher.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                             "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe"),
            };

            foreach (string path in candidates)
                if (File.Exists(path)) return path;

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher");
                string? installDir = key?.GetValue("AppDataPath") as string
                                  ?? key?.GetValue("InstallLocation") as string;
                if (installDir != null)
                {
                    string exe = Path.Combine(installDir, "Portal", "Binaries", "Win32", "EpicGamesLauncher.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
            catch { }

            return null;
        }

        // ───────────────────── Event Handlers ──────────────────────

        private async void SteamLogout_Click(object sender, RoutedEventArgs e)
        {
            SetButtonsEnabled(false);
            await LogoutSteam();
            SetButtonsEnabled(true);
        }

        private async void SteamLogin_Click(object sender, RoutedEventArgs e)
        {
            SetButtonsEnabled(false);
            await LoginSteam();
            SetButtonsEnabled(true);
        }

        private async void EpicLogout_Click(object sender, RoutedEventArgs e)
        {
            SetButtonsEnabled(false);
            await LogoutEpic();
            SetButtonsEnabled(true);
        }

        private async void EpicLogin_Click(object sender, RoutedEventArgs e)
        {
            SetButtonsEnabled(false);
            await LoginEpic();
            SetButtonsEnabled(true);
        }

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

        private void SteamSave_Click(object sender, RoutedEventArgs e)
        {
            BackupSteamCredentials();
            RefreshCredentialsDisplay();
        }

        private void EpicSave_Click(object sender, RoutedEventArgs e)
        {
            BackupEpicCredentials();
            RefreshCredentialsDisplay();
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

        private void SteamClear_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(SteamBackupDir))
            {
                Directory.Delete(SteamBackupDir, recursive: true);
                Log("Steam: Cleared saved credentials.");
            }
            RefreshCredentialsDisplay();
        }

        private void EpicClear_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(EpicBackupDir))
            {
                Directory.Delete(EpicBackupDir, recursive: true);
                Log("Epic: Cleared saved credentials.");
            }
            RefreshCredentialsDisplay();
        }
    }
}
