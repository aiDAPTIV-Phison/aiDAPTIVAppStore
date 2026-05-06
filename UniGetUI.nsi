; NSIS Installer Script
; Package files from nsi directory and execute installation tasks

;--------------------------------
; Include Modern UI and Branding
!include "MUI2.nsh"
!include "LogicLib.nsh"

; Include auto-generated branding definitions
!include "InstallerExtras\AppBranding.nsh"

;--------------------------------
; General Settings

; Name of installer
Name "${APP_NAME} Installer"

; Output file name
OutFile "${APP_EXE_BASENAME}_installer.exe"

; Default installation directory (AppData\Local)
InstallDir "$LOCALAPPDATA\${APP_DATA_FOLDER}"

; Get installation directory from registry
InstallDirRegKey HKCU "Software\${APP_DATA_FOLDER}" ""

; Request execution level (may need for PowerShell commands)
RequestExecutionLevel user

;--------------------------------
; Interface Settings

!define MUI_ABORTWARNING

; Installer pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

; Uninstaller pages
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

;--------------------------------
; Language
; Add multiple language support for auto-detection
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "TradChinese"
!insertmacro MUI_LANGUAGE "English"

; Auto-detect system language
!insertmacro MUI_RESERVEFILE_LANGDLL

;--------------------------------
; Auto-detect system language function
Function .onInit
    ; Auto-detect system language
    ; Read from HKLM (system-wide setting) - this is more reliable
    ReadRegStr $R0 HKLM "SYSTEM\CurrentControlSet\Control\Nls\Language" "Default"
    
    ; If empty, try HKCU as fallback
    ${If} $R0 == ""
        ReadRegStr $R0 HKCU "Control Panel\International" "Locale"
        ; Extract first 4 characters if it's a long string like "00000404"
        StrLen $R1 $R0
        ${If} $R1 > 4
            StrCpy $R0 $R0 4 0
        ${EndIf}
    ${EndIf}
    
    ; Detect language based on locale code
    ; 0404 = Traditional Chinese (Taiwan) - LCID 1028
    ; 0804 = Simplified Chinese (China) - LCID 2052
    ; 0C04 = Traditional Chinese (Hong Kong) - LCID 3076
    ; 1004 = Traditional Chinese (Singapore) - LCID 4100
    ; Default to English
    
    ${If} $R0 == "0404"
        ; Traditional Chinese (Taiwan)
        StrCpy $LANGUAGE 1
    ${ElseIf} $R0 == "0804"
        ; Simplified Chinese (China)
        StrCpy $LANGUAGE 0
    ${ElseIf} $R0 == "0C04"
        ; Traditional Chinese (Hong Kong)
        StrCpy $LANGUAGE 1
    ${ElseIf} $R0 == "1004"
        ; Traditional Chinese (Singapore)
        StrCpy $LANGUAGE 1
    ${Else}
        ; Default to English
        StrCpy $LANGUAGE 2
    ${EndIf}
FunctionEnd

;--------------------------------
; Installer Section

