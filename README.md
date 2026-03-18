# Outlogger

A lightweight Windows tray utility for quickly switching game launcher accounts on shared PCs.

## Features

- **One-click logout** — signs you out of Steam and Epic Games Launcher at once
- **Saved default accounts** — backs up credentials so you can restore them instantly
- **One-click login** — restores saved credentials and launches the game client, ready to go
- **Auto-restore on shutdown** — optionally logs out current users and restores defaults when Windows shuts down
- **Start at login** — optionally launches automatically when you sign in to Windows
- **Minimizes to tray** — stays out of your way when not in use

## How it works

1. **Log in to your launchers** — sign into the accounts you want to save as defaults in Steam and/or Epic Games
2. **Click "Save Current"** — saves your credentials as the default accounts to restore to
3. **Click "Log Out"** — signs you out of the launchers
4. **Click "Login" to restore** — one click restores your saved credentials and launches the client, signed in and ready

## Requirements

- Windows 10 or later
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- Steam and/or Epic Games Launcher installed

## Building from source

```
dotnet build
dotnet run
```

Requires .NET 10 SDK.
