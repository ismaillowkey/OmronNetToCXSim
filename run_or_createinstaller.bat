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
echo [INFO] Building and publishing x86 Release engine...
echo.
dotnet publish src\NetToCXSim.Wpf\NetToCXSim.Wpf.csproj -c Release -r win-x86 --self-contained false -o ./src/NetToCXSim.Wpf/obj/publish_x86
if %ERRORLEVEL% NEQ 0 goto :PUBLISH_FAILED

if not exist "src\NetToCXSim.Wpf\obj\publish_x86\NetToCXSim.exe" goto :MISSING_EXE
echo.
echo [INFO] Build ready.
echo.

set "MAKENSIS="
if exist "C:\Program Files (x86)\NSIS\makensis.exe" set "MAKENSIS=C:\Program Files (x86)\NSIS\makensis.exe"
if exist "C:\Program Files\NSIS\makensis.exe" set "MAKENSIS=C:\Program Files\NSIS\makensis.exe"

if defined MAKENSIS goto :FOUND_NSIS

where makensis >nul 2>nul
if %ERRORLEVEL% EQU 0 set "MAKENSIS=makensis"

:FOUND_NSIS
if not defined MAKENSIS goto :MISSING_NSIS

if not exist "publish" mkdir "publish"

echo [INFO] Using NSIS compiler: "%MAKENSIS%"
echo [INFO] Compiling installer.nsi for %TARGET_ARCH%...
echo.

"%MAKENSIS%" /DTARGET_ARCH=%TARGET_ARCH% installer.nsi
if %ERRORLEVEL% NEQ 0 goto :NSIS_FAILED

if exist "src\NetToCXSim.Wpf\obj\publish_x86" rmdir /s /q "src\NetToCXSim.Wpf\obj\publish_x86"

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
echo  [ERROR] NetToCXSim.exe was not found after publish!
echo =======================================================
goto :END_ERR

:MISSING_NSIS
if exist "src\NetToCXSim.Wpf\obj\publish_x86" rmdir /s /q "src\NetToCXSim.Wpf\obj\publish_x86"
echo.
echo =======================================================
echo  [ERROR] NSIS makensis.exe was not found!
echo  Please install NSIS from https://nsis.sourceforge.io/
echo =======================================================
goto :END_ERR

:NSIS_FAILED
if exist "src\NetToCXSim.Wpf\obj\publish_x86" rmdir /s /q "src\NetToCXSim.Wpf\obj\publish_x86"
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
