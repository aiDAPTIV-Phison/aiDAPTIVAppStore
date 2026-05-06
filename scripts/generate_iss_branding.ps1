# Generate Inno Setup branding definitions from AppBranding.props
# This script generates InstallerExtras\AppBranding.iss

param(
    [string]$OutputPath = "InstallerExtras\AppBranding.iss"
)

$propsPath = Join-Path $PSScriptRoot "..\src\AppBranding.props"

if (-not (Test-Path $propsPath)) {
    Write-Error "AppBranding.props not found at: $propsPath"
    exit 1
}

# Parse the XML
[xml]$props = Get-Content $propsPath

# Extract values from PropertyGroup
$pg = $props.Project.PropertyGroup

$issContent = @"
; Auto-generated file - do not modify
; This file is generated from src/AppBranding.props
; Run scripts/generate_iss_branding.ps1 to regenerate

#define MyAppName "$($pg.AppDisplayName)"
#define MyAppExeName "$($pg.AppExecutableName).exe"
#define MyAppIdentifier "$($pg.AppIdentifier)"
#define MyAppDataFolderName "$($pg.AppDataFolderName)"
"@

# Ensure output directory exists
$outputDir = Split-Path $OutputPath -Parent
if ($outputDir -and -not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$issContent | Set-Content -Encoding ASCII $OutputPath
Write-Host "Generated: $OutputPath"
