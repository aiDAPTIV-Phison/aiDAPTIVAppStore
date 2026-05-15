<div align="center" style="border-bottom: none">
    <h1>
        <br>
        aiDAPTIV AppStore
        <br>
        Privacy-First AI App Management
    </h1>
    <a href="https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore/releases"><img src="https://img.shields.io/badge/License-MIT-blue" alt="License"></a>
    <a href="https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore/releases"><img src="https://img.shields.io/badge/Supported_OS-Windows-white" alt="Supported OS"></a>
    <br>
    <blockquote>
    <p>This project is a fork of <a href="https://github.com/marticliment/UniGetUI"><b>UniGetUI</b></a> by Martí Climent, used under the <a href="LICENSE">MIT License</a>. Modifications and enhancements by aiDAPTIV.</p>
    </blockquote>
</div>

[![Downloads@latest](https://img.shields.io/github/downloads/aiDAPTIV-Phison/aiDAPTIVAppStore/latest/total?style=for-the-badge)](https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore/releases)
[![Release Version Badge](https://img.shields.io/github/v/release/aiDAPTIV-Phison/aiDAPTIVAppStore?style=for-the-badge)](https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore/releases)
[![Issues Badge](https://img.shields.io/github/issues/aiDAPTIV-Phison/aiDAPTIVAppStore?style=for-the-badge)](https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore/issues)
[![Closed Issues Badge](https://img.shields.io/github/issues-closed/aiDAPTIV-Phison/aiDAPTIVAppStore?color=%238256d0&style=for-the-badge)](https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore/issues?q=is%3Aissue+is%3Aclosed)<br>
aiDAPTIV AppStore is a Windows desktop app for managing software from [Scoop](https://scoop.sh/), with aiDAPTIV-specific integration for AI app workflows.
It is based on UniGetUI and extended by aiDAPTIV for installer automation, bucket integration, and local AI runtime setup.

**Disclaimer:** This project has no connection with Scoop — it's completely unofficial. Be aware that the developers of aiDAPTIV AppStore are NOT responsible for the downloaded software. Proceed with caution.

> [!CAUTION]
> **The OFFICIAL repository for aiDAPTIV AppStore is [https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore](https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore)**<br>
> **Any other website should be considered unofficial, despite what they may say.**

🔒 Found a security issue? Please open a private security report in [GitHub Security Advisories](https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore/security/advisories)

## Table of contents
 - **[aiDAPTIV AppStore Repository](https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore)**
 - [Table of contents](#table-of-contents)
 - [Installation](#installation)
 - [Build from source](#build-from-source)
 - [Update aiDAPTIV AppStore](#update-aidaptiv-appstore)
 - [Features](#features)
 - [Translating aiDAPTIV AppStore](#translating-aidaptiv-appstore-to-other-languages)
 - [Frequently Asked Questions](#frequently-asked-questions)
 - [Command-line Arguments](https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore/blob/main/cli-arguments.md)

## Installation
<p>Install aiDAPTIV AppStore from the GitHub Releases page.</p>

### Download aiDAPTIV AppStore installer

![GitHub Release](https://img.shields.io/github/v/release/aiDAPTIV-Phison/aiDAPTIVAppStore?style=for-the-badge)
<p align="left"><b><a href="https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore/releases">Click here to download aiDAPTIV AppStore</a></b></p>

### What the installer configures

- Installs/updates aiDAPTIV AppStore to `%LOCALAPPDATA%\aiDAPTIVAppStore`.
- Detects and installs Scoop (if needed), then configures required dependencies.
- Adds `versions` and `aiDAPTIV-bucket` Scoop buckets.
- Installs `git` and `scoop-search` when missing.
- Prompts for an optional KV cache directory and writes it to user env var `PHISON_AIDAPTIV`.
- Supports update mode through installer parameter `/UPDATE`.

## Build from source

For developers who want to build aiDAPTIV AppStore locally, see the full build guide in [BUILD.md](BUILD.md).

Quick start:

1. Install prerequisites from `BUILD.md` (`.NET 8 SDK`, `Python 3`, `PowerShell 7`, `7-Zip`, optional `Inno Setup 6`).
2. From repository root, run:
   `build_release.cmd`
3. Get build artifacts from `output\` (zip package, and installer if Inno Setup is available).

## Update aiDAPTIV AppStore

aiDAPTIV AppStore has a built-in autoupdater. You can also download the latest installer from the [Releases page](https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore/releases) at any time.


## Features

 - Install, update, and remove software from your system using Scoop packages (including `aiDAPTIV-bucket`).
 - Discover new packages and filter them to easily find the package you want.
 - View detailed metadata about any package before installing it. Get the direct download URL or the name of the publisher, as well as the size of the download.
 - Easily bulk-install, update, or uninstall multiple packages at once selecting multiple packages before performing an operation
 - Automatically update packages, or be notified when updates become available. Skip versions or completely ignore updates on a per-package basis.
 - Manage available updates directly from the app interface.
 - The system tray icon will also show the available updates and installed packages, to efficiently update a program or remove a package from your system.
 - Easily customize how and where packages are installed. Select different installation options and switches for each package. Install an older version or force to install a 32 bit architecture. \[But don't worry, those options will be saved for future updates for this package*]
 - Share packages with your friends to show them off that program you found.
 - Export custom lists of packages to then import them to another machine and install those packages with previously specified, custom installation parameters. Setting up machines or configuring a specific software setup has never been easier.
 - Backup your packages to a local file to easily recover your setup in a matter of seconds when migrating to a new machine*
 - Includes aiDAPTIV model/system check configuration files (`aidaptiv_system_check.json`, `aidaptiv_update.json`) used by bundled aiDAPTIV components.

## Translating aiDAPTIV AppStore to other languages

To translate aiDAPTIV AppStore to other languages or to update an old translation, please see [Translations in this repository](https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore/tree/main/src/UniGetUI.Core.LanguageEngine/Assets/Languages) for more info.


## Currently Supported languages

aiDAPTIV AppStore inherits the localization assets from the upstream UniGetUI project, which supports 50+ languages including English, Traditional Chinese, Simplified Chinese, Japanese, Korean, French, German, Spanish, and more. Translator credits belong to the original UniGetUI translation community. See the [upstream translators list](https://github.com/marticliment/UniGetUI?tab=readme-ov-file#currently-supported-languages) for full credits.

## Frequently asked questions

**Q: I am unable to install or upgrade a specific Scoop package! What should I do?**<br>

A: This is likely an issue with Scoop rather than aiDAPTIV AppStore.

Please check if it's possible to install/upgrade the package through PowerShell by using `scoop install <pkg>` or `scoop update <pkg>` directly.

If this doesn't work, consider asking for help at [Scoop's project page](https://github.com/ScoopInstaller/Scoop).<br>

#

**Q: The name of a package is trimmed with ellipsis — how do I see its full name/id?**<br>

A: Hover over the package row to see its full name and ID in the tooltip, or open the package details page.<br>

#

**Q: My antivirus is telling me that aiDAPTIV AppStore is a virus! / My browser is blocking the download of aiDAPTIV AppStore!**<br>

A: A common reason apps (i.e., executables) get blocked and/or detected as a virus — even when there's nothing malicious about them, like in the case of aiDAPTIV AppStore — is because a relatively large amount of people are not using them.

Combine that with the fact that you might be downloading something recently released, and blocking unknown apps is in many cases a good precaution to take to prevent actual malware.

Since aiDAPTIV AppStore is open source and safe to use, whitelist the app in the settings of your antivirus/browser.<br>

#

**Q: Are Scoop packages safe?**<br>

A: aiDAPTIV AppStore and Scoop aren't responsible for the packages available for download, which are provided by third parties and can theoretically be compromised. It's recommended that you only download software from trusted publishers.

<br><p align="center"><i>Check out the <a href="https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore/wiki">Wiki</a> for more information!</i></p>

## Command-line parameters:

Check out the current command-line and deep-link reference [here](https://github.com/aiDAPTIV-Phison/aiDAPTIVAppStore/blob/main/cli-arguments.md).  
Note: some argument names in that document still follow upstream UniGetUI naming.
