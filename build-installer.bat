@echo off
echo Building Outlogger...

dotnet publish LauncherLogout.csproj /p:PublishProfile=win-x64 -c Release
if errorlevel 1 (
    echo Publish failed.
    pause
    exit /b 1
)

echo.
echo Building installer...

set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not exist %ISCC% set ISCC="C:\Program Files\Inno Setup 6\ISCC.exe"

if not exist %ISCC% (
    echo Inno Setup not found. Download from https://jrsoftware.org/isinfo.php
    pause
    exit /b 1
)

%ISCC% installer.iss
if errorlevel 1 (
    echo Installer build failed.
    pause
    exit /b 1
)

echo.
echo Done. Installer is in installer-output\
pause
