#Requires -RunAsAdministrator
<#
.SYNOPSIS
    RidocImageAPI サービスをアンインストールします。
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ServiceName    = "RidocImageAPI"
$EventLogSource = "RidocImageAPI"

Write-Host "=== RidocImageAPI サービスアンインストール ===" -ForegroundColor Cyan

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -eq "Running") {
        Write-Host "サービスを停止中..."
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 3
    }
    Write-Host "サービスを削除中..."
    sc.exe delete $ServiceName | Out-Null
    Write-Host "削除完了。" -ForegroundColor Green
} else {
    Write-Host "サービス '$ServiceName' は存在しません。" -ForegroundColor Yellow
}

# イベントビューアーソース削除（任意）
$removeEventSource = Read-Host "イベントビューアーのソースも削除しますか？ (y/N)"
if ($removeEventSource -eq "y") {
    try {
        if ([System.Diagnostics.EventLog]::SourceExists($EventLogSource)) {
            [System.Diagnostics.EventLog]::DeleteEventSource($EventLogSource)
            Write-Host "ソース '$EventLogSource' を削除しました。" -ForegroundColor Green
        }
    } catch {
        Write-Warning "イベントソースの削除に失敗: $_"
    }
}

Write-Host "=== アンインストール完了 ===" -ForegroundColor Cyan
