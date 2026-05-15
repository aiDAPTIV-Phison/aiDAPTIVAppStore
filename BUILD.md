# Build Guide

This document explains how to build `aiDAPTIV AppStore` from source on Windows.

## Supported environment

- OS: Windows 10/11 x64
- Shell: PowerShell (`pwsh`) and Command Prompt (`cmd`)

## Prerequisites

Install the following tools before building:

1. `.NET 8 SDK` (required by `src/UniGetUI.sln`)
2. `Python 3` (used by build helper scripts)
3. `PowerShell 7` (`pwsh`, used by branding/manifest scripts)
4. `7-Zip` (`7z` command, used to pack zip artifacts)
5. Optional: `Inno Setup 6` (for generating installer `.exe`)

## Pre-build assets preparation

Before running any build command, prepare aiDAPTIV runtime assets:

1. Get the MW archive package provided by aiDAPTIV.
2. Extract the archive into the repository `aiDAPTIV\` folder.
3. Confirm these folders exist under `aiDAPTIV\` after extraction:
   - `aiDAPTIV\aidaptiv\`
   - `aiDAPTIV\llama-b6766\`
   - `aiDAPTIV\tool\`

If these folders are missing, packaged output may be incomplete.

## Repository layout (build-related)

- Main solution: `src/UniGetUI.sln`
- Main build script: `build_release.cmd`
- Build outputs: `output\`
- Intermediate publish payload: `phison_bin\`

## Quick build (recommended)

From repository root:

```bat
build_release.cmd
```

The script will:

1. Read branding settings from `src/AppBranding.props`
2. Run branding/version scripts
3. Execute tests: `dotnet test src/UniGetUI.sln`
4. Publish Release x64 build
5. Assemble runtime payload into `phison_bin\`
6. Generate integrity metadata and zip package in `output\`
7. Optionally create installer with Inno Setup
8. Optionally build MSIX package

## Minimal dev build (faster iteration)

If you only need to verify compile/run without packaging:

```powershell
dotnet restore src/UniGetUI.sln
dotnet build src/UniGetUI.sln -c Release
dotnet publish src/UniGetUI/UniGetUI.csproj -c Release -p:Platform=x64
```

## Build outputs

After a successful `build_release.cmd` run:

- `output\<AppExecutableName>.x64.zip` (portable package)
- `output\<AppExecutableName>.Installer.exe` (if Inno Setup is installed)
- `output\msix\PhisonUniGet.msix` (if MSIX flow is enabled and successful)

## Common issues

- `pwsh` not found: install PowerShell 7 and ensure it is in `PATH`.
- `python` not found: install Python 3 and ensure it is in `PATH`.
- `7z` not found: install 7-Zip and add it to `PATH`.
- Inno installer skipped: install Inno Setup 6 to `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe` or update the script path.
