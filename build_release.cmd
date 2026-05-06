@echo off
setlocal enabledelayedexpansion

rem ═══════════════════════════════════════════════════════════════════════════
rem Read app branding configuration from AppBranding.props
rem ═══════════════════════════════════════════════════════════════════════════
for /f "delims=" %%i in ('pwsh -NoProfile -Command "[xml]$x = Get-Content 'src\AppBranding.props'; $x.Project.PropertyGroup.AppExecutableName"') do set AppExecutableName=%%i
for /f "delims=" %%i in ('pwsh -NoProfile -Command "[xml]$x = Get-Content 'src\AppBranding.props'; $x.Project.PropertyGroup.AppDisplayName"') do set AppDisplayName=%%i
echo Building %AppDisplayName% (%AppExecutableName%)...

rem Generate installer branding definitions
pwsh -NoProfile -File scripts\generate_iss_branding.ps1
pwsh -NoProfile -File scripts\generate_nsis_branding.ps1

rem update resources
call python scripts/apply_versions.py

rem pushd scripts
rem python download_translations.py
rem popd ..

rem clean old builds
taskkill /im %AppExecutableName%.exe /f

rem Run tests
dotnet test src/UniGetUI.sln -v q --nologo

rem check exit code of the last command
if %errorlevel% neq 0 (
    echo "The tests failed!."
    pause
)

rem build executable
dotnet clean src/UniGetUI.sln -v m -nologo
dotnet publish src/UniGetUI/UniGetUI.csproj /noLogo /property:Configuration=Release /property:Platform=x64 -v m
if %errorlevel% neq 0 (
    echo "DotNet publish has failed!"
    pause
)
rem sign code

rmdir /Q /S phison_bin

mkdir phison_bin
robocopy src\UniGetUI\bin\x64\Release\net8.0-windows10.0.26100.0\win-x64\publish phison_bin *.* /MOVE /E

rem Copy aiDAPTIV files (excluding legacy folder since Legacy Mode is removed)
echo Copying aiDAPTIV files...
mkdir phison_bin\appstore\aiDAPTIV
robocopy aiDAPTIV phison_bin\appstore\aiDAPTIV *.* /E /NP /NFL /NDL /XD legacy
if %errorlevel% leq 7 (
    echo aiDAPTIV files copied successfully
) else (
    echo Warning: aiDAPTIV files copy may have failed
)


rem Copy aidaptiv_system_check.json to exe directory
echo Copying aidaptiv_system_check.json...
copy /Y aidaptiv_system_check.json phison_bin\aidaptiv_system_check.json
if %errorlevel% equ 0 (
    echo aidaptiv_system_check.json copied successfully
) else (
    echo Warning: aidaptiv_system_check.json copy failed
)

rem Copy aidaptiv_update.json to exe directory
echo Copying aidaptiv_update.json...
copy /Y aidaptiv_update.json phison_bin\aidaptiv_update.json
if %errorlevel% equ 0 (
    echo aidaptiv_update.json copied successfully
) else (
    echo Warning: aidaptiv_update.json copy failed
)

set /p signfiles="Do you want to sign the files? [Y/n]: "
if /i "%signfiles%"=="Y" (
    set "SIGN_CERT_PATH=certs\aiDAPTIVAppStore.pfx"
    set /p SIGN_CERT_PASSWORD="Enter PFX password: "
    set "SIGNTOOL="

    rem Find SignTool automatically from common SDK paths first, then PATH.
    for %%P in (
        "%ProgramFiles(x86)%\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
        "%ProgramFiles(x86)%\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
        "%ProgramFiles(x86)%\Windows Kits\10\bin\x64\signtool.exe"
        "%ProgramFiles%\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
        "%ProgramFiles%\Windows Kits\10\bin\x64\signtool.exe"
    ) do (
        if not defined SIGNTOOL if exist %%~P set "SIGNTOOL=%%~P"
    )
    if not defined SIGNTOOL (
        for /f "delims=" %%S in ('where signtool.exe 2^>nul') do (
            if not defined SIGNTOOL set "SIGNTOOL=%%~fS"
        )
    )

    if not defined SIGNTOOL (
        echo ERROR: SignTool was not found. Please install Windows SDK SignTool first.
        pause
        exit /b 1
    )
    if not exist "!SIGN_CERT_PATH!" (
        echo ERROR: Certificate file not found: !SIGN_CERT_PATH!
        pause
        exit /b 1
    )

    echo Signing executable with: "!SIGNTOOL!"
    "!SIGNTOOL!" sign /f "!SIGN_CERT_PATH!" /p "!SIGN_CERT_PASSWORD!" /fd sha256 /tr http://timestamp.digicert.com /td sha256 "phison_bin\%AppExecutableName%.exe"
    if !errorlevel! neq 0 (
        echo ERROR: Signing executable failed.
        pause
        exit /b 1
    )

    for %%F in ("phison_bin\*.dll") do (
        if exist "%%~fF" (
            "!SIGNTOOL!" sign /f "!SIGN_CERT_PATH!" /p "!SIGN_CERT_PASSWORD!" /fd sha256 /tr http://timestamp.digicert.com /td sha256 "%%~fF"
            if !errorlevel! neq 0 (
                echo ERROR: Signing DLL failed: %%~nxF
                pause
                exit /b 1
            )
        )
    )
)

