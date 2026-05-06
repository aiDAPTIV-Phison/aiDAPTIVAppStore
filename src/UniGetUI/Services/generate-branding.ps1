param (
    [string]$OutputPath = "obj\Generated",
    [string]$AppDisplayName = "PhisonUniGet",
    [string]$AppDescription = "PhisonUniGet",
    [string]$AppIconFileName = "icon.ico",
    [string]$AppShortcutName = "PhisonUniGet",
    [string]$AppExecutableName = "PhisonUniGet",
    [string]$AppIdentifier = "Phison.PhisonUniGet",
    [string]$AppDataFolderName = "PhisonUniGet",
    [string]$AppBackupFolderName = "PhisonUniGet"
)

Write-Host "Generating App Branding files..."
Write-Host "  AppDisplayName: $AppDisplayName"
Write-Host "  AppExecutableName: $AppExecutableName"

# Ensure directory exists
if (-not (Test-Path -Path "$OutputPath\Generated Files")) {
    New-Item -ItemType Directory -Path "$OutputPath\Generated Files" -Force | Out-Null
}

if (-not (Test-Path -Path "Generated Files")) {
    New-Item -ItemType Directory -Path "Generated Files" -Force | Out-Null
}

# Generate C# branding constants
@"
// Auto-generated file - do not modify
// This file is generated from AppBranding.props during build
// To change app branding, edit AppBranding.props and rebuild

namespace UniGetUI.Services
{
    /// <summary>
    /// App branding constants generated from AppBranding.props
    /// </summary>
    internal static class AppBranding
    {
        /// <summary>
        /// App display name (used in window titles, notifications, etc.)
        /// </summary>
        public const string DisplayName = `"$AppDisplayName`";
        
        /// <summary>
        /// App description
        /// </summary>
        public const string Description = `"$AppDescription`";
        
        /// <summary>
        /// App icon file name in Assets/Images folder
        /// </summary>
        public const string IconFileName = `"$AppIconFileName`";
        
        /// <summary>
        /// Shortcut file name (without extension)
        /// </summary>
        public const string ShortcutName = `"$AppShortcutName`";
        
        /// <summary>
        /// Executable file name (without extension)
        /// </summary>
        public const string ExecutableName = `"$AppExecutableName`";
        
        /// <summary>
        /// Full executable file name with .exe extension
        /// </summary>
        public const string ExecutableFileName = `"$AppExecutableName.exe`";
        
        /// <summary>
        /// Full executable file name with .dll extension
        /// </summary>
        public const string DllFileName = `"$AppExecutableName.dll`";
        
        /// <summary>
        /// App identifier for Windows
        /// </summary>
        public const string Identifier = `"$AppIdentifier`";
        
        /// <summary>
        /// User data folder name (in %LocalAppData%)
        /// </summary>
        public const string DataFolderName = `"$AppDataFolderName`";
        
        /// <summary>
        /// Backup folder name (in Documents)
        /// </summary>
        public const string BackupFolderName = `"$AppBackupFolderName`";
    }
}
"@ | Set-Content -Encoding UTF8 "Generated Files\AppBranding.Generated.cs"
Copy-Item "Generated Files\AppBranding.Generated.cs" "$OutputPath\Generated Files\AppBranding.Generated.cs"

# Generate Package.appxmanifest from template
$templatePath = "Package.appxmanifest.template"
$outputManifestPath = "Package.appxmanifest"
if (Test-Path $templatePath) {
    $content = Get-Content -Path $templatePath -Raw
    $content = $content -replace '\{\{AppDisplayName\}\}', $AppDisplayName
    $content = $content -replace '\{\{AppDescription\}\}', $AppDescription
    $content = $content -replace '\{\{AppExecutableName\}\}', $AppExecutableName
    $content | Set-Content -Encoding UTF8 $outputManifestPath
    Write-Host "  Generated: $outputManifestPath"
}

# Generate app.manifest from template
$appManifestTemplatePath = "app.manifest.template"
$appManifestOutputPath = "app.manifest"
if (Test-Path $appManifestTemplatePath) {
    $content = Get-Content -Path $appManifestTemplatePath -Raw
    $content = $content -replace '\{\{AppDisplayName\}\}', $AppDisplayName
    $content = $content -replace '\{\{AppDescription\}\}', $AppDescription
    $content = $content -replace '\{\{AppExecutableName\}\}', $AppExecutableName
    $content | Set-Content -Encoding UTF8 $appManifestOutputPath
    Write-Host "  Generated: $appManifestOutputPath"
}

# Generate utility scripts from templates
$utilityTemplates = @(
    "Assets\Utilities\install_scoop.cmd.template",
    "Assets\Utilities\uninstall_scoop.cmd.template"
)

foreach ($templatePath in $utilityTemplates) {
    if (Test-Path $templatePath) {
        $outputPath = $templatePath -replace '\.template$', ''
        $content = Get-Content -Path $templatePath -Raw
        $content = $content -replace '\{\{AppDisplayName\}\}', $AppDisplayName
        $content = $content -replace '\{\{AppDescription\}\}', $AppDescription
        $content = $content -replace '\{\{AppExecutableName\}\}', $AppExecutableName
        $content | Set-Content -Encoding ASCII $outputPath
        Write-Host "  Generated: $outputPath"
    }
}

# Generate SharedAssemblyInfo.cs from template (located in parent src directory)
$sharedAssemblyTemplatePath = "..\SharedAssemblyInfo.cs.template"
$sharedAssemblyOutputPath = "..\SharedAssemblyInfo.cs"
if (Test-Path $sharedAssemblyTemplatePath) {
    $content = Get-Content -Path $sharedAssemblyTemplatePath -Raw
    $content = $content -replace '\{\{AppDisplayName\}\}', $AppDisplayName
    $content = $content -replace '\{\{AppDescription\}\}', $AppDescription
    $content = $content -replace '\{\{AppExecutableName\}\}', $AppExecutableName
    $content | Set-Content -Encoding UTF8 $sharedAssemblyOutputPath
    Write-Host "  Generated: $sharedAssemblyOutputPath"
}

Write-Host "App Branding generation complete."
