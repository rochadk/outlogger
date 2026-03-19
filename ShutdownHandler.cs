using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LauncherLogout
{
    internal static class ShutdownHandler
    {
        private static readonly string AppDataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Outlogger");

        private static readonly string SteamBackupDir = Path.Combine(AppDataDir, "SteamBackup");
        private static readonly string EpicBackupDir  = Path.Combine(AppDataDir, "EpicBackup");

        private static string EpicConfigFile =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "EpicGamesLauncher", "Saved", "Config", "WindowsEditor", "GameUserSettings.ini");

        private static string EpicDataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "EpicGamesLauncher", "Saved", "Data");

        private static string EpicSavedDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "EpicGamesLauncher", "Saved");

        public static void LogoutAndRestoreDefaults()
        {
            try { LogoutAndRestoreSteam(); } catch { }
            try { LogoutAndRestoreEpic(); } catch { }
        }

        private static void KillProcess(string name)
        {
            foreach (var proc in Process.GetProcessesByName(name))
                try { proc.Kill(); } catch { }
        }

        private static void LogoutAndRestoreSteam()
        {
            bool steamHasBackup = Directory.Exists(SteamBackupDir) &&
                                  Directory.GetFiles(SteamBackupDir).Length > 0;
            if (!steamHasBackup) return;

            // Kill Steam
            KillProcess("steam");
            KillProcess("steamwebhelper");

            // Clear current session from registry
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam", writable: true))
            {
                key?.SetValue("AutoLoginUser", "", RegistryValueKind.String);
                key?.SetValue("RememberPassword", 0, RegistryValueKind.DWord);
            }

            // Restore ssfn session files
            string? steamDir = FindSteamDirectory();
            if (steamDir != null)
            {
                foreach (string file in Directory.GetFiles(SteamBackupDir, "ssfn*"))
                    File.Copy(file, Path.Combine(steamDir, Path.GetFileName(file)), overwrite: true);

                string vdfBackup = Path.Combine(SteamBackupDir, "loginusers.vdf");
                if (File.Exists(vdfBackup))
                    File.Copy(vdfBackup, Path.Combine(steamDir, "config", "loginusers.vdf"), overwrite: true);
            }

            // Restore registry AutoLoginUser
            string usernameFile = Path.Combine(SteamBackupDir, "username.txt");
            if (File.Exists(usernameFile))
            {
                string username = File.ReadAllText(usernameFile).Trim();
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam", writable: true);
                key?.SetValue("AutoLoginUser", username, RegistryValueKind.String);
                key?.SetValue("RememberPassword", 1, RegistryValueKind.DWord);
            }
        }

        private static void LogoutAndRestoreEpic()
        {
            bool epicHasBackup = Directory.Exists(EpicBackupDir) &&
                                 Directory.GetFiles(EpicBackupDir).Length > 0;
            if (!epicHasBackup) return;

            // Kill Epic
            KillProcess("EpicGamesLauncher");
            KillProcess("EpicWebHelper");
            KillProcess("EpicOnlineServicesUserHelper");
            KillProcess("msedgewebview2");

            // Clear webcache and dat files
            if (Directory.Exists(EpicSavedDir))
            {
                foreach (string dir in Directory.GetDirectories(EpicSavedDir, "webcache*"))
                    try { Directory.Delete(dir, recursive: true); } catch { }

                foreach (string file in Directory.GetFiles(EpicSavedDir, "*.dat"))
                    try { File.Delete(file); } catch { }

                if (Directory.Exists(EpicDataDir))
                    foreach (string file in Directory.GetFiles(EpicDataDir, "*.dat"))
                        try { File.Delete(file); } catch { }
            }

            // Restore dat files
            Directory.CreateDirectory(EpicDataDir);
            foreach (string file in Directory.GetFiles(EpicBackupDir, "*.dat"))
                File.Copy(file, Path.Combine(EpicDataDir, Path.GetFileName(file)), overwrite: true);

            // Restore RememberMe and Offline tokens in ini
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
    }
}
