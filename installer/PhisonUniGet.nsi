; NSIS Installer Script
; Package files from nsi directory and execute installation tasks

;--------------------------------
; Include Modern UI and Branding
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "nsDialogs.nsh"
!include "WinMessages.nsh"

; Include auto-generated branding definitions
!include "..\InstallerExtras\AppBranding.nsh"

;--------------------------------
; General Settings

; Name of installer
Name "${APP_NAME} Installer"

; Output file name
OutFile "${APP_EXE_BASENAME}_release.exe"

; Default installation directory (AppData\Local)
InstallDir "$LOCALAPPDATA\${APP_DATA_FOLDER}"

; Get installation directory from registry
InstallDirRegKey HKCU "Software\${APP_DATA_FOLDER}" ""

; Request admin to allow stopping Windows services (ada_service)
RequestExecutionLevel admin

; Variables
Var KV_CACHE_DIR
Var KV_CACHE_DIR_HWND
Var IS_UPDATE

;--------------------------------
; Interface Settings

!define MUI_ABORTWARNING
!define MUI_ABORTWARNING_TEXT "Are you sure you wish to abort installation?"

; Installer pages
!define MUI_PAGE_CUSTOMFUNCTION_PRE WelcomePagePre
!insertmacro MUI_PAGE_WELCOME
Page custom KVCacheDirPage KVCacheDirPageLeave
!define MUI_PAGE_CUSTOMFUNCTION_SHOW InstFilesShow
!insertmacro MUI_PAGE_INSTFILES
!define MUI_PAGE_CUSTOMFUNCTION_PRE FinishPagePre
!insertmacro MUI_PAGE_FINISH

; Uninstaller pages
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

;--------------------------------
; Language
!insertmacro MUI_LANGUAGE "English"

;--------------------------------
; Initialization - parse /UPDATE parameter

Function .onInit
    StrCpy $IS_UPDATE "0"
    ${GetParameters} $0
    StrCmp $0 "/UPDATE" 0 +2
    StrCpy $IS_UPDATE "1"
FunctionEnd

Function WelcomePagePre
    StrCmp $IS_UPDATE "1" 0 +2
    Abort
FunctionEnd

Function FinishPagePre
    StrCmp $IS_UPDATE "1" 0 +2
    Abort
FunctionEnd

;--------------------------------
; KV Cache Directory Selection Page

