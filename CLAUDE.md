# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
dotnet build LauncherLogout.csproj
dotnet run --project LauncherLogout.csproj   # requires display; use start "" bin\Debug\net10.0-windows\LauncherLogout.exe instead
start "" "bin\Debug\net10.0-windows\LauncherLogout.exe"
```

The app targets `net10.0-windows` and requires both `UseWPF` and `UseWindowsForms` (for the tray icon).

## Architecture

**Single-window WPF app** — no MVVM, no data binding framework. UI state is updated directly in code-behind via named element references.

### Key files

- `MainWindow.xaml` / `MainWindow.xaml.cs` — entire app UI and logic. Contains Steam and Epic logout/login/backup/restore flows, credentials display, tray icon, and settings toggles.
- `ShutdownHandler.cs` — static class with synchronous versions of logout+restore, called from `App.xaml.cs` on Windows session ending (`OnSessionEnding`). Duplicates some path/registry logic from `MainWindow` intentionally (no shared dependency at shutdown time).
- `WelcomeDialog.xaml/.cs` — first-run onboarding dialog, shown once then gated by `%APPDATA%\Outlogger\initialized.flag`.

### Credential backup strategy

Both launchers use the same pattern: **backup on first logout, restore on login**.

**Steam** — backs up to `%APPDATA%\Outlogger\SteamBackup\`:
- `ssfn*` session token files from the Steam install directory
- `loginusers.vdf` from `Steam\config\`
- `username.txt` (value of `HKCU\SOFTWARE\Valve\Steam\AutoLoginUser`)
- Backup must happen **before** shutting Steam down — Steam clears `AutoLoginUser` from the registry on graceful shutdown.

**Epic** — backs up to `%APPDATA%\Outlogger\EpicBackup\`:
- `*.dat` files from `%LOCALAPPDATA%\EpicGamesLauncher\Saved\Data\`
- `[RememberMe]` and `[Offline]` sections extracted from `GameUserSettings.ini` as `rememberme.txt` / `offline.txt`
- The `[RememberMe] Data=` field in `GameUserSettings.ini` is the primary auth token that keeps the user logged in.

### Settings persistence

| Setting | Storage |
|---|---|
| Auto-restore on shutdown | `%APPDATA%\Outlogger\auto_logout.flag` (presence = enabled) |
| Start at login | `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run\Outlogger` |
| First run shown | `%APPDATA%\Outlogger\initialized.flag` |

### Tray icon

`_reallyClose` bool gates `Window_Closing` — closing the window hides it; right-click tray → Exit sets `_reallyClose = true` before calling `Close()`.
