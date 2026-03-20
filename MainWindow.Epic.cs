using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace LauncherLogout
{
    public partial class MainWindow
    {
        // ── Paths ──

        private static string EpicConfigFile =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "EpicGamesLauncher", "Saved", "Config", "WindowsEditor", "GameUserSettings.ini");

        private static string EpicDataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "EpicGamesLauncher", "Saved", "Data");

        private static string EpicSavedDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "EpicGamesLauncher", "Saved");

        private static string EpicWebViewDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "EpicGamesLauncher", "EBWebView");

        private static string EpicWebViewBackupDir => Path.Combine(EpicBackupDir, "EBWebView");

        // EOS service cache directories that store auth state
        private static string EosUiHelperCacheDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Epic Games", "Epic Online Services", "UI Helper", "Cache");

        private static string EosOverlayCacheDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Epic Games", "EOSOverlay", "BrowserCache");

        // Auth-relevant subdirectories/files within EBWebView\Default to backup.
        // Skips Cache and Code Cache which are large and not needed for auth.
        private static readonly string[] WebViewAuthPaths =
        [
            @"Default\Cookies",
            @"Default\Network",
            @"Default\Local Storage",
            @"Default\Session Storage",
            @"Default\Preferences",
        ];

        private static void BackupEpicWebView()
        {
            if (!Directory.Exists(EpicWebViewDir)) return;

            if (Directory.Exists(EpicWebViewBackupDir))
                Directory.Delete(EpicWebViewBackupDir, recursive: true);

            foreach (string subPath in WebViewAuthPaths)
            {
                string src = Path.Combine(EpicWebViewDir, subPath);
                string dst = Path.Combine(EpicWebViewBackupDir, subPath);

                if (File.Exists(src))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    try { File.Copy(src, dst, overwrite: true); } catch { }
                }
                else if (Directory.Exists(src))
                {
                    CopyDirectoryRecursive(src, dst);
                }
            }
        }

        private static void RestoreEpicWebView()
        {
            if (!Directory.Exists(EpicWebViewBackupDir)) return;

            if (Directory.Exists(EpicWebViewDir))
            {
                bool deleted = false;
                for (int attempt = 0; attempt < 4 && !deleted; attempt++)
                {
                    if (attempt > 0) System.Threading.Thread.Sleep(1000);
                    try { Directory.Delete(EpicWebViewDir, recursive: true); deleted = true; }
                    catch { }
                }
                if (!deleted) return; // Can't clear it — skip restore rather than corrupting
            }

            CopyDirectoryRecursive(EpicWebViewBackupDir, EpicWebViewDir);
        }

        private static void CopyDirectoryRecursive(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string file in Directory.GetFiles(src))
            {
                try { File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true); }
                catch { }
            }
            foreach (string dir in Directory.GetDirectories(src))
                CopyDirectoryRecursive(dir, Path.Combine(dst, Path.GetFileName(dir)));
        }

        // ── Windows Credential Manager P/Invoke ──

        [StructLayout(LayoutKind.Explicit)]
        private struct NATIVE_CREDENTIAL
        {
            [FieldOffset(0)]  public uint   Flags;
            [FieldOffset(4)]  public uint   Type;
            [FieldOffset(8)]  public IntPtr TargetName;
            [FieldOffset(16)] public IntPtr Comment;
            [FieldOffset(24)] public uint   LastWrittenLow;
            [FieldOffset(28)] public uint   LastWrittenHigh;
            [FieldOffset(32)] public uint   CredentialBlobSize;
            [FieldOffset(40)] public IntPtr CredentialBlob;
            [FieldOffset(48)] public uint   Persist;
            [FieldOffset(52)] public uint   AttributeCount;
            [FieldOffset(56)] public IntPtr Attributes;
            [FieldOffset(64)] public IntPtr TargetAlias;
            [FieldOffset(72)] public IntPtr UserName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredEnumerateW(string filter, uint flags, out uint count, out IntPtr credentials);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDeleteW(string target, uint type, uint reserved);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWriteW(ref NATIVE_CREDENTIAL credential, uint flags);

        private record SavedCredential(string Target, uint Type, string UserName, uint Persist, string BlobBase64);

        private static List<SavedCredential> GetEpicWindowsCredentials()
        {
            var result = new List<SavedCredential>();
            // CRED_ENUMERATE_ALL_CREDENTIALS = 1 (filter is ignored when this flag is set)
            if (!CredEnumerateW("", 1, out uint count, out IntPtr pCreds)) return result;
            try
            {
                for (int i = 0; i < (int)count; i++)
                {
                    IntPtr credPtr = Marshal.ReadIntPtr(pCreds, i * IntPtr.Size);
                    var cred = Marshal.PtrToStructure<NATIVE_CREDENTIAL>(credPtr);

                    string target = cred.TargetName != IntPtr.Zero
                        ? Marshal.PtrToStringUni(cred.TargetName) ?? "" : "";
                    if (!target.Contains("epic", StringComparison.OrdinalIgnoreCase) &&
                        !target.Contains("EOS",  StringComparison.OrdinalIgnoreCase))
                        continue;

                    string username = cred.UserName != IntPtr.Zero
                        ? Marshal.PtrToStringUni(cred.UserName) ?? "" : "";
                    string blob = "";
                    if (cred.CredentialBlob != IntPtr.Zero && cred.CredentialBlobSize > 0)
                    {
                        byte[] bytes = new byte[cred.CredentialBlobSize];
                        Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
                        blob = Convert.ToBase64String(bytes);
                    }
                    result.Add(new SavedCredential(target, cred.Type, username, cred.Persist, blob));
                }
            }
            finally { CredFree(pCreds); }
            return result;
        }

        private static void DeleteEpicWindowsCredentials()
        {
            foreach (var c in GetEpicWindowsCredentials())
                CredDeleteW(c.Target, c.Type, 0);
        }

        private static void RestoreEpicWindowsCredentials(List<SavedCredential> credentials)
        {
            foreach (var saved in credentials)
            {
                byte[] blob = string.IsNullOrEmpty(saved.BlobBase64)
                    ? [] : Convert.FromBase64String(saved.BlobBase64);

                IntPtr targetPtr = Marshal.StringToHGlobalUni(saved.Target);
                IntPtr userPtr   = string.IsNullOrEmpty(saved.UserName)
                    ? IntPtr.Zero : Marshal.StringToHGlobalUni(saved.UserName);
                IntPtr blobPtr   = blob.Length > 0 ? Marshal.AllocHGlobal(blob.Length) : IntPtr.Zero;
                if (blobPtr != IntPtr.Zero) Marshal.Copy(blob, 0, blobPtr, blob.Length);
                try
                {
                    var cred = new NATIVE_CREDENTIAL
                    {
                        Type               = saved.Type,
                        TargetName         = targetPtr,
                        UserName           = userPtr,
                        CredentialBlob     = blobPtr,
                        CredentialBlobSize = (uint)blob.Length,
                        Persist            = saved.Persist,
                    };
                    CredWriteW(ref cred, 0);
                }
                finally
                {
                    Marshal.FreeHGlobal(targetPtr);
                    if (userPtr != IntPtr.Zero) Marshal.FreeHGlobal(userPtr);
                    if (blobPtr != IntPtr.Zero) Marshal.FreeHGlobal(blobPtr);
                }
            }
        }

        // ── Backup / logout / login ──

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
                    var offline    = Regex.Match(ini, @"\[Offline\].*?(?=\n\[|\z)",    RegexOptions.Singleline);

                    if (rememberMe.Success)
                        File.WriteAllText(Path.Combine(EpicBackupDir, "rememberme.txt"), rememberMe.Value);
                    if (offline.Success)
                        File.WriteAllText(Path.Combine(EpicBackupDir, "offline.txt"), offline.Value);
                }

                var wincreds = GetEpicWindowsCredentials();
                if (wincreds.Count > 0)
                {
                    File.WriteAllText(Path.Combine(EpicBackupDir, "wincreds.json"),
                                      JsonSerializer.Serialize(wincreds));
                    Log($"Epic: Saved {wincreds.Count} Windows credential(s).");
                }

                BackupEpicWebView();
                Log("Epic: Saved browser session.");

                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Epic Games\Unreal Engine\Identifiers");
                    string? accountId = key?.GetValue("AccountId") as string;
                    if (!string.IsNullOrEmpty(accountId))
                        File.WriteAllText(Path.Combine(EpicBackupDir, "accountid.txt"), accountId);
                }
                catch { }

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
                string[] epicProcesses = [
                    "EpicGamesLauncher", "EpicWebHelper",
                    "EpicOnlineServicesUserHelper", "msedgewebview2"
                ];

                bool wasRunning = false;
                foreach (string name in epicProcesses)
                    if (KillProcess(name)) wasRunning = true;

                if (wasRunning)
                {
                    Log("Epic: Processes terminated.");
                    await Task.Delay(2500);
                }
                else
                {
                    Log("Epic: No Epic processes were running.");
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

                try
                {
                    DeleteEpicWindowsCredentials();
                    Log("Epic: Cleared Windows credentials.");
                }
                catch (Exception ex) { Log($"Epic: Could not clear Windows credentials - {ex.Message}"); }

                if (Directory.Exists(EpicWebViewDir))
                {
                    bool cleared = false;
                    for (int attempt = 0; attempt < 4 && !cleared; attempt++)
                    {
                        if (attempt > 0) await Task.Delay(1000);
                        try { Directory.Delete(EpicWebViewDir, recursive: true); cleared = true; }
                        catch { }
                    }

                    if (cleared)
                    {
                        Log("Epic: Cleared browser session.");
                    }
                    else
                    {
                        // Directory still locked — fall back to clearing just the auth-relevant files
                        Log("Epic: Browser session dir locked, clearing individual auth files.");
                        foreach (string subPath in WebViewAuthPaths)
                        {
                            string path = Path.Combine(EpicWebViewDir, subPath);
                            if (File.Exists(path))
                                try { File.Delete(path); } catch { }
                            else if (Directory.Exists(path))
                                foreach (string f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                                    try { File.Delete(f); } catch { }
                        }
                        allGood = false;
                    }
                }
                // else: EBWebView doesn't exist on this install — nothing to clear

                // Clear EOS service caches (store auth state independently of EGL)
                foreach (string dir in new[] { EosUiHelperCacheDir, EosOverlayCacheDir })
                {
                    if (Directory.Exists(dir))
                        try { Directory.Delete(dir, recursive: true); }
                        catch { }
                }

                // Remove AccountId from registry — this is what EGL uses to identify which account to resume
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(
                        @"Software\Epic Games\Unreal Engine\Identifiers", writable: true);
                    if (key?.GetValue("AccountId") != null)
                    {
                        key.DeleteValue("AccountId");
                        Log("Epic: Cleared account identity.");
                    }
                }
                catch (Exception ex) { Log($"Epic: Could not clear account identity - {ex.Message}"); }

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
                string[] epicProcesses = [
                    "EpicGamesLauncher", "EpicWebHelper",
                    "EpicOnlineServicesUserHelper", "msedgewebview2"
                ];

                bool wasRunning = false;
                foreach (string name in epicProcesses)
                    if (KillProcess(name)) wasRunning = true;

                if (wasRunning)
                {
                    Log("Epic: Closed running processes.");
                    await Task.Delay(2500);
                }
                else
                {
                    // Processes may have crashed recently — give the OS a moment
                    // to release any lingering file handles before restoring.
                    await Task.Delay(1000);
                }

                // Clear any existing .dat files (may belong to a different user)
                if (Directory.Exists(EpicDataDir))
                    foreach (string file in Directory.GetFiles(EpicDataDir, "*.dat"))
                        try { File.Delete(file); } catch { }

                Directory.CreateDirectory(EpicDataDir);
                int restored = 0;
                foreach (string file in Directory.GetFiles(EpicBackupDir, "*.dat"))
                {
                    File.Copy(file, Path.Combine(EpicDataDir, Path.GetFileName(file)), overwrite: true);
                    restored++;
                }
                Log($"Epic: Restored {restored} credential file(s).");

                string winCredsFile = Path.Combine(EpicBackupDir, "wincreds.json");
                if (File.Exists(winCredsFile))
                {
                    try
                    {
                        var saved = JsonSerializer.Deserialize<List<SavedCredential>>(
                            File.ReadAllText(winCredsFile));
                        if (saved != null && saved.Count > 0)
                        {
                            DeleteEpicWindowsCredentials();
                            RestoreEpicWindowsCredentials(saved);
                            Log($"Epic: Restored {saved.Count} Windows credential(s).");
                        }
                    }
                    catch (Exception ex) { Log($"Epic: Windows credential restore warning - {ex.Message}"); }
                }

                string rememberMeBackup = Path.Combine(EpicBackupDir, "rememberme.txt");
                string offlineBackup    = Path.Combine(EpicBackupDir, "offline.txt");

                if (File.Exists(rememberMeBackup))
                {
                    // Config file may be absent or have its sections stripped by Epic's own logout
                    string ini = File.Exists(EpicConfigFile) ? File.ReadAllText(EpicConfigFile) : "";
                    string rememberMeSection = File.ReadAllText(rememberMeBackup);

                    if (Regex.IsMatch(ini, @"\[RememberMe\]", RegexOptions.Singleline))
                        ini = Regex.Replace(ini, @"\[RememberMe\].*?(?=\n\[|\z)", rememberMeSection.TrimEnd(),
                                            RegexOptions.Singleline);
                    else
                        ini = ini.TrimEnd() + (ini.Length > 0 ? "\n" : "") + rememberMeSection.TrimEnd() + "\n";

                    if (File.Exists(offlineBackup))
                    {
                        string offlineSection = File.ReadAllText(offlineBackup);
                        if (Regex.IsMatch(ini, @"\[Offline\]", RegexOptions.Singleline))
                            ini = Regex.Replace(ini, @"\[Offline\].*?(?=\n\[|\z)", offlineSection.TrimEnd(),
                                                RegexOptions.Singleline);
                        else
                            ini = ini.TrimEnd() + "\n" + offlineSection.TrimEnd() + "\n";
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(EpicConfigFile)!);
                    File.WriteAllText(EpicConfigFile, ini);
                    Log("Epic: Restored auth tokens.");
                }

                RestoreEpicWebView();
                Log("Epic: Restored browser session.");

                string accountIdFile = Path.Combine(EpicBackupDir, "accountid.txt");
                if (File.Exists(accountIdFile))
                {
                    try
                    {
                        string accountId = File.ReadAllText(accountIdFile).Trim();
                        using var key = Registry.CurrentUser.CreateSubKey(
                            @"Software\Epic Games\Unreal Engine\Identifiers");
                        key?.SetValue("AccountId", accountId, RegistryValueKind.String);
                        Log("Epic: Restored account identity.");
                    }
                    catch (Exception ex) { Log($"Epic: Could not restore account identity - {ex.Message}"); }
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

        // ── Event handlers ──

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

        private void EpicSave_Click(object sender, RoutedEventArgs e)
        {
            BackupEpicCredentials();
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

        private void EpicDiagnose_Click(object sender, RoutedEventArgs e)
        {
            Log("── Epic Diagnose ────────────────────────────");
            LogEpicBackupState();
            LogEpicLiveState();
            Log("─────────────────────────────────────────────");
        }

        private void LogEpicBackupState()
        {
            Log("  [Backup]");
            if (!Directory.Exists(EpicBackupDir)) { Log("    No backup directory found."); return; }

            // .dat files
            var datFiles = Directory.GetFiles(EpicBackupDir, "*.dat");
            Log($"    .dat files: {datFiles.Length}" + (datFiles.Length > 0
                ? " (" + string.Join(", ", Array.ConvertAll(datFiles, Path.GetFileName)) + ")" : ""));

            // rememberme.txt
            string rmPath = Path.Combine(EpicBackupDir, "rememberme.txt");
            if (File.Exists(rmPath))
            {
                string rmText = File.ReadAllText(rmPath);
                var dataMatch = Regex.Match(rmText, @"^Data\s*=(.*)$", RegexOptions.Multiline);
                string dataVal = dataMatch.Success ? dataMatch.Groups[1].Value.Trim() : "(not found)";
                Log($"    rememberme.txt: present, Data= {Truncate(dataVal)}");
                var enableMatch = Regex.Match(rmText, @"^Enable\s*=(.*)$", RegexOptions.Multiline);
                if (enableMatch.Success) Log($"    rememberme Enable= {enableMatch.Groups[1].Value.Trim()}");
            }
            else Log("    rememberme.txt: absent");

            // offline.txt
            Log($"    offline.txt: {(File.Exists(Path.Combine(EpicBackupDir, "offline.txt")) ? "present" : "absent")}");

            // wincreds.json
            string wcPath = Path.Combine(EpicBackupDir, "wincreds.json");
            if (File.Exists(wcPath))
            {
                try
                {
                    var saved = JsonSerializer.Deserialize<List<SavedCredential>>(File.ReadAllText(wcPath));
                    Log($"    wincreds.json: {saved?.Count ?? 0} credential(s)");
                    if (saved != null)
                        foreach (var c in saved) Log($"      - {c.Target} (user: {c.UserName})");
                }
                catch (Exception ex) { Log($"    wincreds.json: parse error - {ex.Message}"); }
            }
            else Log("    wincreds.json: absent");

            // WebView backup
            if (Directory.Exists(EpicWebViewBackupDir))
            {
                int fileCount = Directory.GetFiles(EpicWebViewBackupDir, "*", SearchOption.AllDirectories).Length;
                Log($"    EBWebView backup: {fileCount} file(s)");
            }
            else Log("    EBWebView backup: absent");
        }

        private void LogEpicLiveState()
        {
            Log("  [Live Epic state]");

            // .dat files
            if (Directory.Exists(EpicDataDir))
            {
                var datFiles = Directory.GetFiles(EpicDataDir, "*.dat");
                Log($"    Data\\.dat files: {datFiles.Length}" + (datFiles.Length > 0
                    ? " (" + string.Join(", ", Array.ConvertAll(datFiles, Path.GetFileName)) + ")" : ""));
            }
            else Log("    Data dir: absent");

            // GameUserSettings.ini
            if (File.Exists(EpicConfigFile))
            {
                string ini = File.ReadAllText(EpicConfigFile);
                var rmSection = Regex.Match(ini, @"\[RememberMe\].*?(?=\n\[|\z)", RegexOptions.Singleline);
                if (rmSection.Success)
                {
                    var dataMatch  = Regex.Match(rmSection.Value, @"^Data\s*=(.*)$",   RegexOptions.Multiline);
                    var enableMatch = Regex.Match(rmSection.Value, @"^Enable\s*=(.*)$", RegexOptions.Multiline);
                    string dataVal = dataMatch.Success ? dataMatch.Groups[1].Value.Trim() : "(not found)";
                    Log($"    GameUserSettings [RememberMe] present");
                    Log($"      Data= {Truncate(dataVal)}");
                    if (enableMatch.Success) Log($"      Enable= {enableMatch.Groups[1].Value.Trim()}");
                }
                else Log("    GameUserSettings: [RememberMe] section absent");
            }
            else Log($"    GameUserSettings.ini: absent ({EpicConfigFile})");

            // Windows credentials
            var wincreds = GetEpicWindowsCredentials();
            Log($"    Windows credentials: {wincreds.Count}");
            foreach (var c in wincreds) Log($"      - {c.Target} (user: {c.UserName})");

            // Registry AccountId
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Epic Games\Unreal Engine\Identifiers");
                string? accountId = key?.GetValue("AccountId") as string;
                Log($"    Registry AccountId: {(string.IsNullOrEmpty(accountId) ? "(empty/absent)" : accountId)}");
            }
            catch { Log("    Registry AccountId: (could not read)"); }

            // EBWebView live dir
            if (Directory.Exists(EpicWebViewDir))
            {
                int fileCount = Directory.GetFiles(EpicWebViewDir, "*", SearchOption.AllDirectories).Length;
                Log($"    EBWebView live: {fileCount} file(s)");
            }
            else Log($"    EBWebView live: absent ({EpicWebViewDir})");
        }

        private static string Truncate(string s) =>
            s.Length == 0 ? "(empty)" : s.Length <= 32 ? s : s[..16] + "…" + s[^8..] + $" ({s.Length} chars)";
    }
}
