#Requires -RunAsAdministrator
<#
.SYNOPSIS
    RidocImageAPI を Release ビルドしてサービスを再起動します。

.DESCRIPTION
    1. サービス停止
    2. dotnet publish（win-x64, Release, self-contained=false）
    3. SDK DLL のコピー（bin/ → publish/）
    4. サービス再起動
#>
param(
    [string]$SolutionDir = "",
    [string]$PublishDir  = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ServiceName = "RidocImageAPI"

if ([string]::IsNullOrEmpty($SolutionDir)) {
    $SolutionDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
}
$ProjectDir = Join-Path $SolutionDir "RidocImageAPI"

if ([string]::IsNullOrEmpty($PublishDir)) {
    $PublishDir = Join-Path $ProjectDir "publish"
}

Write-Host "=== RidocImageAPI Release ビルド & デプロイ ===" -ForegroundColor Cyan
Write-Host "ProjectDir : $ProjectDir"
Write-Host "PublishDir : $PublishDir"

# [1] サービス停止
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -eq "Running") {
    Write-Host "`n[1/4] サービス停止..."
    Stop-Service -Name $ServiceName -Force
    Start-Sleep -Seconds 3
}

# [2] dotnet publish
Write-Host "`n[2/4] dotnet publish..."
dotnet publish "$ProjectDir\RidocImageAPI.csproj" `
    -c Release `
    -r win-x64 `
    --no-self-contained `
    -o "$PublishDir"

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish に失敗しました。"
    exit 1
}

# [3] SDK DLL のコピー（bin\ → publish\）
Write-Host "`n[3/4] SDK DLL コピー..."
$binDir = Join-Path $ProjectDir "bin"
if (Test-Path $binDir) {
    Copy-Item "$binDir\*.dll" "$PublishDir\" -Force
    Write-Host "  SDK DLL をコピーしました。" -ForegroundColor Green
} else {
    Write-Warning "bin ディレクトリが見つかりません: $binDir"
}

# [4] サービス再起動
Write-Host "`n[4/4] サービス再起動..."
if ($svc) {
    Start-Service -Name $ServiceName
    Start-Sleep -Seconds 3
    $status = (Get-Service -Name $ServiceName).Status
    $color  = if ($status -eq "Running") { "Green" } else { "Red" }
    Write-Host "  サービス状態: $status" -ForegroundColor $color
}

Write-Host "`n=== デプロイ完了 ===" -ForegroundColor Cyan
