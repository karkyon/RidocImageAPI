#Requires -RunAsAdministrator
<#
.SYNOPSIS
    RidocImageAPI サービスの状態・ログを確認します。
#>
$ServiceName = "RidocImageAPI"

Write-Host "=== RidocImageAPI サービス状態 ===" -ForegroundColor Cyan

# サービス状態
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    $color = if ($svc.Status -eq "Running") { "Green" } else { "Red" }
    Write-Host "状態: $($svc.Status)" -ForegroundColor $color
    Write-Host "スタートタイプ: $($svc.StartType)"
} else {
    Write-Host "サービス '$ServiceName' は登録されていません。" -ForegroundColor Red
}

# ヘルスチェック
Write-Host "`n[ヘルスチェック]"
try {
    $response = Invoke-RestMethod -Uri "https://localhost:5088/healthz" `
        -SkipCertificateCheck -TimeoutSec 5
    Write-Host "  /healthz       : $($response.status)" -ForegroundColor Green
} catch {
    Write-Host "  /healthz       : 応答なし ($_)" -ForegroundColor Red
}
try {
    $response = Invoke-RestMethod -Uri "https://localhost:5088/healthz/ready" `
        -SkipCertificateCheck -TimeoutSec 10
    Write-Host "  /healthz/ready : $($response.status)" -ForegroundColor Green
} catch {
    Write-Host "  /healthz/ready : 応答なし ($_)" -ForegroundColor Red
}

# 最新ログ（直近20行）
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$logDir    = Join-Path $scriptDir "logs"
if (Test-Path $logDir) {
    $latest = Get-ChildItem $logDir -Filter "ridocapi-*.log" |
              Sort-Object LastWriteTime -Descending |
              Select-Object -First 1
    if ($latest) {
        Write-Host "`n[最新ログ: $($latest.Name)]"
        Get-Content $latest.FullName -Tail 20
    }
} else {
    Write-Host "`nlogs ディレクトリが見つかりません: $logDir" -ForegroundColor Yellow
}