Section "MainSection" SEC01

    ; Set output directory to AppData\Local\${APP_DATA_FOLDER}
    SetOutPath "$LOCALAPPDATA\${APP_DATA_FOLDER}"
    
    ; Copy all files and folders from current directory to installation directory
    ; Note: installer.nsi will not be included as it's the script file itself
    ; Use "*" pattern to match all files including those without extensions
    File /r "*"
    
    ; Save installation path to registry
    WriteRegStr HKCU "Software\${APP_DATA_FOLDER}" "" $INSTDIR
    
    ; Create uninstaller
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    
    ; Write registry entries for Add/Remove Programs
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_DATA_FOLDER}" \
                 "DisplayName" "${APP_NAME}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_DATA_FOLDER}" \
                 "UninstallString" "$INSTDIR\Uninstall.exe"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_DATA_FOLDER}" \
                 "InstallLocation" "$INSTDIR"
    
    ; Task 1: Install Scoop
    DetailPrint "Checking if Scoop is already installed..."
    ; Check if Scoop is already installed (hidden window)
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "if (Test-Path \"$env:USERPROFILE\scoop\shims\scoop.cmd\") { exit 0 } elseif (Test-Path \"$env:USERPROFILE\scoop\apps\scoop\current\bin\scoop.ps1\") { exit 0 } else { exit 1 }"`
    Pop $0  ; Exit code
    Pop $1  ; Output (not used)
    ${If} $0 != 0
        DetailPrint "Installing Scoop..."
        ; Execute PowerShell command to install Scoop (hidden window)
        nsExec::Exec `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser -Force; Invoke-RestMethod -Uri https://get.scoop.sh | Invoke-Expression"`
    ${Else}
        DetailPrint "Scoop is already installed, skipping installation."
    ${EndIf}
    
    ; Add Scoop bucket: versions
    DetailPrint "Checking if versions bucket is already added..."
    ; Check if versions bucket is already added (hidden window)
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "$$buckets = scoop bucket list; if ($$buckets -match 'versions') { exit 0 } else { exit 1 }"`
    Pop $0  ; Exit code
    Pop $1  ; Output (not used)
    ${If} $0 != 0
        DetailPrint "Adding Scoop bucket: versions..."
        nsExec::Exec `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "scoop bucket add versions"`
    ${Else}
        DetailPrint "versions bucket is already added, skipping."
    ${EndIf}
    
    ; Add Scoop bucket: aiDAPTIV-bucket
    DetailPrint "Checking if aiDAPTIV-bucket is already added..."
    ; Check if aiDAPTIV-bucket is already added (hidden window)
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "$$buckets = scoop bucket list; if ($$buckets -match 'aiDAPTIV-bucket') { exit 0 } else { exit 1 }"`
    Pop $0  ; Exit code
    Pop $1  ; Output (not used)
    ${If} $0 != 0
        DetailPrint "Adding Scoop bucket: aiDAPTIV-bucket..."
        nsExec::Exec `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "scoop bucket add aiDAPTIV-bucket https://github.com/aiDAPTIV-Phison/aiDAPTIV-bucket"`
    ${Else}
        DetailPrint "aiDAPTIV-bucket is already added, skipping."
    ${EndIf}
    
    ; Check and install scoop-search
    DetailPrint "Checking if scoop-search is already installed..."
    ; Check if scoop-search is already installed (hidden window)
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "if (Test-Path \"$env:USERPROFILE\scoop\apps\scoop-search\current\scoop-search.ps1\") { exit 0 } elseif (Get-Command scoop-search -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }"`
    Pop $0  ; Exit code
    Pop $1  ; Output (not used)
    ${If} $0 != 0
        DetailPrint "Installing scoop-search..."
        nsExec::Exec `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "scoop install scoop-search"`
    ${Else}
        DetailPrint "scoop-search is already installed, skipping installation."
    ${EndIf}
    
    ; Task 2: Files already copied to AppData path via File /r command
    DetailPrint "Files copied to: $INSTDIR"
    
    ; Task 3: Create desktop shortcut
    CreateShortCut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE_NAME}" "" "$INSTDIR\${APP_EXE_NAME}" 0
    
    ; Check and create C:/models directory if it doesn't exist
    DetailPrint "Checking C:/models directory..."
    IfFileExists "C:\models" +3 0
    DetailPrint "Creating C:/models directory..."
    CreateDirectory "C:\models"
    
    ; Task 4:
    
    DetailPrint "Installation completed!"
    
SectionEnd

;--------------------------------
; Uninstaller Section

Section "Uninstall"

    ; Delete all files in installation directory
    RMDir /r "$INSTDIR"
    
    ; Delete desktop shortcut
    Delete "$DESKTOP\${APP_NAME}.lnk"
    
    ; Delete registry keys
    DeleteRegKey HKCU "Software\${APP_DATA_FOLDER}"
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_DATA_FOLDER}"
    
    DetailPrint "Uninstallation completed!"

SectionEnd
