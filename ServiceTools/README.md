# RidocImageAPI Windows サービス 運用手順

## 必要条件
- Windows Server 2019 以降（または Windows 10/11）
- .NET 9.0 Runtime（`dotnet-runtime-9.0.x-win-x64.exe`）
- 管理者権限での PowerShell 実行

---

## サービスのインストール

```powershell
# 1. Release ビルド（VS2022 または dotnet publish）
dotnet publish RidocImageAPI.csproj -c Release -r win-x64 --no-self-contained -o publish\

# 2. SDK DLL を publish\ にコピー
Copy-Item bin\*.dll publish\

# 3. インストールスクリプト実行（管理者 PowerShell）
cd publish\
..\ServiceTools\Install-Service.ps1

# または exeパス指定
..\ServiceTools\Install-Service.ps1 -ExePath "C:\Services\RidocImageAPI\RidocImageAPI.exe"
```

---

## サービスの状態確認

```powershell
.\ServiceTools\Service-Status.ps1

# または手動
Get-Service RidocImageAPI
Invoke-RestMethod https://localhost:5088/healthz -SkipCertificateCheck
Invoke-RestMethod https://localhost:5088/healthz/ready -SkipCertificateCheck
```

---

## 更新デプロイ

```powershell
# ビルド → 停止 → デプロイ → 再起動 を一括実行
.\ServiceTools\Publish-Release.ps1
```

---

## 開発PCからリモートサーバーへ更新デプロイ（Deploy-Remote.ps1）

開発PC（VS2022 / .NET 9 SDK あり）でビルドし、192.168.1.207 のサービスへ配置する。
WinRM 不要（`sc.exe` と管理共有 `\\host\C$` のみ）。appsettings*.json と logs は上書きしない。

```powershell
# 既定: 192.168.1.207 / RidocImageAPI / port 5087 / 確認用 docId=F42186A01
.\ServiceTools\Deploy-Remote.ps1

# 権限エラー時は先に管理共有へ接続
net use \\192.168.1.207\C$ /user:<ユーザー> *

# 戻す（デプロイ完了時に表示されるタイムスタンプを指定）
.\ServiceTools\Deploy-Remote.ps1 -Rollback 20261019113000
```

---

## サービスのアンインストール

```powershell
.\ServiceTools\Uninstall-Service.ps1
```

---

## ログの場所

| 種別 | 場所 |
|---|---|
| ファイルログ | `{exeの場所}\logs\ridocapi-YYYYMMDD.log` |
| Windows イベントビューアー | アプリケーションログ → ソース: `RidocImageAPI` |

ファイルログは **1日1ファイル** のローリング（最大100MB、31日分保持）。

---

## フォールトトレランス設定

サービスがクラッシュした場合の自動再起動設定（`Install-Service.ps1` で自動設定）：

| クラッシュ回数 | 再起動までの待機時間 |
|---|---|
| 1回目 | 60秒後 |
| 2回目 | 120秒後 |
| 3回目以降 | 300秒後（5分後） |
| リセット | 24時間後にカウンターリセット |

---

## ヘルスチェックエンドポイント

| URL | 内容 |
|---|---|
| `GET /healthz` | API プロセスの生死確認 |
| `GET /healthz/ready` | RSN サーバーへの疎通確認 |

---

## 未処理例外の対処

グローバル例外ハンドラーが以下を実装：

- `AppDomain.UnhandledException` → FATAL ログ出力後に終了（サービスが自動再起動）
- `TaskScheduler.UnobservedTaskException` → ERROR ログ出力（プロセスクラッシュを防止）
- ミドルウェアレベルの未キャッチ例外 → 500 JSON レスポンス + ERROR ログ