pushd phison_bin
popd


rem Generate integrity
python scripts\generate_integrity_tree.py %cd%\phison_bin

rmdir /q /s output
mkdir output
cd phison_bin
7z a -tzip "..\output\%AppExecutableName%.x64.zip" "*"
cd ..
if %errorlevel% neq 0 (
    echo "Compression of phison_bin into output/%AppExecutableName%.x64.zip has failed!"
    pause
)

set INSTALLATOR="%localappdata%\Programs\Inno Setup 6\ISCC.exe"
if exist %INSTALLATOR% (
    %INSTALLATOR% "UniGetUI.iss"
    move "%AppDisplayName% Installer.exe" "%AppExecutableName%.Installer.exe"
    move "%AppExecutableName%.Installer.exe" output\
    pwsh.exe -Command echo """%AppExecutableName%.Installer.exe SHA256: ``$((Get-FileHash 'output\%AppExecutableName%.Installer.exe').Hash)``"""
    pwsh.exe -Command echo """%AppExecutableName%.x64.zip SHA256: ``$((Get-FileHash 'output\%AppExecutableName%.x64.zip').Hash)``"""
    echo .
    pause
    "output\%AppExecutableName%.Installer.exe"
) else (
    echo "Make installer was skipped, because the installer is missing."
)

