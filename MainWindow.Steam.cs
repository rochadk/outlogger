using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace LauncherLogout
{
    public partial class MainWindow
    {
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

        // ── Event handlers ──

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

        private void SteamSave_Click(object sender, RoutedEventArgs e)
        {
            BackupSteamCredentials();
            RefreshCredentialsDisplay();
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
    }
}
