# RidocImageAPI — HANDOFF 資料 / 仕様設計書 / API 設計書

> 作成日: 2026-06-02  
> バージョン: v2.0.0  
> 作成者: 開発セッション記録

---

## 目次

1. [プロジェクト概要](#1-プロジェクト概要)
2. [システム構成](#2-システム構成)
3. [環境要件](#3-環境要件)
4. [ソリューション構成](#4-ソリューション構成)
5. [API 仕様設計書](#5-api-仕様設計書)
6. [サービス仕様設計書](#6-サービス仕様設計書)
7. [テスターアプリ仕様](#7-テスターアプリ仕様)
8. [導入手順](#8-導入手順)
9. [運用手順](#9-運用手順)
10. [フォールトトレランス設計](#10-フォールトトレランス設計)
11. [ログ設計](#11-ログ設計)
12. [セッション作業内容記録](#12-セッション作業内容記録)

---

## 1. プロジェクト概要

### 目的

RICOH Ridoc Smart Navigator SDK V2 を使用して、図面管理システム（RSN サーバー）から図面画像を取得し、HTTP API として外部アプリケーションへ提供するサービス。

### 背景

- 既存システムは SDK を直接呼び出し、ローカルディスクに一時ファイルを書き出してから配信していた
- 本プロジェクトはディスク I/O を廃止し、`MemoryStream` 経由でメモリ上で完結する実装に刷新
- Windows サービスとして常駐稼働し、他システムから HTTP で利用可能

### 主要機能

| 機能 | 説明 |
|---|---|
| サムネイル取得 | 指定図番の JPEG サムネイルを返す |
| オリジナル取得 | 指定図番のオリジナルファイル（TIFF/DXF 等）を返す |
| 複数候補一覧 | キーワードにヒットする図面の候補一覧を JSON で返す |
| インデックス指定取得 | 複数ヒット時に特定のインデックスを指定して取得 |
| ページング取得 | offset/count でページングしながら複数画像を取得 |
| ヘルスチェック | API 生死確認・RSN 疎通確認エンドポイント |

---

## 2. システム構成

```
┌─────────────────────────────────────────────────────────────────┐
│  クライアントアプリ群                                             │
│  ・既存システム（図面管理 等）                                    │
│  ・RidocImageAPITester（本プロジェクト内テストアプリ）            │
└────────────────────────┬────────────────────────────────────────┘
                         │ HTTP (port 5087)
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│  Windows Server 2019  (192.168.1.207)                           │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ RidocImageAPI  (Windows サービス)                        │   │
│  │ ASP.NET Core 9.0 Web API                                │   │
│  │  ├── DrawingImageController   /v1/DrawingImage          │   │
│  │  ├── DrawingImagesController  /v1/DrawingImages         │   │
│  │  ├── RsnImageService          SDK ラッパー              │   │
│  │  └── RsnConnectivityCheck     ヘルスチェック            │   │
│  └─────────────────────────────────────────────────────────┘   │
└────────────────────────┬────────────────────────────────────────┘
                         │ HTTP SDK (port 8080)
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│  RSN サーバー  (192.168.1.5:8080)                               │
│  Ridoc Smart Navigator V2                                       │
└─────────────────────────────────────────────────────────────────┘
```

---

## 3. 環境要件

### 本番サーバー（Windows Server 2019）

| 項目 | 要件 |
|---|---|
| OS | Windows Server 2019 以降（Windows 10/11 でも動作可） |
| ランタイム | ASP.NET Core Runtime 9.0.x (x64) |
| .NET Runtime | .NET Runtime 9.0.x (x64) |
| ディスク空き容量 | 最低 500 MB（ログローリング含む） |
| ネットワーク | RSN サーバー（192.168.1.5:8080）への HTTP 通信 |
| ポート | 5087（インバウンド開放必須） |
| 権限 | サービス登録に管理者権限が必要 |

### 開発環境（Windows 11）

| 項目 | 要件 |
|---|---|
| OS | Windows 11 |
| IDE | Visual Studio 2022 (v17.14+) |
| SDK | .NET 9.0 SDK (win-x64) |
| フレームワーク | .NET 9.0-windows（テスターは WinForms） |

### RSN SDK 依存 DLL（`bin\` フォルダに配置）

| ファイル | 用途 |
|---|---|
| `RsnSDK.dll` | Ridoc Smart Navigator SDK 本体 |
| `RsnRestSDK.dll` | RSN REST 通信ライブラリ |
| `RestSharp.dll` | HTTP クライアント（SDK 依存） |
| `Newtonsoft.Json.dll` | JSON シリアライズ（SDK 依存） |

---

## 4. ソリューション構成

```
RidocImageAPI.sln
├── RidocImageAPI/                          # ASP.NET Core Web API
│   ├── Controllers/
│   │   ├── DrawingImageController.cs       # 単体取得エンドポイント（既存互換）
│   │   └── DrawingImagesController.cs      # 複数取得エンドポイント（新規）
│   ├── Models/
│   │   ├── RsnServerSettings.cs            # RSN 接続設定モデル
│   │   ├── ApiErrorResponse.cs             # エラーレスポンスモデル
│   │   ├── DrawingImages.cs                # 旧モデル（互換用）
│   │   ├── DrawingImageListItem.cs         # 候補一覧アイテムモデル
│   │   └── DrawingImageSearchResponse.cs   # 候補一覧レスポンスモデル
│   ├── Services/
│   │   ├── IRsnImageService.cs             # サービスインターフェース + ImageResult
│   │   ├── RsnImageService.cs              # SDK 呼び出し実装
│   │   └── RsnConnectivityCheck.cs         # ヘルスチェック実装
│   ├── Properties/
│   │   └── launchSettings.json             # 開発時ポート設定
│   ├── Program.cs                          # エントリーポイント・DI・ミドルウェア
│   ├── appsettings.json                    # 本番設定
│   ├── appsettings.Development.json        # 開発設定（タイムスタンプ付きログ）
│   └── RidocImageAPI.csproj
│
├── RidocImageAPITester/                    # WinForms テストアプリ
│   ├── ApiClient.cs                        # HTTP 通信ロジック
│   ├── MainForm.cs                         # UI ロジック
│   ├── MainForm.Designer.cs                # UI レイアウト
│   ├── Program.cs                          # エントリーポイント
│   └── Properties/Settings.cs             # ベース URL 永続化
│
└── ServiceTools/                           # Windows サービス運用スクリプト
    ├── Install-Service.ps1                 # インストール
    ├── Uninstall-Service.ps1               # アンインストール
    ├── Service-Status.ps1                  # 状態確認
    ├── Publish-Release.ps1                 # ビルド & デプロイ
    └── README.md                           # 運用手順書
```

---

## 5. API 仕様設計書

### 共通仕様

| 項目 | 値 |
|---|---|
| ベース URL（開発） | `https://localhost:5088` |
| ベース URL（本番） | `http://192.168.1.207:5087` |
| API バージョン | v1 |
| 認証 | なし（内部ネットワーク前提） |
| 文字コード | UTF-8 |
| Swagger UI | 開発環境のみ `/swagger` で公開 |

### エラーレスポンス共通フォーマット

```json
{
  "message": "エラーメッセージ",
  "errorKey": "error.http.401",
  "detail": "スタックトレース（Development 環境のみ）"
}
```

---

### エンドポイント一覧

#### 【既存・互換維持】単体画像取得

```
GET /v1/DrawingImage
```

**説明**  
指定キーワードで文書を検索し、**検索結果の先頭1件**の画像を返す。  
複数ヒット時は先頭文書を使用（インデックス指定が必要な場合は `/v1/DrawingImages` を使用）。

**クエリパラメーター**

| パラメーター | 型 | 必須 | 説明 |
|---|---|---|---|
| `docId` | string | ✅ | 検索キーワード（図番・文書名など） |
| `imgType` | string | ✅ | `TN`（サムネイル）または `ORG`（オリジナル） |

**レスポンス**

| ステータス | Content-Type | 説明 |
|---|---|---|
| 200 | `image/jpeg` | TN: 常に JPEG |
| 200 | `image/tiff` | ORG: TIFF ファイルの場合 |
| 200 | `application/dxf` | ORG: DXF ファイルの場合 |
| 200 | `application/octet-stream` | ORG: 不明な拡張子 |
| 400 | `application/json` | パラメーター不足・不正 |
| 404 | `application/json` | 文書が見つからない |
| 500 | `application/json` | サーバー内部エラー |
| 503 | `application/json` | RSN サーバー接続エラー |

**レスポンスヘッダー**

| ヘッダー | 例 | 説明 |
|---|---|---|
| `Content-Disposition` | `inline; filename="A15086A02_TN.jpg"` | ファイル名 |
| `Content-Type` | `image/tiff` | 実際のファイル形式 |

**curl 例**

```bash
# サムネイル取得
curl -X GET "http://192.168.1.207:5087/v1/DrawingImage?docId=A15086A02&imgType=TN" \
     -o thumbnail.jpg

# オリジナル取得
curl -X GET "http://192.168.1.207:5087/v1/DrawingImage?docId=A15086A02&imgType=ORG" \
     -o original.tif
```

---

#### 【新規】候補一覧取得

```
GET /v1/DrawingImages/search
```

**説明**  
キーワードにヒットする文書の一覧を JSON で返す。バイナリは含まない。  
複数候補がある場合に `index` を確認してから画像取得に使用する。

**クエリパラメーター**

| パラメーター | 型 | 必須 | 説明 |
|---|---|---|---|
| `docId` | string | ✅ | 検索キーワード |

**レスポンス（200 OK）**

```json
{
  "docId": "AU85-00",
  "totalCount": 7,
  "candidates": [
    {
      "index": 0,
      "id": "_1009470002_3_1009470002_66503",
      "name": "AU85-0038-002",
      "sectionCount": 1,
      "extension": ".TIF",
      "size": 161095
    },
    {
      "index": 1,
      "id": "_1009470002_3_1009470002_91819",
      "name": "AU85-0038-102(SUS316L)",
      "sectionCount": 1,
      "extension": ".tif",
      "size": 101517
    }
  ]
}
```

---

#### 【新規】複数画像取得（インデックス指定 / ページング / 全件）

```
GET /v1/DrawingImages
```

**説明**  
`index` 指定時は単体バイナリ、未指定時は `multipart/mixed` で複数バイナリを返す。

**クエリパラメーター**

| パラメーター | 型 | 必須 | デフォルト | 説明 |
|---|---|---|---|---|
| `docId` | string | ✅ | - | 検索キーワード |
| `imgType` | string | ✅ | - | `TN` または `ORG` |
| `index` | integer | - | null | 指定時: 単体バイナリ返却 |
| `offset` | integer | - | 0 | 取得開始インデックス |
| `count` | integer | - | 0 | 取得件数（0 = 全件） |

**動作パターン**

| パターン | URL 例 | レスポンス |
|---|---|---|
| インデックス指定1件 | `?docId=AU85-00&imgType=TN&index=2` | `image/jpeg` 単体 |
| 全件取得 | `?docId=AU85-00&imgType=TN` | `multipart/mixed` |
| ページング | `?docId=AU85-00&imgType=TN&offset=0&count=3` | `multipart/mixed` |

**multipart/mixed レスポンス構造**

```
Content-Type: multipart/mixed; boundary=ridoc-image-{uuid}

--ridoc-image-{uuid}
Content-Type: image/jpeg
Content-Disposition: inline; filename="AU85-0038-002_TN.jpg"
X-Document-Name: AU85-0038-002
X-Document-Index: 0
Content-Length: 8157

{バイナリデータ}

--ridoc-image-{uuid}
Content-Type: image/jpeg
Content-Disposition: inline; filename="AU85-0038-102(SUS316L)_TN.jpg"
X-Document-Name: AU85-0038-102(SUS316L)
X-Document-Index: 1
Content-Length: 7838

{バイナリデータ}

--ridoc-image-{uuid}--
```

**multipart 取得時の追加レスポンスヘッダー**

| ヘッダー | 説明 |
|---|---|
| `X-Document-Name` | 実際の文書名（RSN 上の名前） |
| `X-Document-Index` | 検索結果内のインデックス（0始まり） |

---

#### ヘルスチェック

```
GET /healthz
GET /healthz/ready
```

| エンドポイント | 確認内容 | 正常ステータス |
|---|---|---|
| `/healthz` | API プロセスの生死確認 | `Healthy` |
| `/healthz/ready` | RSN サーバーへの疎通確認 | `Healthy` / `Degraded` |

**レスポンス例**

```json
{
  "status": "Healthy",
  "elapsed": 0.5374,
  "entries": {
    "self": {
      "status": "Healthy",
      "description": "API プロセスは正常です。",
      "duration": 0.4228,
      "exception": null
    }
  }
}
```

> **注意:** `/healthz/ready` の `Degraded` は RSN SDK の仕様上、Connect → Disconnect の疎通チェックで 403 が返ることが多い。これは SDK が ReadSectionData 後にセッションを自動クローズするため。実際の API は正常動作している。

---

### Content-Type 決定ロジック

```
imgType = TN
  → 常に image/jpeg（SDK が内部で JPEG 変換）

imgType = ORG
  → RsnSection.extension から決定
     .jpg / .jpeg  → image/jpeg
     .png          → image/png
     .tif / .tiff  → image/tiff
     .pdf          → application/pdf
     .dxf          → application/dxf
     .dwg          → application/acad
     .svg          → image/svg+xml
     その他 / 不明 → application/octet-stream
```

---

## 6. サービス仕様設計書

### サービス基本情報

| 項目 | 値 |
|---|---|
| サービス名 | `RidocImageAPI` |
| 表示名 | `RidocImageAPI` |
| 実行ユーザー | `LocalSystem` |
| スタートアップ種別 | 自動 |
| 実行ファイル | `E:\Services\RidocImageAPI\RidocImageAPI.exe` |

### appsettings.json（本番）

```json
{
  "Urls": "http://0.0.0.0:5087",
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "RidocImageAPI": "Information"
    }
  },
  "AllowedHosts": "*",
  "RsnServer": {
    "Url": "http://192.168.1.5:8080/rsn/",
    "User": "imotoseiki",
    "Password": "0750",
    "DocumentTypeId": "f94711dd-b737-49ba-b68a-bc5cef424019"
  }
}
```

### RSN 接続設定（RsnServerSettings）

| プロパティ | 説明 |
|---|---|
| `Url` | RSN サーバーの URL（末尾スラッシュ必須） |
| `User` | 認証ユーザー名 |
| `Password` | 認証パスワード |
| `DocumentTypeId` | 検索対象の文書タイプ ID（null で全タイプ） |

### DI 構成

| サービス | ライフタイム | 理由 |
|---|---|---|
| `IRsnImageService` | Transient | リクエストごとに RSN に接続・切断する設計のため |
| `RsnConnectivityCheck` | Transient | ヘルスチェックごとに接続テスト |

---

## 7. テスターアプリ仕様

### 概要

`RidocImageAPITester` は RidocImageAPI の動作確認用 Windows Forms アプリケーション。

### 主要機能

| 機能 | 説明 |
|---|---|
| 単体画像取得 | docId + imgType を指定して GET `/v1/DrawingImage` |
| 画像プレビュー | JPEG / PNG / TIFF / BMP / GIF をインライン表示 |
| ズーム操作 | Fit / ＋25% / －25% / 倍率表示 |
| スクロール | 拡大時にスクロールで閲覧 |
| ファイル保存 | 拡張子に応じたダイアログ保存 |
| 外部アプリで開く | 一時フォルダーに保存して既定アプリ起動 |
| リクエスト履歴 | 直近 50 件表示。ダブルクリックで入力欄に反映 |
| ログパネル | タイムスタンプ付き。コピー・クリア対応 |
| キャンセル | 通信中にキャンセル可能 |
| ベース URL 永続化 | アプリ終了後も設定保持 |
| F5 / Enter | キーボードショートカット送信 |

### ApiClient.cs 主要クラス

```csharp
// 単体取得
Task<FetchResult> GetImageAsync(string docId, string imgType, CancellationToken ct)

// 候補一覧取得
Task<DrawingImageSearchResponse?> SearchAsync(string docId, CancellationToken ct)

// インデックス指定取得
Task<FetchResult> GetImageByIndexAsync(string docId, string imgType, int index, CancellationToken ct)

// 複数取得（AsyncStream）
IAsyncEnumerable<MultiFetchItem> GetImagesAsync(string docId, string imgType, int offset, int count, CancellationToken ct)
```

---

## 8. 導入手順

### 前提条件

- [ ] Windows Server 2019 以降
- [ ] ASP.NET Core Runtime 9.0.x (x64) インストール済み
- [ ] .NET Runtime 9.0.x (x64) インストール済み
- [ ] RSN サーバー（192.168.1.5:8080）への通信が許可されている
- [ ] 管理者権限での作業が可能

### Step 1: ビルド & Publish（開発 PC）

```cmd
cd E:\GitHub\RidocImageAPI\RidocImageAPI

dotnet publish -c Release -r win-x64 --no-self-contained -o E:\Services\RidocImageAPI\

xcopy /Y bin\*.dll E:\Services\RidocImageAPI\
```

### Step 2: appsettings.json 編集（発行先）

`E:\Services\RidocImageAPI\appsettings.json` を以下の内容で上書き：

```json
{
  "Urls": "http://0.0.0.0:5087",
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "RidocImageAPI": "Information"
    }
  },
  "AllowedHosts": "*",
  "RsnServer": {
    "Url": "http://192.168.1.5:8080/rsn/",
    "User": "imotoseiki",
    "Password": "0750",
    "DocumentTypeId": "f94711dd-b737-49ba-b68a-bc5cef424019"
  }
}
```

### Step 3: サーバーへのファイル転送

`E:\Services\RidocImageAPI\` フォルダをサーバーの同パスにコピー。  
（ネットワーク共有 `\\192.168.1.207\E$\Services\RidocImageAPI\` 等を使用）

### Step 4: サービス登録（サーバー管理者 PowerShell）

```powershell
sc.exe create RidocImageAPI `
    binPath= "E:\Services\RidocImageAPI\RidocImageAPI.exe" `
    DisplayName= "RidocImageAPI" `
    start= auto

sc.exe failure RidocImageAPI `
    reset= 86400 `
    actions= restart/60000/restart/120000/restart/300000

Start-Service RidocImageAPI
Get-Service RidocImageAPI
```

### Step 5: ファイアウォール開放（サーバー管理者 PowerShell）

```powershell
New-NetFirewallRule `
    -DisplayName "RidocImageAPI HTTP" `
    -Direction Inbound `
    -Protocol TCP `
    -LocalPort 5087 `
    -Action Allow
```

### Step 6: 動作確認

```powershell
# ヘルスチェック
Invoke-RestMethod http://localhost:5087/healthz

# 画像取得テスト
Invoke-WebRequest "http://localhost:5087/v1/DrawingImage?docId=A15086A02&imgType=TN" `
    -OutFile C:\test_thumbnail.jpg
```

---

## 9. 運用手順

### サービス起動 / 停止 / 再起動

```powershell
Start-Service RidocImageAPI
Stop-Service RidocImageAPI
Restart-Service RidocImageAPI
```

### 状態確認

```powershell
Get-Service RidocImageAPI
Invoke-RestMethod http://localhost:5087/healthz
Invoke-RestMethod http://localhost:5087/healthz/ready | ConvertTo-Json -Depth 5
```

### ログ確認

```powershell
# 最新ログ（直近 50 行）
Get-Content "E:\Services\RidocImageAPI\logs\ridocapi-$(Get-Date -Format 'yyyyMMdd').log" -Tail 50

# エラーのみ抽出
Get-Content "E:\Services\RidocImageAPI\logs\ridocapi-$(Get-Date -Format 'yyyyMMdd').log" | Where-Object { $_ -match "\[ERR\]|\[FTL\]" }
```

### Windows イベントビューアー

アプリケーションログ → ソース: `RidocImageAPI` （Warning 以上）

### 更新デプロイ

```powershell
# 開発 PC でビルド後
Stop-Service RidocImageAPI
# ファイルをサーバーに上書きコピー
Start-Service RidocImageAPI
```

---

## 10. フォールトトレランス設計

### 多層防御構成

| 層 | 実装 | 動作 |
|---|---|---|
| プロセス | `AppDomain.UnhandledException` | FATAL ログ出力後に終了（SCM が自動再起動） |
| タスク | `TaskScheduler.UnobservedTaskException` | ERROR ログ（クラッシュを防止・SetObserved） |
| HTTP | `UseExceptionHandler` ミドルウェア | 500 JSON レスポンス + ERROR ログ |
| サービス | `sc.exe failure` 自動再起動設定 | クラッシュ時に 60→120→300 秒で再起動 |
| SDK | `IsAlreadyClosedError()` | 403 を正常な SDK 挙動として Debug に格下げ |

### サービス自動再起動設定

| クラッシュ回数 | 再起動待機 |
|---|---|
| 1回目 | 60 秒後 |
| 2回目 | 120 秒後 |
| 3回目以降 | 300 秒後（5分後） |
| カウンターリセット | 24時間後 |

### SDK の既知の挙動

```
ReadSectionData() 完了後に Dispose() / Disconnect() を呼ぶと HTTP 403 が返る。
→ SDK がセッションを自動クローズするため（仕様）。
→ IsAlreadyClosedError() で判定し Debug レベルにログ。
→ 画像データは正常に取得済みのため問題なし。
```

---

## 11. ログ設計

### ログ出力先

| 出力先 | 対象レベル | 形式 |
|---|---|---|
| コンソール | Debug 以上（開発）/ Information 以上（本番） | `[HH:mm:ss.fff LVL] メッセージ` |
| ファイル | Debug 以上 | タイムスタンプ + pid + tid 付き |
| イベントビューアー | Warning 以上 | Windows Application ログ |

### ファイルログ設定

| 設定 | 値 |
|---|---|
| パス | `{exeDir}\logs\ridocapi-{日付}.log` |
| ローリング | 1日1ファイル |
| 最大サイズ | 100 MB / ファイル |
| 保持日数 | 31 日 |

### ログプレフィックス規則

| プレフィックス | 意味 |
|---|---|
| `[RSN]` | SDK 呼び出し関連 |
| `[RSN][MULTI]` | 複数取得 SDK 呼び出し |
| `[API]` | コントローラー受信/送信 |
| `[API][MULTI]` | 複数取得コントローラー |
| `[HEALTH]` | ヘルスチェック |
| `[HTTP]` | Serilog アクセスログ |
| `[FATAL]` | 未処理例外 |
| `[MIDDLEWARE]` | ミドルウェアレベル例外 |

### ログ出力例（正常系）

```
[2026-06-02 08:42:15.123 +09:00 INF] [pid:1234 tid:5] [API] リクエスト受信: docId=A15086A02 imgType=TN remoteIp=192.168.1.100
[2026-06-02 08:42:15.234 +09:00 DBG] [pid:1234 tid:5] [RSN] 接続開始: Url=http://192.168.1.5:8080/rsn/ docId=A15086A02 imgType=TN
[2026-06-02 08:42:15.631 +09:00 DBG] [pid:1234 tid:5] [RSN] 接続成功 elapsed=397ms
[2026-06-02 08:42:16.150 +09:00 INF] [pid:1234 tid:5] [RSN] 文書発見: id=_1009... name=A15086A02 size=79822 sections=1 elapsed=916ms
[2026-06-02 08:42:16.200 +09:00 INF] [pid:1234 tid:5] [RSN] 画像読み込み完了: docId=A15086A02 imgType=TN contentType=image/jpeg size=8827bytes ext=.jpg elapsed=966ms
[2026-06-02 08:42:16.210 +09:00 INF] [pid:1234 tid:5] [API] レスポンス送信: docId=A15086A02 imgType=TN contentType=image/jpeg fileName=A15086A02_TN.jpg size=8827bytes
[2026-06-02 08:42:16.220 +09:00 INF] [pid:1234 tid:5] [HTTP] GET /v1/DrawingImage → 200 (1097.000ms)
```

---

## 12. セッション作業内容記録

### 実施日: 2026-06-01 〜 2026-06-02

### フェーズ 1: API 基盤構築・リファクタリング

| No. | 作業 | 詳細 |
|---|---|---|
| 1 | 認証情報外部化 | ハードコードを `IOptions<RsnServerSettings>` に移行 |
| 2 | Dispose/Disconnect 改善 | `try/finally` で確実に実行。403 は `IsAlreadyClosedError()` で格下げ |
| 3 | ディスク廃止 | 一時ファイル書き出し → `MemoryStream` 直書きに変更 |
| 4 | Content-Type 動的決定 | `RsnSection.extension` から MIME を自動決定 |
| 5 | Swagger 整備 | `[Produces]` `[ProducesResponseType]` でレスポンス仕様を宣言 |
| 6 | 全件取得廃止 | `GetDocumentList(0, 1)` で先頭1件のみ取得 |
| 7 | ログ強化 | `[RSN]` / `[API]` プレフィックス、elapsed、sectionExt 等を追加 |
| 8 | `sectionId` → `sectionNo` | RsnSection の正しいプロパティに修正 |
| 9 | `extension` 直接取得 | GetSectionList から `RsnSection.extension` を直接参照 |
| 10 | net7.0 → net9.0 | EOL 対応 |

### フェーズ 2: テスターアプリ開発

| No. | 作業 | 詳細 |
|---|---|---|
| 11 | WinForms テスターアプリ新規作成 | `RidocImageAPITester` プロジェクトをソリューションに追加 |
| 12 | SplitContainer 修正 | `InitializeComponent` 内での MinSize/SplitterDistance 設定を `Load` イベントに移動 |
| 13 | ズーム機能追加 | `PictureBox(Zoom)` → `Panel(AutoScroll) + PictureBox(AutoSize)` に変更 |
| 14 | Fit / ＋ / － ボタン追加 | 倍率表示、ズームイン/アウト、Fit ボタン実装 |

### フェーズ 3: multi エンドポイント開発

| No. | 作業 | 詳細 |
|---|---|---|
| 15 | 候補一覧 API 追加 | `GET /v1/DrawingImages/search` 実装 |
| 16 | インデックス指定取得 API 追加 | `GET /v1/DrawingImages?index=N` 実装 |
| 17 | ページング取得 API 追加 | `GET /v1/DrawingImages?offset=N&count=M` multipart/mixed 実装 |
| 18 | `IAsyncEnumerable` ストリーミング | `GetImagesAsync` を async stream で実装 |
| 19 | テスター側 multi 対応 | `SearchAsync` / `GetImageByIndexAsync` / `GetImagesAsync` 追加 |

### フェーズ 4: Windows サービス化

| No. | 作業 | 詳細 |
|---|---|---|
| 20 | `UseWindowsService()` 追加 | コンソール実行との互換維持 |
| 21 | Serilog 導入 | ファイルローリング・イベントビューアー出力 |
| 22 | ヘルスチェック実装 | `/healthz` / `/healthz/ready` エンドポイント |
| 23 | グローバル例外ハンドラー | `UnhandledException` / `UnobservedTaskException` |
| 24 | 運用スクリプト作成 | Install / Uninstall / Status / Publish-Release |
| 25 | `csproj` 変更 | `net9.0` → `net9.0-windows`、各種パッケージ追加 |

### フェーズ 5: Windows Server 2019 デプロイ

| No. | 作業 | 詳細 |
|---|---|---|
| 26 | ASP.NET Core Runtime 9.0.16 インストール | サーバーに x64 インストール |
| 27 | .NET Runtime 9.0.16 インストール | 不足していたためインストール |
| 28 | dotnet publish | `E:\Services\RidocImageAPI\` に発行 |
| 29 | SDK DLL コピー | `xcopy /Y bin\*.dll` で転送 |
| 30 | サービス登録 | `sc.exe create` で登録・自動再起動設定 |
| 31 | ファイアウォール開放 | ポート 5087 のインバウンドルール追加 |
| 32 | 本番稼働確認 | `GET /healthz` → Healthy 確認 ✅ |

### 解決したビルドエラー一覧

| エラー | 原因 | 解決策 |
|---|---|---|
| `sectionId` 存在しない | RsnSection に `sectionId` はない | `sectionNo` / `extension` に変更 |
| `CS1513 } が必要` | ReadImageToMemoryStream の閉じ括弧不足 | 3つの `}` に修正 |
| `CS7064 app.ico 不在` | csproj の ApplicationIcon 参照 | `<ApplicationIcon>` 行を削除 |
| `RollingInterval` 名前空間誤り | `Serilog.Sinks.File.RollingInterval` → 存在しない | `Serilog.RollingInterval`（`using Serilog` で解決） |
| `TaskScheduler` 未定義 | `using System.Threading.Tasks` 不足 | using 追加 |
| `long → int` 変換不可 | `sectionCount` が `long` | `DrawingImageListItem.SectionCount` を `long` に変更 |
| `ArgumentOutOfRangeException` catch 順序 | `ArgumentException` のサブクラスを後に書いた | サブクラスを先に catch |
| `Serialize` 呼び出し不適切 | `new()` が `JsonTypeInfo<T>` と `JsonSerializerOptions` で曖昧 | `new JsonSerializerOptions` と明示 |
| `SplitterDistance` 例外 | `InitializeComponent` 内でフォームサイズ未確定 | `Load` イベントに移動・MinSize も Load で設定 |

---

## 付録 A: 技術スタック

| 種別 | 使用技術 |
|---|---|
| API フレームワーク | ASP.NET Core 9.0 |
| ターゲット | net9.0-windows (win-x64) |
| SDK | Ridoc Smart Navigator SDK V2 |
| ログ | Serilog + File Sink + EventLog Sink |
| ヘルスチェック | Microsoft.AspNetCore.Diagnostics.HealthChecks |
| API ドキュメント | Swashbuckle.AspNetCore 6.9.0 |
| サービスホスト | Microsoft.Extensions.Hosting.WindowsServices |
| テスター UI | Windows Forms (.NET 9.0-windows) |

## 付録 B: ポート一覧

| ポート | プロトコル | 用途 |
|---|---|---|
| 5087 | HTTP | 本番 API（外部公開） |
| 5088 | HTTPS | 開発 API（ローカルのみ） |
| 8080 | HTTP | RSN サーバー（192.168.1.5） |

## 付録 C: 関連ファイルパス（本番サーバー）

| パス | 内容 |
|---|---|
| `E:\Services\RidocImageAPI\` | アプリケーション本体 |
| `E:\Services\RidocImageAPI\appsettings.json` | 本番設定 |
| `E:\Services\RidocImageAPI\logs\` | ローリングログ |
| `C:\Program Files\dotnet\` | .NET ランタイム |