Function KVCacheDirPage
    StrCmp $IS_UPDATE "1" 0 +2
    Abort
    !insertmacro MUI_HEADER_TEXT "KV Cache Directory" "Specify the directory for KV Cache storage"
    
    nsDialogs::Create 1018
    Pop $0
    
    ${NSD_CreateLabel} 0 10u 100% 30u "Please specify the directory path for KV Cache storage:"
    Pop $0
    
    ${NSD_CreateLabel} 0 50u 100% 20u "KV Cache Directory: (Optional)"
    Pop $0
    
    ${NSD_CreateDirRequest} 0 75u 70% 12u ""
    Pop $KV_CACHE_DIR_HWND
    
    ${NSD_CreateBrowseButton} 75% 75u 25% 12u "Browse..."
    Pop $0
    ${NSD_OnClick} $0 KVCacheDirBrowse
    
    ; Initialize variable as empty
    StrCpy $KV_CACHE_DIR ""
    
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
    
    ; If left empty, skip validation (optional field)
    ${If} $KV_CACHE_DIR == ""
        DetailPrint "KV Cache Directory not specified, skipping."
        Return
    ${EndIf}
    
    ; Validate path format and check if it exists (if exists, verify it's a directory)
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "try { $$path = '$KV_CACHE_DIR'; $$normalized = [System.IO.Path]::GetFullPath($$path); if (Test-Path $$path) { if (-not (Test-Path $$path -PathType Container)) { Write-Host 'Path exists but is not a directory'; exit 2 } else { exit 0 } } else { exit 0 } } catch { Write-Host $$_.Exception.Message; exit 1 }"`
    Pop $0
    Pop $1
    ${If} $0 == 2
        MessageBox MB_OK|MB_ICONEXCLAMATION "The specified path exists but is not a directory.$\r$\n$\r$\nPath: $KV_CACHE_DIR$\r$\nPlease specify a different path."
        Abort
    ${ElseIf} $0 != 0
        MessageBox MB_OK|MB_ICONEXCLAMATION "Invalid path format.$\r$\n$\r$\nPath: $KV_CACHE_DIR$\r$\nError: $1"
        Abort
    ${EndIf}
    
    DetailPrint "KV Cache directory validated: $KV_CACHE_DIR"
FunctionEnd

;--------------------------------
; Enable Cancel Button on InstFiles Page

Function InstFilesShow
    GetDlgItem $0 $HWNDPARENT 2
    StrCmp $IS_UPDATE "1" 0 +3
    ; In update mode, disable the Cancel button
    EnableWindow $0 0
    Return
    ; In first install mode, enable the Cancel button
    EnableWindow $0 1
FunctionEnd

;--------------------------------
; Installer Section

Section "MainSection" SEC01

    ; Set output directory to AppData\Local\${APP_DATA_FOLDER}
    SetOutPath "$LOCALAPPDATA\${APP_DATA_FOLDER}"
    
    ; Stop apps installed from aiDAPTIV-bucket via their stop.ps1 scripts
    DetailPrint "Stopping aiDAPTIV-bucket applications..."
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "$scoopShim = Join-Path $$env:USERPROFILE 'scoop\shims\scoop.cmd'; if (-not (Test-Path $$scoopShim)) { exit 0 }; $$apps = scoop list 6>&1 | Where-Object { $$_.Source -eq 'aiDAPTIV-bucket' }; foreach ($$app in $$apps) { $$stopScript = Join-Path $$env:USERPROFILE \"scoop\apps\$$($$app.Name)\current\stop.ps1\"; if (Test-Path $$stopScript) { try { & $$stopScript } catch { } } }; exit 0"`
    Pop $0

    ; Gracefully shutdown aiDAPTIVService via API, then force kill remaining processes
    DetailPrint "Shutting down aiDAPTIVService via API..."
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "try { Invoke-RestMethod -Uri 'http://127.0.0.1:13140/shutdown' -Method Post -TimeoutSec 10 | Out-Null } catch { }; exit 0"`
    Pop $0

    ; Stop ada_service via wService_delete.bat from existing installation (if present)
    IfFileExists "$LOCALAPPDATA\${APP_DATA_FOLDER}\appstore\aiDAPTIV\aidaptiv\wService_delete.bat" 0 skip_wservice_delete
    DetailPrint "Stopping and removing ada_service..."
    nsExec::Exec '"$LOCALAPPDATA\${APP_DATA_FOLDER}\appstore\aiDAPTIV\aidaptiv\wService_delete.bat"'
    skip_wservice_delete:

    DetailPrint "Closing remaining processes..."
    nsExec::Exec 'taskkill /im aiDAPTIVService.exe /f'
    nsExec::Exec 'taskkill /im ada.exe /f'
    nsExec::Exec 'taskkill /im ${APP_EXE_NAME} /f'
    Sleep 1000
    
    ; Copy files to installation directory
    StrCmp $IS_UPDATE "1" 0 normal_file_copy
    
    ; Update mode: backup model_path from existing config before overwriting
    DetailPrint "Backing up configuration..."
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "try { $$json = Get-Content '$INSTDIR\appstore\aiDAPTIV\aidaptiv_config.json' -Raw -ErrorAction Stop | ConvertFrom-Json; Write-Host $$json.model_path -NoNewline; exit 0 } catch { Write-Host '' -NoNewline; exit 0 }"`
    Pop $0
    Pop $R9
    
    ; Copy files, excluding Models directory, logs directory, and .nsi scripts
    DetailPrint "Updating application files..."
    File /r /x "Models" /x "logs" /x "*.nsi" "*"
    
    ; Restore model_path to new config if backup was successful
    StrCmp $R9 "" skip_config_restore 0
    DetailPrint "Restoring model_path configuration..."
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "try { $$configPath = '$INSTDIR\appstore\aiDAPTIV\aidaptiv_config.json'; $$json = Get-Content $$configPath -Raw -ErrorAction Stop | ConvertFrom-Json; $$json.model_path = '$R9'; $$content = $$json | ConvertTo-Json -Depth 10; [System.IO.File]::WriteAllText($$configPath, $$content, [System.Text.UTF8Encoding]::new($$false)); exit 0 } catch { Write-Host $$_.Exception.Message; exit 1 }"`
    Pop $0
    Pop $1
    skip_config_restore:
    Goto file_copy_done
    
    normal_file_copy:
    ; First install: copy all files (exclude .nsi scripts)
    File /r /x "*.nsi" "*"
    
    file_copy_done:
    
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
    
    ; In update mode, skip first-install tasks (Scoop, Git, models, etc.)
    StrCmp $IS_UPDATE "1" update_complete
    
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
    
    ; Configure Scoop: ignore_running_processes
    DetailPrint "Configuring Scoop: ignore_running_processes..."
    ; Check if ignore_running_processes is already set to true (hidden window)
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "$$env:Path = \"$$env:USERPROFILE\scoop\shims;$$env:USERPROFILE\scoop\apps\scoop\current\bin;$$env:Path\"; $$config = & \"$$env:USERPROFILE\scoop\shims\scoop.cmd\" config ignore_running_processes; if ($$config -eq 'true') { exit 0 } else { exit 1 }"`
    Pop $0  ; Exit code
    Pop $1  ; Output (not used)
    ${If} $0 != 0
        DetailPrint "Setting Scoop config ignore_running_processes to true..."
        nsExec::Exec `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "$$env:Path = \"$$env:USERPROFILE\scoop\shims;$$env:USERPROFILE\scoop\apps\scoop\current\bin;$$env:Path\"; & \"$$env:USERPROFILE\scoop\shims\scoop.cmd\" config ignore_running_processes true"`
    ${Else}
        DetailPrint "ignore_running_processes is already set to true, skipping."
    ${EndIf}
    
    ; Install Git using Scoop
    DetailPrint "Checking if Git is already installed..."
    ; Check if Git is already installed via Scoop (hidden window)
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "$$env:Path = \"$$env:USERPROFILE\scoop\shims;$$env:USERPROFILE\scoop\apps\scoop\current\bin;$$env:Path\"; if (Test-Path \"$$env:USERPROFILE\scoop\apps\git\current\bin\git.exe\") { exit 0 } elseif (Get-Command git -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }"`
    Pop $0  ; Exit code
    Pop $1  ; Output (not used)
    ${If} $0 != 0
        DetailPrint "Installing Git using Scoop..."
        nsExec::Exec `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "$$env:Path = \"$$env:USERPROFILE\scoop\shims;$$env:USERPROFILE\scoop\apps\scoop\current\bin;$$env:Path\"; & \"$$env:USERPROFILE\scoop\shims\scoop.cmd\" install git"`
    ${Else}
        DetailPrint "Git is already installed, skipping installation."
    ${EndIf}
    
    ; Add Scoop bucket: versions
    DetailPrint "Checking if versions bucket is already added..."
    ; Check if versions bucket is already added (hidden window)
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "$$env:Path = \"$$env:USERPROFILE\scoop\shims;$$env:USERPROFILE\scoop\apps\scoop\current\bin;$$env:Path\"; $$buckets = & \"$$env:USERPROFILE\scoop\shims\scoop.cmd\" bucket list; if ($$buckets -match 'versions') { exit 0 } else { exit 1 }"`
    Pop $0  ; Exit code
    Pop $1  ; Output (not used)
    ${If} $0 != 0
        DetailPrint "Adding Scoop bucket: versions..."
        nsExec::Exec `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "$$env:Path = \"$$env:USERPROFILE\scoop\shims;$$env:USERPROFILE\scoop\apps\scoop\current\bin;$$env:Path\"; & \"$$env:USERPROFILE\scoop\shims\scoop.cmd\" bucket add versions"`
    ${Else}
        DetailPrint "versions bucket is already added, skipping."
    ${EndIf}
    
    ; Add Scoop bucket: aiDAPTIV-bucket
    DetailPrint "Checking if aiDAPTIV-bucket is already added..."
    ; Check if aiDAPTIV-bucket is already added (hidden window)
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "$$env:Path = \"$$env:USERPROFILE\scoop\shims;$$env:USERPROFILE\scoop\apps\scoop\current\bin;$$env:Path\"; $$buckets = & \"$$env:USERPROFILE\scoop\shims\scoop.cmd\" bucket list; if ($$buckets -match 'aiDAPTIV-bucket') { exit 0 } else { exit 1 }"`
    Pop $0  ; Exit code
    Pop $1  ; Output (not used)
    ${If} $0 != 0
        DetailPrint "Adding Scoop bucket: aiDAPTIV-bucket..."
        nsExec::Exec `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "$$env:Path = \"$$env:USERPROFILE\scoop\shims;$$env:USERPROFILE\scoop\apps\scoop\current\bin;$$env:Path\"; & \"$$env:USERPROFILE\scoop\shims\scoop.cmd\" bucket add aiDAPTIV-bucket https://github.com/aiDAPTIV-Phison/aiDAPTIV-bucket"`
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
        nsExec::Exec `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "$$env:Path = \"$$env:USERPROFILE\scoop\shims;$$env:USERPROFILE\scoop\apps\scoop\current\bin;$$env:Path\"; & \"$$env:USERPROFILE\scoop\shims\scoop.cmd\" install scoop-search"`
    ${Else}
        DetailPrint "scoop-search is already installed, skipping installation."
    ${EndIf}
    
    ; Task 2: Files already copied to AppData path via File /r command
    DetailPrint "Files copied to: $INSTDIR"
    
    ; Task 3: Create desktop shortcut
    CreateShortCut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE_NAME}" "" "$INSTDIR\${APP_EXE_NAME}" 0
    
    ; Set KV Cache directory to system environment variable
    DetailPrint "Setting KV Cache directory to system environment variable..."
    ; Set environment variable PHISON_AIDAPTIV with the KV cache directory path
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "try { [System.Environment]::SetEnvironmentVariable('PHISON_AIDAPTIV', '$KV_CACHE_DIR', [System.EnvironmentVariableTarget]::User); Write-Host 'Set successfully'; exit 0 } catch { Write-Host $$_.Exception.Message; exit 1 }"`
    Pop $0
    Pop $1
    ${If} $0 == 0
        DetailPrint "KV Cache directory set to environment variable PHISON_AIDAPTIV: $KV_CACHE_DIR"
    ${Else}
        DetailPrint "Warning: Failed to set KV Cache directory to environment variable. Error: $1"
    ${EndIf}
    
    update_complete:
    ; In update mode, restart the application after update
    StrCmp $IS_UPDATE "1" 0 skip_restart
    DetailPrint "Update completed, restarting application..."
    Exec '"$INSTDIR\${APP_EXE_NAME}"'
    skip_restart:
    
    DetailPrint "Installation completed!"
    
SectionEnd

;--------------------------------
; Uninstaller Section

Section "Uninstall"

    ; Check if ${APP_EXE_BASENAME} is running
    DetailPrint "Checking if ${APP_NAME} is running..."
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "$$process = Get-Process -Name '${APP_EXE_BASENAME}' -ErrorAction SilentlyContinue; if ($$process) { exit 1 } else { exit 0 }"`
    Pop $0
    Pop $1
    ${If} $0 != 0
        MessageBox MB_OK|MB_ICONEXCLAMATION "${APP_NAME} is currently running.$\r$\n$\r$\nPlease close the application before uninstalling."
        Quit
    ${EndIf}
    DetailPrint "${APP_NAME} is not running, proceeding with uninstallation..."

    ; Uninstall all apps from aiDAPTIV-bucket
    DetailPrint "Checking for apps installed from aiDAPTIV-bucket..."
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "$$env:Path = \"$$env:USERPROFILE\scoop\shims;$$env:USERPROFILE\scoop\apps\scoop\current\bin;$$env:Path\"; if (Test-Path \"$$env:USERPROFILE\scoop\shims\scoop.cmd\") { $$apps = & \"$$env:USERPROFILE\scoop\shims\scoop.cmd\" list | Select-String -Pattern 'aiDAPTIV-bucket' | ForEach-Object { $$_.ToString().Trim().Split(' ')[0] }; if ($$apps) { Write-Host \"Found apps: $$($$apps -join ', ')\"; exit 0 } else { Write-Host 'No apps found'; exit 1 } } else { Write-Host 'Scoop not found'; exit 2 }"`
    Pop $0
    Pop $1
    ${If} $0 == 0
        DetailPrint "Found apps from aiDAPTIV-bucket: $1"
        DetailPrint "Uninstalling apps from aiDAPTIV-bucket..."
        nsExec::ExecToLog `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "$$env:Path = \"$$env:USERPROFILE\scoop\shims;$$env:USERPROFILE\scoop\apps\scoop\current\bin;$$env:Path\"; $$apps = & \"$$env:USERPROFILE\scoop\shims\scoop.cmd\" list | Select-String -Pattern 'aiDAPTIV-bucket' | ForEach-Object { $$_.ToString().Trim().Split(' ')[0] }; foreach ($$app in $$apps) { Write-Host \"Uninstalling $$app...\"; & \"$$env:USERPROFILE\scoop\shims\scoop.cmd\" uninstall $$app }"`
        Pop $0
        ${If} $0 == 0
            DetailPrint "Successfully uninstalled all apps from aiDAPTIV-bucket."
        ${Else}
            DetailPrint "Warning: Some apps may not have been uninstalled completely."
        ${EndIf}
    ${ElseIf} $0 == 1
        DetailPrint "No apps from aiDAPTIV-bucket found, skipping."
    ${Else}
        DetailPrint "Scoop not found or not accessible, skipping app uninstallation."
    ${EndIf}

    ; Delete all files in installation directory
    RMDir /r "$INSTDIR"
    
    ; Delete desktop shortcut
    Delete "$DESKTOP\${APP_NAME}.lnk"
    
    ; Delete registry keys
    DeleteRegKey HKCU "Software\${APP_DATA_FOLDER}"
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_DATA_FOLDER}"
    
    ; Remove KV Cache directory environment variable
    DetailPrint "Removing KV Cache directory environment variable..."
    nsExec::ExecToStack `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -Command "try { [System.Environment]::SetEnvironmentVariable('PHISON_AIDAPTIV', $null, [System.EnvironmentVariableTarget]::User); Write-Host 'Removed successfully'; exit 0 } catch { Write-Host $$_.Exception.Message; exit 1 }"`
    Pop $0
    Pop $1
    ${If} $0 == 0
        DetailPrint "KV Cache directory environment variable removed."
    ${Else}
        DetailPrint "Warning: Failed to remove KV Cache directory environment variable. Error: $1"
    ${EndIf}
    
    DetailPrint "Uninstallation completed!"

SectionEnd
