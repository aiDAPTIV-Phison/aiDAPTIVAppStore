# certs_build_flow

本文提供在**新開發環境**中，從憑證複製到執行 build 的完整流程。

## 1) 前置條件

- 可存取共享路徑：``
- 本機已安裝：
  - Windows SDK（含 `signtool.exe`）
  - .NET SDK
  - 7-Zip（若你的 build 流程有用到）
- 具系統管理員權限（安裝器流程有憑證匯入與系統層設定）

## 2) 從共享路徑複製 certs 到專案

目標是將 `certs` 資料夾放到專案根目錄下，結構如下：

```text
AppStore/
  certs/
    aiDAPTIVAppStore.pfx
    aiDAPTIVAppStore.cer
  build_release.cmd
  ...
```

### 方法 A：檔案總管手動複製

1. 開啟 ``
2. 複製 `certs` 資料夾
3. 貼到專案根目錄（例如：`D:\Project\AppStore`）

### 方法 B：PowerShell 複製

在 PowerShell 執行：

```powershell
$src = ""
$dst = "D:\Project\AppStore\certs"
Copy-Item -Path $src -Destination $dst -Recurse -Force
```

## 3) 驗證 certs 檔案是否就位

在專案根目錄執行：

```powershell
Test-Path ".\certs\aiDAPTIVAppStore.pfx"
Test-Path ".\certs\aiDAPTIVAppStore.cer"
```

兩個結果都應該是 `True`。

## 4) 執行 build_release.cmd

在專案根目錄執行：

```powershell
.\build_release.cmd
```

腳本中的簽章相關互動如下：

1. `Do you want to sign the files? [Y/n]:`
   - 請輸入 `Y` 才會進入檢查與簽章流程
2. `Enter PFX password:`
   - 輸入 `aiDAPTIVAppStore.pfx` 的密碼
3. `Do you want to build and sign MSIX package? [Y/n]:`
   - 需要 MSIX 時輸入 `Y`
4. `Enter PFX password for MSIX signing:`
   - 輸入同一份 PFX 密碼

## 5) 失敗時常見檢查點

- `SignTool was not found`
  - 安裝 Windows SDK，確認 `signtool.exe` 可用
- `Certificate file not found`
  - 檢查 `AppStore\certs\aiDAPTIVAppStore.pfx` 是否存在
- `No certificates were found that met all the given criteria`
  - 使用具 `Code Signing` EKU 的憑證（非一般 TLS 憑證）
- 密碼錯誤
  - 重新確認輸入的 PFX 密碼

## 6) 安裝器端憑證匯入行為（已內建）

`installer/offload/AppStore.nsi` 已加入以下行為：

- 安裝時檢查安裝程式同層是否有 `aiDAPTIVAppStore.cer`
- 若存在，執行：

```cmd
certutil -f -addstore "Root" "aiDAPTIVAppStore.cer"
```

把憑證加入「受信任的根憑證授權單位」。

## 7) 安全建議

- `certs/` 已在 `.gitignore`，請勿將 PFX 與密碼提交到版本庫
- 不要在文件、腳本或聊天記錄中明碼保存密碼
- 若憑證外流，請立即更換憑證與密碼
