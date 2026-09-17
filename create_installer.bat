@echo off
setlocal
cd /d "%~dp0"

echo =======================================================
echo   NetToCxSim NSIS Installer Builder
echo   Output: SetupNetToCxSim_v*.exe
echo   Author: ismaillowkey
echo =======================================================
echo.

rem 1. Check if published files exist, if not, publish first
if not exist "publish_x86\NetToCXSim.exe" (
    echo [INFO] publish_x86\NetToCXSim.exe not found.
    echo Building and publishing x86 Release first...
    echo.
    dotnet publish src\NetToCXSim.Wpf\NetToCXSim.Wpf.csproj -c Release -r win-x86 --self-contained false -o ./publish_x86
    if %ERRORLEVEL% NEQ 0 (
        echo.
        echo [ERROR] Dotnet publish failed! Cannot create installer.
        goto :END
    )
    echo.
)

rem 2. Find NSIS makensis.exe
set "MAKENSIS="
if exist "C:\Program Files (x86)\NSIS\makensis.exe" set "MAKENSIS=C:\Program Files (x86)\NSIS\makensis.exe"
if exist "C:\Program Files\NSIS\makensis.exe" set "MAKENSIS=C:\Program Files\NSIS\makensis.exe"

if not defined MAKENSIS (
    where makensis >nul 2>nul
    if %ERRORLEVEL% EQU 0 set "MAKENSIS=makensis"
)

if not defined MAKENSIS (
    echo [ERROR] NSIS makensis.exe was not found!
    echo Please install NSIS from https://nsis.sourceforge.io/
    goto :END
)

echo [INFO] Using NSIS compiler: "%MAKENSIS%"
echo Compiling installer.nsi...
echo.

"%MAKENSIS%" installer.nsi

if %ERRORLEVEL% EQU 0 (
    echo.
    echo =======================================================
    echo  [SUCCESS] Installer Created Successfully!
    for %%F in (SetupNetToCxSim_v*.exe) do (
        echo  File: %%~nxF (%%~zF bytes^)
        echo  Path: %%~dpnxF
    )
    echo =======================================================
) else (
    echo.
    echo =======================================================
    echo  [ERROR] NSIS compilation failed with code %ERRORLEVEL%!
    echo =======================================================
)

:END
echo.
pause
