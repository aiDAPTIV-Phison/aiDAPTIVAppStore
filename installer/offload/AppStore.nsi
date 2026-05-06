; NSIS Installer Script for aiDAPTIVAppStore (Offload/Silent Installation)
; Supports:
;   - Interactive installation (UI)
;   - Silent installation with /S and optional /KV_CACHE_DIR=<path>
;     If /KV_CACHE_DIR is omitted, PHISON_AIDAPTIV env var is set to "NULL".
; Example (PowerShell):
;   Start-Process -FilePath ".\aiDAPTIVAppStoreInstaller.exe" -ArgumentList '/S /KV_CACHE_DIR="D:\KVCache"' -Wait
;   Start-Process -FilePath ".\aiDAPTIVAppStoreInstaller.exe" -ArgumentList '/S' -Wait   # KV_CACHE_DIR omitted -> PHISON_AIDAPTIV="NULL"
; Tasks:
;   1. Extract scoop.zip to %USERPROFILE%\scoop
;   2. Add %USERPROFILE%\scoop\shims to user PATH
;   3. Copy installer contents to $LOCALAPPDATA\${APP_DATA_FOLDER}

;--------------------------------
; Include Required Headers
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "x64.nsh"
!include "nsDialogs.nsh"
!include "WinMessages.nsh"

;--------------------------------
; Application Settings (Customize these values)
!define APP_NAME "aiDAPTIVAppStore"
!define APP_DATA_FOLDER "aiDAPTIVAppStore"
!define APP_VERSION "1.0.0"
!define APP_EXE_NAME "aiDAPTIVAppStore.exe"
!define APP_PROCESS_NAME "aiDAPTIVAppStore"

;--------------------------------
; General Settings

Name "${APP_NAME}"
OutFile "aiDAPTIVAppStoreInstaller.exe"
InstallDir "$LOCALAPPDATA\${APP_DATA_FOLDER}"
InstallDirRegKey HKCU "Software\${APP_DATA_FOLDER}" ""
RequestExecutionLevel admin
SetCompressor /SOLID lzma
; SilentInstall silent  ; Uncomment this line for silent installation

;--------------------------------
; Variables
Var SCOOP_ZIP_PATH
Var SCOOP_DEST_PATH
Var SHIMS_PATH
Var KV_CACHE_DIR
Var KV_CACHE_DIR_HWND
Var CMDLINE_ARGS

;--------------------------------
; Interface Settings
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_NOAUTOCLOSE

; Installer pages
!insertmacro MUI_PAGE_WELCOME
Page custom KVCacheDirPage KVCacheDirPageLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

; Uninstaller pages
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

;--------------------------------
; Language
!insertmacro MUI_LANGUAGE "English"

;--------------------------------
; Installer Initialization
Function .onInit
    ; Get the directory where installer is located
    System::Call 'kernel32::GetModuleFileName(i 0, t .R0, i 1024)'
    ${GetParent} "$R0" $R1
    
    ; Set paths
    StrCpy $SCOOP_ZIP_PATH "$R1\scoop.zip"
    StrCpy $SCOOP_DEST_PATH "$PROFILE\scoop"
    StrCpy $SHIMS_PATH "$PROFILE\scoop\shims"
    
    ; Read optional command-line parameters:
    ;   /KV_CACHE_DIR=<path>
    ; Used by silent install, and also pre-fills UI install if provided.
    ${GetParameters} $CMDLINE_ARGS
    ${GetOptions} "$CMDLINE_ARGS" "/KV_CACHE_DIR=" $KV_CACHE_DIR
    ${If} ${Errors}
        StrCpy $KV_CACHE_DIR ""
    ${EndIf}
FunctionEnd

;--------------------------------
; KV Cache Directory Selection Page

Function KVCacheDirPage
    !insertmacro MUI_HEADER_TEXT "KV Cache Directory" "Specify the directory for KV Cache storage"
    
    nsDialogs::Create 1018
    Pop $0
    
    ${NSD_CreateLabel} 0 10u 100% 30u "Please specify the directory path for KV Cache storage:"
    Pop $0
    
    ${NSD_CreateLabel} 0 50u 100% 20u "KV Cache Directory: (Optional, leave empty to skip)"
    Pop $0
    
    ${NSD_CreateDirRequest} 0 75u 70% 12u ""
    Pop $KV_CACHE_DIR_HWND
    
    ${NSD_CreateBrowseButton} 75% 75u 25% 12u "Browse..."
    Pop $0
    ${NSD_OnClick} $0 KVCacheDirBrowse
    
    ; Preserve command-line value if provided, otherwise this remains empty.
    ${NSD_SetText} $KV_CACHE_DIR_HWND "$KV_CACHE_DIR"
    
    nsDialogs::Show
