; ========================================================
;  NSIS Installer Script for NetToCxSim
;  Author: ismaillowkey
;  Version: 0.4.4
; ========================================================

!define PRODUCT_NAME "NetToCxSim"
!define PRODUCT_VERSION "0.4.4"
!define PRODUCT_PUBLISHER "ismaillowkey"
!define PRODUCT_WEB_SITE "https://saweria.co/ismaillowkey"
!define PRODUCT_EXE "NetToCXSim.exe"
!define STARTMENU_FOLDER "Omron NetToCxSim"
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
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${PRODUCT_NAME} v${PRODUCT_VERSION}"
!insertmacro MUI_PAGE_FINISH

; Uninstaller Pages
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

; Language
!insertmacro MUI_LANGUAGE "English"

; --------------------------------------------------------
; Installation Section
; --------------------------------------------------------
Section "MainSection" SEC01
    SetOutPath "$INSTDIR"
    SetOverwrite on

    ; Check if older version exists, silently uninstall it first
    ReadRegStr $R0 ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "UninstallString"
    StrCmp $R0 "" skip_old_uninstall
    IfFileExists "$R0" 0 skip_old_uninstall
        DetailPrint "Removing previous installation..."
        ExecWait '"$R0" /S _?=$INSTDIR'
        Delete "$R0"
        Sleep 500
    skip_old_uninstall:

    ; Clean up legacy files before installing new files
    Delete "$INSTDIR\NetToCXSim.exe"
    Delete "$INSTDIR\NetToCXSim.exe.config"
    Delete "$INSTDIR\NetToCXSim.Core.dll"
    Delete "$INSTDIR\app.ico"
    Delete "$INSTDIR\Saweria Donate.url"
    Delete "$INSTDIR\uninst.exe"

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
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "InstallLocation" "$INSTDIR"

    ; Register OPC DA Server in 32-bit (WOW6432Node) and 64-bit registry
    SetRegView 32
    WriteRegStr HKLM "Software\Classes\NetToCxSim.OPCServer.DA" "" "NetToCxSim Omron CX-Simulator OPC DA Server"
    WriteRegStr HKLM "Software\Classes\NetToCxSim.OPCServer.DA\CLSID" "" "{B5D2D68C-7975-4B86-B909-64DE9B518F8C}"
    WriteRegStr HKLM "Software\Classes\NetToCxSim.OPCServer.DA\OPC" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}" "" "NetToCxSim Omron CX-Simulator OPC DA Server"
    WriteRegStr HKLM "Software\Classes\CLSID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}" "AppID" "{B5D2D68C-7975-4B86-B909-64DE9B518F8C}"
    WriteRegStr HKLM "Software\Classes\CLSID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}\ProgID" "" "NetToCxSim.OPCServer.DA"
    WriteRegStr HKLM "Software\Classes\CLSID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}\LocalServer32" "" '"$INSTDIR\${PRODUCT_EXE}"'
    WriteRegStr HKLM "Software\Classes\CLSID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}\Implemented Categories\{63D5F432-CFE4-11d1-B2C8-0060083BA1FB}" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}\Implemented Categories\{63D5F430-CFE4-11d1-B2C8-0060083BA1FB}" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}\Implemented Categories\{CC54E38A-DB8C-11d2-AB76-00805F77D1E1}" "" ""
    WriteRegStr HKLM "Software\Classes\AppID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}" "" "NetToCxSim Omron CX-Simulator OPC DA Server"
    WriteRegDWORD HKLM "Software\Classes\AppID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}" "AuthenticationLevel" 1
    WriteRegStr HKLM "Software\Classes\AppID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}" "RunAs" "Interactive User"
    WriteRegStr HKLM "Software\Classes\AppID\${PRODUCT_EXE}" "AppID" "{B5D2D68C-7975-4B86-B909-64DE9B518F8C}"

    SetRegView 64
    WriteRegStr HKLM "Software\Classes\NetToCxSim.OPCServer.DA" "" "NetToCxSim Omron CX-Simulator OPC DA Server"
    WriteRegStr HKLM "Software\Classes\NetToCxSim.OPCServer.DA\CLSID" "" "{B5D2D68C-7975-4B86-B909-64DE9B518F8C}"
    WriteRegStr HKLM "Software\Classes\NetToCxSim.OPCServer.DA\OPC" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}" "" "NetToCxSim Omron CX-Simulator OPC DA Server"
    WriteRegStr HKLM "Software\Classes\CLSID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}" "AppID" "{B5D2D68C-7975-4B86-B909-64DE9B518F8C}"
    WriteRegStr HKLM "Software\Classes\CLSID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}\ProgID" "" "NetToCxSim.OPCServer.DA"
    WriteRegStr HKLM "Software\Classes\CLSID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}\LocalServer32" "" '"$INSTDIR\${PRODUCT_EXE}"'
    WriteRegStr HKLM "Software\Classes\CLSID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}\Implemented Categories\{63D5F432-CFE4-11d1-B2C8-0060083BA1FB}" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}\Implemented Categories\{63D5F430-CFE4-11d1-B2C8-0060083BA1FB}" "" ""
    WriteRegStr HKLM "Software\Classes\CLSID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}\Implemented Categories\{CC54E38A-DB8C-11d2-AB76-00805F77D1E1}" "" ""
    WriteRegStr HKLM "Software\Classes\AppID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}" "" "NetToCxSim Omron CX-Simulator OPC DA Server"
    WriteRegDWORD HKLM "Software\Classes\AppID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}" "AuthenticationLevel" 1
    WriteRegStr HKLM "Software\Classes\AppID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}" "RunAs" "Interactive User"
    WriteRegStr HKLM "Software\Classes\AppID\${PRODUCT_EXE}" "AppID" "{B5D2D68C-7975-4B86-B909-64DE9B518F8C}"
    SetRegView 32
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

    ; Remove OPC DA Server Registry Keys
    SetRegView 32
    DeleteRegKey HKLM "Software\Classes\NetToCxSim.OPCServer.DA"
    DeleteRegKey HKLM "Software\Classes\CLSID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}"
    DeleteRegKey HKLM "Software\Classes\AppID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}"
    DeleteRegKey HKLM "Software\Classes\AppID\${PRODUCT_EXE}"
    SetRegView 64
    DeleteRegKey HKLM "Software\Classes\NetToCxSim.OPCServer.DA"
    DeleteRegKey HKLM "Software\Classes\CLSID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}"
    DeleteRegKey HKLM "Software\Classes\AppID\{B5D2D68C-7975-4B86-B909-64DE9B518F8C}"
    DeleteRegKey HKLM "Software\Classes\AppID\${PRODUCT_EXE}"
    SetRegView 32

    SetAutoClose true
SectionEnd
