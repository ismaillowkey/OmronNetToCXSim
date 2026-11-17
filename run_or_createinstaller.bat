@echo off
setlocal
cd /d "%~dp0"

:MENU
cls
echo =======================================================
echo   NetToCxSim Launcher and Installer Builder
echo   Author: Ismail Lowkey
echo =======================================================
echo.
echo   [1] Run NetToCxSim (x86)
echo   [2] Create Windows Installer (x86)
echo   [3] Create Windows Installer (x64)
echo   [0] Exit
echo.
set "CHOICE="
set /p "CHOICE=Select option [1/2/3/0]: "

if "%CHOICE%"=="1" goto :RUN_X86
if "%CHOICE%"=="2" goto :INSTALLER_X86
if "%CHOICE%"=="3" goto :INSTALLER_X64
if "%CHOICE%"=="0" goto :EXIT_CLEAN

echo.
echo [ERROR] Invalid selection "%CHOICE%". Please enter 1, 2, 3, or 0.
echo.
pause
goto :MENU

:RUN_X86
echo.
echo =======================================================
echo   Running NetToCxSim (x86)...
echo =======================================================
echo.
dotnet run --project src\NetToCXSim.Wpf\NetToCXSim.Wpf.csproj
if %ERRORLEVEL% NEQ 0 goto :RUN_FAILED
echo.
echo [INFO] Application launched / exited cleanly.
goto :END_OK

:INSTALLER_X86
set "TARGET_ARCH=x86"
goto :BUILD_INSTALLER

:INSTALLER_X64
set "TARGET_ARCH=x64"
goto :BUILD_INSTALLER

:BUILD_INSTALLER
echo.
echo =======================================================
echo   NetToCxSim NSIS Installer Builder (%TARGET_ARCH%)
echo   Output: publish\SetupNetToCxSim_v*_%TARGET_ARCH%.exe
echo =======================================================
echo.
set "STAGING_DIR=src\NetToCXSim.Wpf\obj\publish_staging"
if exist "%STAGING_DIR%" rmdir /s /q "%STAGING_DIR%"
if not exist "publish" mkdir "publish"

echo [1/3] Building and publishing Release engine to staging folder...
echo.
dotnet publish src\NetToCXSim.Wpf\NetToCXSim.Wpf.csproj -c Release -r win-x86 --self-contained false -o "%STAGING_DIR%"
if %ERRORLEVEL% NEQ 0 goto :PUBLISH_FAILED

if not exist "%STAGING_DIR%\NetToCXSim.exe" goto :MISSING_EXE
echo.
echo [INFO] Staging build ready.
echo.

set "MAKENSIS="
if exist "C:\Program Files (x86)\NSIS\makensis.exe" set "MAKENSIS=C:\Program Files (x86)\NSIS\makensis.exe"
if exist "C:\Program Files\NSIS\makensis.exe" set "MAKENSIS=C:\Program Files\NSIS\makensis.exe"

if defined MAKENSIS goto :FOUND_NSIS

where makensis >nul 2>nul
if %ERRORLEVEL% EQU 0 set "MAKENSIS=makensis"

:FOUND_NSIS
if not defined MAKENSIS goto :MISSING_NSIS

echo [2/3] Using NSIS compiler: "%MAKENSIS%"
echo [3/3] Compiling installer.nsi for %TARGET_ARCH%...
echo.

"%MAKENSIS%" /DTARGET_ARCH=%TARGET_ARCH% /DSTAGING_DIR="%STAGING_DIR%" installer.nsi
if %ERRORLEVEL% NEQ 0 goto :NSIS_FAILED

:: Clean up staging directory after successful NSIS compile
if exist "%STAGING_DIR%" rmdir /s /q "%STAGING_DIR%"

echo.
echo =======================================================
echo  [SUCCESS] Installer (%TARGET_ARCH%) Created Successfully!
for %%F in (publish\SetupNetToCxSim_v*_%TARGET_ARCH%.exe) do (
    echo  File: %%~nxF
    echo  Size: %%~zF bytes
    echo  Path: %%~dpnxF
)
echo =======================================================
goto :END_OK

:RUN_FAILED
echo.
echo =======================================================
echo  [ERROR] Failed to run NetToCxSim (exit code %ERRORLEVEL%).
echo =======================================================
goto :END_ERR

:PUBLISH_FAILED
echo.
echo =======================================================
echo  [ERROR] Dotnet publish failed! Cannot create installer.
echo =======================================================
goto :END_ERR

:MISSING_EXE
echo.
echo =======================================================
echo  [ERROR] NetToCXSim.exe was not found in publish folder!
echo =======================================================
goto :END_ERR

:MISSING_NSIS
echo.
echo =======================================================
echo  [ERROR] NSIS makensis.exe was not found!
echo  Please install NSIS from https://nsis.sourceforge.io/
echo =======================================================
goto :END_ERR

:NSIS_FAILED
echo.
echo =======================================================
echo  [ERROR] NSIS compilation failed with code %ERRORLEVEL%!
echo =======================================================
goto :END_ERR

:END_OK
echo.
pause
exit /b 0

:END_ERR
echo.
pause
exit /b 1

:EXIT_CLEAN
exit /b 0
