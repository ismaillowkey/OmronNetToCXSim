; ========================================================
;  NSIS Installer Script for NetToCxSim
;  Author: ismaillowkey
;  Version: 0.3.1
; ========================================================

!define PRODUCT_NAME "NetToCxSim"
!define PRODUCT_VERSION "0.3.1"
!define PRODUCT_PUBLISHER "ismaillowkey"
!define PRODUCT_WEB_SITE "https://saweria.co/ismaillowkey"
!define PRODUCT_EXE "NetToCXSim.exe"
!define STARTMENU_FOLDER "Omron NetToPlcSim"
!define PRODUCT_DIR_REGKEY "Software\Microsoft\Windows\CurrentVersion\App Paths\NetToCXSim.exe"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
!define PRODUCT_UNINST_ROOT_KEY "HKLM"

; Modern UI
!include "MUI2.nsh"

; General Settings
Name "${PRODUCT_NAME} v${PRODUCT_VERSION}"
OutFile "SetupNetToCxSim_v${PRODUCT_VERSION}.exe"
InstallDir "$PROGRAMFILES\NetToCxSim"
InstallDirRegKey HKLM "${PRODUCT_DIR_REGKEY}" ""
RequestExecutionLevel admin
SetCompressor /SOLID lzma

; Interface Configuration
!define MUI_ICON "app.ico"
!define MUI_UNICON "app.ico"
!define MUI_ABORTWARNING

; Installer Pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\${PRODUCT_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Jalankan ${PRODUCT_NAME} v${PRODUCT_VERSION}"
!insertmacro MUI_PAGE_FINISH

; Uninstaller Pages
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

; Language
!insertmacro MUI_LANGUAGE "Indonesian"
!insertmacro MUI_LANGUAGE "English"

; --------------------------------------------------------
; Installation Section
; --------------------------------------------------------
Section "MainSection" SEC01
    SetOutPath "$INSTDIR"
    SetOverwrite ifnewer

    ; Copy published files
    File "publish_x86\NetToCXSim.exe"
    File "publish_x86\NetToCXSim.exe.config"
    File "publish_x86\NetToCXSim.Core.dll"
    File "app.ico"

    ; Create Desktop Shortcut
    CreateShortCut "$DESKTOP\${PRODUCT_NAME}.lnk" "$INSTDIR\${PRODUCT_EXE}" "" "$INSTDIR\app.ico" 0

    ; Create Start Menu Shortcuts
    CreateDirectory "$SMPROGRAMS\${STARTMENU_FOLDER}"
    CreateShortCut "$SMPROGRAMS\${STARTMENU_FOLDER}\${PRODUCT_NAME}.lnk" "$INSTDIR\${PRODUCT_EXE}" "" "$INSTDIR\app.ico" 0
    CreateShortCut "$SMPROGRAMS\${STARTMENU_FOLDER}\Uninstall ${PRODUCT_NAME}.lnk" "$INSTDIR\uninst.exe" "" "$INSTDIR\uninst.exe" 0
SectionEnd

Section -AdditionalIcons
    WriteIniStr "$INSTDIR\Saweria Donate.url" "InternetShortcut" "URL" "${PRODUCT_WEB_SITE}"
    CreateShortCut "$SMPROGRAMS\${STARTMENU_FOLDER}\Support & Donate (Saweria).lnk" "$INSTDIR\Saweria Donate.url" "" "$INSTDIR\app.ico" 0
SectionEnd

Section -Post
    ; Create Uninstaller
    WriteUninstaller "$INSTDIR\uninst.exe"

    ; Registry Keys for App Paths
    WriteRegStr HKLM "${PRODUCT_DIR_REGKEY}" "" "$INSTDIR\${PRODUCT_EXE}"

    ; Registry Keys for Windows Add/Remove Programs
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayName" "$(^Name)"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "UninstallString" "$INSTDIR\uninst.exe"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\app.ico"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "URLInfoAbout" "${PRODUCT_WEB_SITE}"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
SectionEnd

; --------------------------------------------------------
; Uninstallation Section
; --------------------------------------------------------
Section Uninstall
    ; Remove shortcuts
    Delete "$DESKTOP\${PRODUCT_NAME}.lnk"
    Delete "$SMPROGRAMS\${STARTMENU_FOLDER}\${PRODUCT_NAME}.lnk"
    Delete "$SMPROGRAMS\${STARTMENU_FOLDER}\Uninstall ${PRODUCT_NAME}.lnk"
    Delete "$SMPROGRAMS\${STARTMENU_FOLDER}\Support & Donate (Saweria).lnk"
    RMDir "$SMPROGRAMS\${STARTMENU_FOLDER}"

    ; Remove installed files
    Delete "$INSTDIR\NetToCXSim.exe"
    Delete "$INSTDIR\NetToCXSim.exe.config"
    Delete "$INSTDIR\NetToCXSim.Core.dll"
    Delete "$INSTDIR\app.ico"
    Delete "$INSTDIR\Saweria Donate.url"
    Delete "$INSTDIR\uninst.exe"

    RMDir "$INSTDIR"

    ; Remove registry keys
    DeleteRegKey ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}"
    DeleteRegKey HKLM "${PRODUCT_DIR_REGKEY}"

    SetAutoClose true
SectionEnd
