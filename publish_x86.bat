@echo off
setlocal
cd /d "%~dp0"

echo =======================================================
echo   Publishing NetToCxSim v0.3.0 [x86 Release]
echo   Author: ismaillowkey
echo =======================================================
echo.

if exist "publish_x86" (
    echo Cleaning previous publish_x86 directory...
    rd /s /q "publish_x86"
)

echo Building and publishing x86 Release...
dotnet publish src\NetToCXSim.Wpf\NetToCXSim.Wpf.csproj -c Release -r win-x86 --self-contained false -o ./publish_x86

if %ERRORLEVEL% EQU 0 (
    echo.
    echo =======================================================
    echo  [SUCCESS] Published successfully!
    echo  Folder: %~dp0publish_x86
    echo  Executable: NetToCXSim.exe [32-bit x86 Release]
    echo =======================================================
    echo.

    rem Check and build NSIS installer
    if exist "C:\Program Files (x86)\NSIS\makensis.exe" (
        echo Building NSIS Setup Installer...
        "C:\Program Files (x86)\NSIS\makensis.exe" installer.nsi
    ) else if exist "C:\Program Files\NSIS\makensis.exe" (
        echo Building NSIS Setup Installer...
        "C:\Program Files\NSIS\makensis.exe" installer.nsi
    ) else (
        echo [INFO] NSIS not found. Skipped installer packaging.
    )
) else (
    echo.
    echo =======================================================
    echo  [ERROR] Build/Publish failed with exit code %ERRORLEVEL%!
    echo =======================================================
)

echo.
pause
