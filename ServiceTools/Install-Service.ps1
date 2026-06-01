#Requires -RunAsAdministrator
<#
.SYNOPSIS
    RidocImageAPI を Windows サービスとしてインストールします。

.DESCRIPTION
    ・サービス登録（sc.exe）
    ・Windows イベントビューアーのソース登録
    ・サービス失敗時の自動再起動設定（1回目:1分後、2回目:2分後、3回目以降:5分後）
    ・サービス開始

.PARAMETER ExePath
    RidocImageAPI.exe の完全パス。省略時は本スクリプトと同じディレクトリを使用。

.EXAMPLE
    .\Install-Service.ps1
    .\Install-Service.ps1 -ExePath "D:\Apps\RidocImageAPI\RidocImageAPI.exe"
#>
param(
    [string]$ExePath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ServiceName    = "RidocImageAPI"
$ServiceDisplay = "RidocImageAPI - 図面画像取得 API"
$ServiceDesc    = "Ridoc Smart Navigator SDK を使用して図面画像を HTTP で提供するサービス。"
$EventLogSource = "RidocImageAPI"

# exe パスの解決
if ([string]::IsNullOrEmpty($ExePath)) {
    $ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $ExePath   = Join-Path $ScriptDir "RidocImageAPI.exe"
}

if (-not (Test-Path $ExePath)) {
    Write-Error "実行ファイルが見つかりません: $ExePath"
    exit 1
}

Write-Host "=== RidocImageAPI サービスインストール ===" -ForegroundColor Cyan
Write-Host "ExePath: $ExePath"

# ── イベントビューアーソース登録 ─────────────────────────────────────────
Write-Host "`n[1/4] イベントビューアーのソース登録..."
try {
    if (-not [System.Diagnostics.EventLog]::SourceExists($EventLogSource)) {
        [System.Diagnostics.EventLog]::CreateEventSource($EventLogSource, "Application")
        Write-Host "  ソース '$EventLogSource' を登録しました。" -ForegroundColor Green
    } else {
        Write-Host "  ソース '$EventLogSource' は既に登録済みです。" -ForegroundColor Yellow
    }
} catch {
    Write-Warning "イベントビューアーソースの登録に失敗しました: $_"
}

# ── 既存サービスの停止・削除 ────────────────────────────────────────────
Write-Host "`n[2/4] 既存サービスの確認..."
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "  既存サービスを停止・削除します..." -ForegroundColor Yellow
    if ($existing.Status -eq "Running") {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
    Write-Host "  削除完了。" -ForegroundColor Green
}

# ── サービス登録 ────────────────────────────────────────────────────────
Write-Host "`n[3/4] サービス登録..."
$binPath = "`"$ExePath`""
sc.exe create $ServiceName `
    binPath= $binPath `
    DisplayName= $ServiceDisplay `
    start= auto `
    obj= "LocalSystem" | Out-Null

sc.exe description $ServiceName $ServiceDesc | Out-Null

# ── 失敗時の自動再起動設定 ──────────────────────────────────────────────
# reset= 86400 : 24時間後にリセットカウンター
# actions= restart/60000/restart/120000/restart/300000
#   1回目クラッシュ: 60秒後に再起動
#   2回目クラッシュ: 120秒後に再起動
#   3回目以降     : 300秒後に再起動
sc.exe failure $ServiceName `
    reset= 86400 `
    actions= restart/60000/restart/120000/restart/300000 | Out-Null

Write-Host "  サービス登録完了。" -ForegroundColor Green

# ── サービス開始 ────────────────────────────────────────────────────────
Write-Host "`n[4/4] サービス開始..."
Start-Service -Name $ServiceName
Start-Sleep -Seconds 3

$status = (Get-Service -Name $ServiceName).Status
if ($status -eq "Running") {
    Write-Host "  サービスが正常に起動しました。Status=$status" -ForegroundColor Green
} else {
    Write-Warning "  サービスの起動に失敗した可能性があります。Status=$status"
    Write-Host "  イベントビューアーで詳細を確認してください。" -ForegroundColor Yellow
}

Write-Host "`n=== インストール完了 ===" -ForegroundColor Cyan
Write-Host "サービス名: $ServiceName"
Write-Host "状態: $status"
Write-Host "ヘルスチェック: https://localhost:5088/healthz"