rem ========================================
rem Build and Sign MSIX Package
rem ========================================
echo.
echo ========================================
echo [8/8] Building MSIX package...
echo ========================================
set /p buildmsix="Do you want to build and sign MSIX package? [Y/n]: "
if /i "%buildmsix%"=="Y" (
    rem setup tool path
    set "SIGNTOOL="
    set MAKEAPPX="C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe"
    rem set certification and password
    set "CERT_PATH=certs\aiDAPTIVAppStore.pfx"
    set /p CERT_PASSWORD="Enter PFX password for MSIX signing: "

    rem Find SignTool automatically from common SDK paths first, then PATH.
    for %%P in (
        "%ProgramFiles(x86)%\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
        "%ProgramFiles(x86)%\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
        "%ProgramFiles(x86)%\Windows Kits\10\bin\x64\signtool.exe"
        "%ProgramFiles%\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
        "%ProgramFiles%\Windows Kits\10\bin\x64\signtool.exe"
    ) do (
        if not defined SIGNTOOL if exist %%~P set "SIGNTOOL=%%~P"
    )
    if not defined SIGNTOOL (
        for /f "delims=" %%S in ('where signtool.exe 2^>nul') do (
            if not defined SIGNTOOL set "SIGNTOOL=%%~fS"
        )
    )
    if not defined SIGNTOOL (
        echo ERROR: SignTool was not found. Please install Windows SDK SignTool first.
        goto :skip_msix_sign
    )
    if not exist "!CERT_PATH!" (
        echo ERROR: Certificate file not found: !CERT_PATH!
        goto :skip_msix_sign
    )
    rem create MSIX output folder
    if not exist "output\msix" mkdir output\msix
    echo Building MSIX package...
    echo Using makeappx to create MSIX package...
    if not exist "phison_bin" (
        echo ERROR: phison_bin directory not found! Cannot create MSIX.
        goto :skip_msix_sign
    )
    rem Prepare MSIX required image assets
    echo Preparing MSIX image assets...
    if not exist "phison_bin\Assets" mkdir phison_bin\Assets
    rem Use existing icon.png as base for all required MSIX images
    set ICON_SOURCE=phison_bin\Assets\Images\icon.png
    if exist "!ICON_SOURCE!" (
        copy /Y "!ICON_SOURCE!" "phison_bin\Assets\StoreLogo.png" >nul
        copy /Y "!ICON_SOURCE!" "phison_bin\Assets\Square150x150Logo.png" >nul
        copy /Y "!ICON_SOURCE!" "phison_bin\Assets\Square44x44Logo.png" >nul
        copy /Y "!ICON_SOURCE!" "phison_bin\Assets\Wide310x150Logo.png" >nul
        copy /Y "!ICON_SOURCE!" "phison_bin\Assets\SplashScreen.png" >nul
        echo MSIX image assets prepared successfully.
    ) else (
        echo WARNING: Source icon not found at !ICON_SOURCE!
        echo Creating placeholder images...
        rem Create simple placeholder images using PowerShell
        powershell -NoProfile -Command "$bmp = New-Object System.Drawing.Bitmap(150,150); $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::FromArgb(0,120,212)); $bmp.Save('phison_bin\Assets\Square150x150Logo.png'); $bmp.Dispose()"
        powershell -NoProfile -Command "$bmp = New-Object System.Drawing.Bitmap(44,44); $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::FromArgb(0,120,212)); $bmp.Save('phison_bin\Assets\Square44x44Logo.png'); $bmp.Dispose()"
        powershell -NoProfile -Command "$bmp = New-Object System.Drawing.Bitmap(50,50); $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::FromArgb(0,120,212)); $bmp.Save('phison_bin\Assets\StoreLogo.png'); $bmp.Dispose()"
        powershell -NoProfile -Command "$bmp = New-Object System.Drawing.Bitmap(310,150); $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::FromArgb(0,120,212)); $bmp.Save('phison_bin\Assets\Wide310x150Logo.png'); $bmp.Dispose()"
        powershell -NoProfile -Command "$bmp = New-Object System.Drawing.Bitmap(620,300); $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::FromArgb(0,120,212)); $bmp.Save('phison_bin\Assets\SplashScreen.png'); $bmp.Dispose()"
        echo Placeholder images created.
    )
    rem process manifest: parameter
    echo Processing manifest file...
    powershell -NoProfile -Command "(Get-Content 'src\UniGetUI\Package.appxmanifest' -Raw) -replace '\$targetnametoken\$', 'PhisonUniGet' -replace '\$targetentrypoint\$', 'Windows.FullTrustApplication' -replace 'x-generate', 'en-US' | Set-Content 'phison_bin\AppxManifest.xml' -Encoding UTF8"
    rem using makeappx to pack installation
    !MAKEAPPX! pack /d phison_bin /p output\msix\PhisonUniGet.msix /o
    set BUILD_RESULT=!errorlevel!
    if !BUILD_RESULT! neq 0 (
        echo WARNING: makeappx failed, searching for existing MSIX files...
        rem search output\msix folder .msix files
        set MSIX_FILE=
        for /r "output\msix" %%f in (*.msix) do (
            set MSIX_FILE=%%f
            goto :found_msix
        )
        if not defined MSIX_FILE (
            echo ERROR: No MSIX file found!
            goto :skip_msix_sign
        )
        :found_msix
        echo Found existing MSIX file: !MSIX_FILE!
    ) else (
        echo MSIX package created successfully.
        set MSIX_FILE=output\msix\PhisonUniGet.msix
        rem verify if file existed
        if not exist "!MSIX_FILE!" (
            echo WARNING: Expected MSIX not found at !MSIX_FILE!, searching...
            set MSIX_FILE=
            for /r "output\msix" %%f in (*.msix) do (
                set MSIX_FILE=%%f
                goto :found_msix2
            )
            if not defined MSIX_FILE (
                echo ERROR: No MSIX file found!
                goto :skip_msix_sign
            )
            :found_msix2
            echo Found MSIX file: !MSIX_FILE!
        ) else (
            echo Found MSIX file: !MSIX_FILE!
        )
    )
    rem using SignTool sign MSIX
    echo Signing MSIX package with certificate...
    !SIGNTOOL! sign /f "!CERT_PATH!" /p "!CERT_PASSWORD!" /fd sha256 /tr http://timestamp.digicert.com /td sha256 "!MSIX_FILE!"
    if !errorlevel! neq 0 (
        echo WARNING: MSIX signing failed!
        goto :skip_msix_sign
    )
    echo MSIX package signed successfully!
    rem Verifying signature
    echo Verifying signature...
    !SIGNTOOL! verify /pa "!MSIX_FILE!"
    rem Calculating MSIX SHA256
    echo Calculating SHA256...
    for /f "skip=1 delims=" %%h in ('certutil -hashfile "!MSIX_FILE!" SHA256') do (
        if not "%%h"=="" (
            set "MSIX_HASH=%%h"
            goto :hash_done
        )
    )
    :hash_done
    echo MSIX SHA256: !MSIX_HASH!
    :skip_msix_sign
    echo MSIX process completed.
    echo.
)
if exist temp_msix (
    rmdir /q /s temp_msix
    echo temp_msix directory removed.
)
if exist "output\msix\PhisonUniGet.msix" (
    echo [OK] MSIX Package: output\msix\PhisonUniGet.msix
    for /f "skip=1 delims=" %%h in ('certutil -hashfile "output\msix\PhisonUniGet.msix" SHA256 2^>nul') do (
        set "line=%%h"
        if not "!line:CertUtil=!"=="!line!" goto msix_hash_done
        if not "!line: =!"=="" echo      SHA256: %%h
        goto msix_hash_done
    )
) else (
    set FOUND_MSIX=0
    for /r "output\msix" %%f in (*.msix) do (
        echo [OK] MSIX Package: %%f
        set MSIX_TEMP=%%f
        for /f "skip=1 delims=" %%h in ('certutil -hashfile "!MSIX_TEMP!" SHA256 2^>nul') do (
            set "line=%%h"
            if not "!line:CertUtil=!"=="!line!" goto msix_search_hash_done
            if not "!line: =!"=="" echo      SHA256: %%h
            goto msix_search_hash_done
        )
        :msix_search_hash_done
        set FOUND_MSIX=1
        goto end_msix
    )
    if !FOUND_MSIX! equ 0 echo [SKIPPED] MSIX Package not created
)
:msix_hash_done
:end_msix
pause