FunctionEnd

Function KVCacheDirBrowse
    ${NSD_GetText} $KV_CACHE_DIR_HWND $KV_CACHE_DIR
    nsDialogs::SelectFolderDialog "" "$KV_CACHE_DIR"
    Pop $0
    ${If} $0 != error
        ${NSD_SetText} $KV_CACHE_DIR_HWND "$0"
        StrCpy $KV_CACHE_DIR "$0"
    ${EndIf}
FunctionEnd

Function KVCacheDirPageLeave
    ${NSD_GetText} $KV_CACHE_DIR_HWND $KV_CACHE_DIR
    
    ; KV_CACHE_DIR is optional: if empty, set to "NULL" and skip validation
    ${If} $KV_CACHE_DIR == ""
        StrCpy $KV_CACHE_DIR "NULL"
        DetailPrint "KV Cache directory not specified, will set PHISON_AIDAPTIV to NULL."
        Return
    ${EndIf}
    
    ; Validate path format, existence, and that it is a directory
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "try { $$path = '$KV_CACHE_DIR'; $$normalized = [System.IO.Path]::GetFullPath($$path); if (-not (Test-Path $$path)) { Write-Host 'Path does not exist'; exit 3 } elseif (-not (Test-Path $$path -PathType Container)) { Write-Host 'Path exists but is not a directory'; exit 2 } else { exit 0 } } catch { Write-Host $$_.Exception.Message; exit 1 }"`
    Pop $0
    Pop $1
    ${If} $0 == 3
        MessageBox MB_OK|MB_ICONEXCLAMATION "The specified path does not exist on this computer.$\r$\n$\r$\nPath: $KV_CACHE_DIR$\r$\nPlease specify an existing directory."
        Abort
    ${ElseIf} $0 == 2
        MessageBox MB_OK|MB_ICONEXCLAMATION "The specified path exists but is not a directory.$\r$\n$\r$\nPath: $KV_CACHE_DIR$\r$\nPlease specify a different path."
        Abort
    ${ElseIf} $0 != 0
        MessageBox MB_OK|MB_ICONEXCLAMATION "Invalid path format.$\r$\n$\r$\nPath: $KV_CACHE_DIR$\r$\nError: $1"
        Abort
    ${EndIf}
    
    DetailPrint "KV Cache directory validated: $KV_CACHE_DIR"
FunctionEnd

;--------------------------------
; Installer Section
Section "Install" SEC01

    ; ========================================
    ; Pre-check: Validate KV_CACHE_DIR before any installation work
    ; ========================================
    ; For silent install, the custom page is skipped so KVCacheDirPageLeave never runs.
    ; Handle empty value and path validation here for that case.
    ${If} $KV_CACHE_DIR == ""
        StrCpy $KV_CACHE_DIR "NULL"
        DetailPrint "KV Cache directory not specified, PHISON_AIDAPTIV will be set to NULL."
    ${ElseIf} $KV_CACHE_DIR != "NULL"
        DetailPrint "Validating KV Cache directory: $KV_CACHE_DIR..."
        nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "try { $$path = '$KV_CACHE_DIR'; $$normalized = [System.IO.Path]::GetFullPath($$path); if (-not (Test-Path $$path)) { Write-Host 'Path does not exist'; exit 3 } elseif (-not (Test-Path $$path -PathType Container)) { Write-Host 'Path exists but is not a directory'; exit 2 } else { exit 0 } } catch { Write-Host $$_.Exception.Message; exit 1 }"`
        Pop $0
        Pop $1
        ${If} $0 == 3
            DetailPrint "Error: The specified KV Cache path does not exist: $KV_CACHE_DIR"
            Abort "The specified KV Cache path does not exist on this computer: $KV_CACHE_DIR"
        ${ElseIf} $0 == 2
            DetailPrint "Error: The specified KV Cache path exists but is not a directory: $KV_CACHE_DIR"
            Abort "The specified KV Cache path exists but is not a directory: $KV_CACHE_DIR"
        ${ElseIf} $0 != 0
            DetailPrint "Error: Invalid KV Cache path format: $KV_CACHE_DIR ($1)"
            Abort "Invalid KV Cache path: $KV_CACHE_DIR. Error: $1"
        ${EndIf}
        DetailPrint "KV Cache directory validated: $KV_CACHE_DIR"
    ${EndIf}

    ; ========================================
    ; Pre-check: Install Visual C++ Redistributable (if not already installed)
    ; ========================================
    DetailPrint "Checking for Visual C++ Redistributable (x64)..."
    ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64" "Installed"
    ${If} $0 == 1
        DetailPrint "Visual C++ Redistributable (x64) is already installed, skipping."
    ${Else}
        DetailPrint "Visual C++ Redistributable (x64) not found, installing..."
        System::Call 'kernel32::GetModuleFileName(i 0, t .R0, i 1024)'
        ${GetParent} "$R0" $R3
        IfFileExists "$R3\VC_redist.x64.exe" 0 vcredist_not_found
            DetailPrint "Running VC_redist.x64.exe /install /quiet /norestart..."
            ExecWait '"$R3\VC_redist.x64.exe" /install /quiet /norestart' $0
            ${If} $0 == 0
                DetailPrint "Visual C++ Redistributable installed successfully."
            ${ElseIf} $0 == 3010
                DetailPrint "Visual C++ Redistributable installed successfully (restart may be required later)."
            ${Else}
                DetailPrint "Warning: VC_redist.x64.exe exited with code $0."
                MessageBox MB_OK|MB_ICONEXCLAMATION "Visual C++ Redistributable installation failed (exit code: $0).$\r$\n$\r$\nYou may need to install it manually."
            ${EndIf}
            Goto vcredist_done
        vcredist_not_found:
            DetailPrint "Warning: VC_redist.x64.exe not found in installer directory, skipping."
        vcredist_done:
    ${EndIf}

    ; ========================================
    ; Task 1: Import aiDAPTIVAppStore.cer to Trusted Root CA (if present)
    ; ========================================
    DetailPrint "Checking for aiDAPTIVAppStore.cer in installer directory..."
    System::Call 'kernel32::GetModuleFileName(i 0, t .R0, i 1024)'
    ${GetParent} "$R0" $R5
    IfFileExists "$R5\aiDAPTIVAppStore.cer" 0 cert_missing
        DetailPrint "Found certificate: $R5\aiDAPTIVAppStore.cer"
        DetailPrint "Importing certificate into Trusted Root Certification Authorities..."
        nsExec::ExecToStack `cmd.exe /c certutil -f -addstore "Root" "$R5\aiDAPTIVAppStore.cer"`
        Pop $0
        Pop $1
        ${If} $0 != 0
            DetailPrint "Warning: Failed to import aiDAPTIVAppStore.cer. Exit code: $0, Output: $1"
        ${Else}
            DetailPrint "Successfully imported aiDAPTIVAppStore.cer to Trusted Root."
        ${EndIf}
        Goto cert_done
    cert_missing:
        DetailPrint "aiDAPTIVAppStore.cer not found in installer directory, skipping certificate import."
    cert_done:

    ; ========================================
    ; Task 1: Extract scoop.zip to %USERPROFILE%\scoop
    ; ========================================
    DetailPrint "Extracting scoop.zip to $SCOOP_DEST_PATH..."
    
    ; Check if scoop.zip exists
    IfFileExists "$SCOOP_ZIP_PATH" +3 0
        DetailPrint "Error: scoop.zip not found at $SCOOP_ZIP_PATH"
        Abort "scoop.zip not found. Please ensure scoop.zip is in the same directory as the installer."
    
    ; Create scoop destination directory if it doesn't exist
    CreateDirectory "$SCOOP_DEST_PATH"
    
    ; Extract using PowerShell (supports silent mode)
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "try { Expand-Archive -Path '$SCOOP_ZIP_PATH' -DestinationPath '$SCOOP_DEST_PATH' -Force; exit 0 } catch { Write-Host $$_.Exception.Message; exit 1 }"`
    Pop $0
    Pop $1
    ${If} $0 != 0
        DetailPrint "Error extracting scoop.zip: $1"
        Abort "Failed to extract scoop.zip. Error: $1"
    ${EndIf}
    DetailPrint "Successfully extracted scoop.zip to $SCOOP_DEST_PATH"
    
    ; ========================================
    ; Task 2: Add %USERPROFILE%\scoop\shims to user PATH
    ; ========================================
    DetailPrint "Adding $SHIMS_PATH to user PATH..."
    
    ; Check if path already exists in PATH, if not add it
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "try { $$currentPath = [System.Environment]::GetEnvironmentVariable('Path', [System.EnvironmentVariableTarget]::User); $$shimsPath = '$SHIMS_PATH'; if ($$currentPath -notlike \"*$$shimsPath*\") { $$newPath = if ($$currentPath) { \"$$currentPath;$$shimsPath\" } else { $$shimsPath }; [System.Environment]::SetEnvironmentVariable('Path', $$newPath, [System.EnvironmentVariableTarget]::User); Write-Host 'Path added successfully'; exit 0 } else { Write-Host 'Path already exists'; exit 0 } } catch { Write-Host $$_.Exception.Message; exit 1 }"`
    Pop $0
    Pop $1
    ${If} $0 != 0
        DetailPrint "Warning: Failed to add to PATH: $1"
    ${Else}
        DetailPrint "Successfully updated user PATH: $1"
    ${EndIf}
    
    ; ========================================
    ; Task 3: Execute scoop reset *
    ; ========================================
    DetailPrint "Executing scoop reset *..."
    nsExec::ExecToLog `powershell.exe -ExecutionPolicy Bypass -NoProfile -File "$SCOOP_DEST_PATH\apps\scoop\current\bin\scoop.ps1" reset *`
    Pop $0
    ${If} $0 != 0
        DetailPrint "Error: scoop reset failed with exit code: $0"
        MessageBox MB_OK|MB_ICONEXCLAMATION "scoop reset failed with exit code: $0$\r$\n$\r$\nPlease check the installation log for details."
        Abort "Installation aborted due to scoop reset failure."
    ${Else}
        DetailPrint "Successfully executed scoop reset *"
    ${EndIf}
    
    ; ========================================
    ; Task 4: Configure Scoop ignore_running_processes
    ; ========================================
    DetailPrint "Configuring Scoop: ignore_running_processes..."
    nsExec::Exec `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "$$env:Path = \"$$env:USERPROFILE\scoop\shims;$$env:USERPROFILE\scoop\apps\scoop\current\bin;$$env:Path\"; & \"$$env:USERPROFILE\scoop\shims\scoop.cmd\" config ignore_running_processes true"`
    DetailPrint "Scoop ignore_running_processes configured."
    
    ; ========================================
    ; Task 5: Copy installer contents to $LOCALAPPDATA\${APP_DATA_FOLDER}
    ; ========================================
    DetailPrint "Copying files to $INSTDIR..."
    
    ; Create installation directory
    CreateDirectory "$INSTDIR"
    SetOutPath "$INSTDIR"
    
    ; Copy all files from installer directory
    ; Exclude only the nsi script and scoop.zip (which is extracted separately)
    ; The compiled installer exe is output to a different location, so no need to exclude it
    File /nonfatal /r /x "*.nsi" /x "scoop.zip" /x "VC_redist.x64.exe" "*.*"
    
    ; Save installation path to registry
    WriteRegStr HKCU "Software\${APP_DATA_FOLDER}" "" $INSTDIR
    WriteRegStr HKCU "Software\${APP_DATA_FOLDER}" "Version" "${APP_VERSION}"
    
    ; Create uninstaller
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    
    ; Create desktop shortcut
    CreateShortCut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE_NAME}" "" "$INSTDIR\${APP_EXE_NAME}" 0
    
    ; ========================================
    ; Task 6: Copy models folder to $INSTDIR\appstore\aiDAPTIV\aidaptiv
    ; ========================================
    DetailPrint "Copying models folder to $INSTDIR\appstore\aiDAPTIV\aidaptiv..."
    
    ; Get installer directory path for models folder
    System::Call 'kernel32::GetModuleFileName(i 0, t .R0, i 1024)'
    ${GetParent} "$R0" $R2
    
    ; Check if models folder exists in installer directory
    IfFileExists "$R2\models\*.*" 0 +6
        ; Create target directory under installed executable path and copy models folder
        CreateDirectory "$INSTDIR\appstore\aiDAPTIV\aidaptiv"
        nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "try { Copy-Item -Path '$R2\models' -Destination '$INSTDIR\appstore\aiDAPTIV\aidaptiv' -Recurse -Force; exit 0 } catch { Write-Host $$_.Exception.Message; exit 1 }"`
        Pop $0
        Pop $1
        ${If} $0 != 0
            DetailPrint "Warning: Failed to copy models folder: $1"
        ${Else}
            DetailPrint "Successfully copied models folder to $INSTDIR\appstore\aiDAPTIV\aidaptiv"
        ${EndIf}
        Goto +2
    DetailPrint "Note: models folder not found in installer directory, skipping..."
    
    ; ========================================
    ; Task 7: Set PHISON_AIDAPTIV environment variable
    ; ========================================
    DetailPrint "Setting PHISON_AIDAPTIV environment variable..."
    WriteRegExpandStr HKCU "Environment" "PHISON_AIDAPTIV" "$KV_CACHE_DIR"
    ClearErrors
    WriteRegExpandStr HKLM "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" "PHISON_AIDAPTIV" "$KV_CACHE_DIR"
    ${If} ${Errors}
        DetailPrint "Warning: Failed to set machine-level PHISON_AIDAPTIV. User-level value was still written."
    ${EndIf}
    ; Non-blocking broadcast to avoid hanging on unresponsive windows.
    System::Call 'user32::SendNotifyMessage(p ${HWND_BROADCAST}, i ${WM_WININICHANGE}, p 0, t "Environment")'
    DetailPrint "PHISON_AIDAPTIV set to: $KV_CACHE_DIR"

    ; Write registry entries for Add/Remove Programs
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_DATA_FOLDER}" \
                 "DisplayName" "${APP_NAME}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_DATA_FOLDER}" \
                 "DisplayVersion" "${APP_VERSION}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_DATA_FOLDER}" \
                 "UninstallString" "$INSTDIR\Uninstall.exe"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_DATA_FOLDER}" \
                 "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_DATA_FOLDER}" \
                 "Publisher" "Phison"
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_DATA_FOLDER}" \
                 "NoModify" 1
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_DATA_FOLDER}" \
                 "NoRepair" 1

    ; ========================================
    ; Task 8: Run post-install updater silently
    ; ========================================
    DetailPrint "Running post-install update: aiDAPTIVAppStore_release.exe /UPDATE /S..."
    IfFileExists "$INSTDIR\aiDAPTIVAppStore_release.exe" 0 +6
        nsExec::ExecToStack '"$INSTDIR\aiDAPTIVAppStore_release.exe" /UPDATE /S'
        Pop $0
        Pop $1
        ${If} $0 != 0
            DetailPrint "Warning: post-install update exited with code $0. Output: $1"
        ${EndIf}
        Goto +2
    DetailPrint "Warning: $INSTDIR\aiDAPTIVAppStore_release.exe not found, skipping post-install update."

    ; ========================================
    ; Task 9: Delete post-install updater
    ; ========================================
    DetailPrint "Deleting aiDAPTIVAppStore_release.exe..."
    IfFileExists "$INSTDIR\aiDAPTIVAppStore_release.exe" 0 updater_delete_missing
        Delete "$INSTDIR\aiDAPTIVAppStore_release.exe"
        IfErrors updater_delete_failed 0
        DetailPrint "Deleted: $INSTDIR\aiDAPTIVAppStore_release.exe"
        Goto updater_delete_done
    updater_delete_failed:
        DetailPrint "Warning: Failed to delete $INSTDIR\aiDAPTIVAppStore_release.exe"
        Goto updater_delete_done
    updater_delete_missing:
        DetailPrint "Note: aiDAPTIVAppStore_release.exe not found after install."
    updater_delete_done:

    ; ========================================
    ; Task 10: Install aidaptiv-meetily via Scoop
    ; ========================================
    DetailPrint "Installing aidaptiv-meetily via Scoop..."
    nsExec::ExecToLog `powershell.exe -ExecutionPolicy Bypass -NoProfile -Command "$$env:Path = \"$$env:USERPROFILE\scoop\shims;$$env:USERPROFILE\scoop\apps\scoop\current\bin;$$env:Path\"; & scoop install aiDAPTIV-bucket/aidaptiv-meetily"`
    Pop $0
    ${If} $0 != 0
        DetailPrint "Warning: scoop install aidaptiv-meetily failed with exit code: $0"
        MessageBox MB_OK|MB_ICONEXCLAMATION "Failed to install aidaptiv-meetily via Scoop (exit code: $0).$\r$\n$\r$\nYou may need to install it manually using: scoop install aiDAPTIV-bucket/aidaptiv-meetily"
    ${Else}
        DetailPrint "Successfully installed aidaptiv.meetily via Scoop."
    ${EndIf}

    ; ========================================
    ; Task 11: Extract meetily_init.zip to %APPDATA%\com.meetily.ai
    ; ========================================
    DetailPrint "Extracting meetily_init.zip to $APPDATA\com.meetily.ai..."
    System::Call 'kernel32::GetModuleFileName(i 0, t .R0, i 1024)'
    ${GetParent} "$R0" $R4
    IfFileExists "$R4\meetily_init.zip" 0 meeting_zip_not_found
        CreateDirectory "$APPDATA\com.aidaptiv.meetily"
        nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "try { Expand-Archive -Path '$R4\meetily_init.zip' -DestinationPath '$APPDATA\com.aidaptiv.meetily' -Force; exit 0 } catch { Write-Host $$_.Exception.Message; exit 1 }"`
        Pop $0
        Pop $1
        ${If} $0 != 0
            DetailPrint "Warning: Failed to extract meetily_init.zip: $1"
            MessageBox MB_OK|MB_ICONEXCLAMATION "Failed to extract meetily_init.zip.$\r$\n$\r$\nError: $1"
        ${Else}
            DetailPrint "Successfully extracted meetily_init.zip to $APPDATA\com.aidaptiv.meetily"
        ${EndIf}
        Goto meeting_zip_done
    meeting_zip_not_found:
        DetailPrint "Warning: meetily_init.zip not found in installer directory, skipping."
    meeting_zip_done:
    
    DetailPrint "Installation completed successfully!"

SectionEnd

;--------------------------------
; Uninstaller Section
Section "Uninstall"

    ; Check if application is currently running
    DetailPrint "Checking if ${APP_NAME} is running..."
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "try { $$p = Get-Process -Name '${APP_PROCESS_NAME}' -ErrorAction SilentlyContinue; if ($$p) { Write-Host 'running'; exit 1 } else { exit 0 } } catch { exit 0 }"`
    Pop $0
    Pop $1
    ${If} $0 != 0
        MessageBox MB_OK|MB_ICONEXCLAMATION "${APP_NAME} is currently running. Please close the application before uninstalling."
        Abort
    ${EndIf}

    ; Remove desktop shortcut
    Delete "$DESKTOP\${APP_NAME}.lnk"
    
    ; Remove files from installation directory
    RMDir /r "$INSTDIR"
    
    ; Remove PATH entry (optional - uncomment if you want to clean up PATH on uninstall)
    ; DetailPrint "Removing scoop shims from PATH..."
    ; nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "try { $$currentPath = [System.Environment]::GetEnvironmentVariable('Path', [System.EnvironmentVariableTarget]::User); $$shimsPath = \"$$env:USERPROFILE\scoop\shims\"; $$newPath = ($$currentPath -split ';' | Where-Object { $$_ -ne $$shimsPath -and $$_ -ne '' }) -join ';'; [System.Environment]::SetEnvironmentVariable('Path', $$newPath, [System.EnvironmentVariableTarget]::User); exit 0 } catch { exit 1 }"`
    
    ; Remove PHISON_AIDAPTIV environment variable
    DetailPrint "Removing PHISON_AIDAPTIV environment variable..."
    DeleteRegValue HKCU "Environment" "PHISON_AIDAPTIV"
    DeleteRegValue HKLM "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" "PHISON_AIDAPTIV"
    ; Non-blocking broadcast to avoid hanging on unresponsive windows.
    System::Call 'user32::SendNotifyMessage(p ${HWND_BROADCAST}, i ${WM_WININICHANGE}, p 0, t "Environment")'
    DetailPrint "PHISON_AIDAPTIV environment variable removed."
    
    ; Delete registry keys
    DeleteRegKey HKCU "Software\${APP_DATA_FOLDER}"
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_DATA_FOLDER}"
    
    DetailPrint "Uninstallation completed!"

SectionEnd
