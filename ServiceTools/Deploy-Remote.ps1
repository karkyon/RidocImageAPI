<#
.SYNOPSIS
    開発PCで RidocImageAPI を Release 発行し、リモートサーバー(既定 192.168.1.207)の
    Windows サービスへ配置・再起動する。

.DESCRIPTION
    1. git pull → dotnet build（エラーがあれば中止）
    2. リモートのサービス実体パスを sc.exe qc で取得し、管理共有(\\host\C$\...)へ変換
    3. dotnet publish（一時フォルダ）＋ SDK DLL(bin\*.dll) を同梱
    4. 配置先を丸ごとバックアップ（<配置先>_bak_yyyyMMddHHmmss、logs 除く）
    5. サービス停止 → robocopy 配置（appsettings*.json と logs は上書きしない）→ サービス開始
    6. /healthz と /v1/DrawingImages/search で動作確認

    WinRM 不要（sc.exe と管理共有のみ使用）。実行ユーザーにリモートの管理者権限が必要。
    権限が無い場合は先に: net use \\192.168.1.207\C$ /user:<ユーザー> *

.EXAMPLE
    .\ServiceTools\Deploy-Remote.ps1
    .\ServiceTools\Deploy-Remote.ps1 -TestDocId F42186A01
    .\ServiceTools\Deploy-Remote.ps1 -Rollback 20261019113000
#>
param(
    [string]$ComputerName = "192.168.1.207",
    [string]$ServiceName  = "RidocImageAPI",
    [int]   $Port         = 5087,
    [string]$TestDocId    = "F42186A01",
    [string]$Rollback     = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$SolutionDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$ProjectDir  = Join-Path $SolutionDir "RidocImageAPI"
$ProjectFile = Join-Path $ProjectDir  "RidocImageAPI.csproj"
$Remote      = "\\$ComputerName"

function Invoke-Sc([string[]]$ScArgs) {
    $out = & sc.exe $Remote @ScArgs 2>&1
    return @{ Code = $LASTEXITCODE; Text = ($out -join "`n") }
}

function Wait-ServiceState([string]$State, [int]$TimeoutSec = 60) {
    for ($i = 0; $i -lt $TimeoutSec; $i++) {
        $q = Invoke-Sc @("query", $ServiceName)
        if ($q.Text -match "STATE\s*:\s*\d+\s+$State") { return $true }
        Start-Sleep -Seconds 1
    }
    return $false
}

function Stop-RemoteService {
    $q = Invoke-Sc @("query", $ServiceName)
    if ($q.Text -match "STATE\s*:\s*\d+\s+STOPPED") { return }
    Write-Host "サービス停止: $ServiceName @ $ComputerName"
    Invoke-Sc @("stop", $ServiceName) | Out-Null
    if (-not (Wait-ServiceState "STOPPED")) { throw "サービスが停止しません: $ServiceName" }
    Start-Sleep -Seconds 2   # ファイルロック解放待ち
}

function Start-RemoteService {
    Write-Host "サービス開始: $ServiceName @ $ComputerName"
    $r = Invoke-Sc @("start", $ServiceName)
    if ($r.Code -ne 0 -and $r.Text -notmatch "1056") { throw "サービス開始失敗:`n$($r.Text)" }
    if (-not (Wait-ServiceState "RUNNING")) { throw "サービスが RUNNING になりません: $ServiceName" }
}

function Get-RemoteInstallDir {
    $qc = Invoke-Sc @("qc", $ServiceName)
    if ($qc.Code -ne 0) {
        throw "リモートのサービス情報を取得できません（権限・名前・ネットワークを確認）:`n$($qc.Text)"
    }
    $m = [regex]::Match($qc.Text, 'BINARY_PATH_NAME\s*:\s*"?([A-Za-z]):\\([^"\r\n]+?\.exe)')
    if (-not $m.Success) { throw "実行ファイルパスを解析できません:`n$($qc.Text)" }
    $exeRel = $m.Groups[2].Value
    $dir    = Split-Path -Parent $exeRel
    return @{
        Local = "$($m.Groups[1].Value):\$dir"
        Unc   = "$Remote\$($m.Groups[1].Value)`$\$dir"
    }
}

function Test-Remote {
    $base = "http://${ComputerName}:$Port"
    Write-Host "`n=== 動作確認 $base ===" -ForegroundColor Cyan
    try {
        $h = Invoke-RestMethod "$base/healthz" -TimeoutSec 15
        Write-Host "healthz: $($h.status)" -ForegroundColor Green
    } catch { Write-Warning "healthz 失敗: $($_.Exception.Message)" }

    if ([string]::IsNullOrWhiteSpace($TestDocId)) { return }
    try {
        $r = Invoke-RestMethod "$base/v1/DrawingImages/search?docId=$([uri]::EscapeDataString($TestDocId))" -TimeoutSec 60
        Write-Host "docId=$TestDocId  totalCount(文書名一致)=$($r.totalCount)  keywordHitCount(照合前)=$($r.keywordHitCount)"
        if ($r.candidates) { $r.candidates | Format-Table index, name, extension, size -AutoSize | Out-String | Write-Host }
    } catch { Write-Warning "search 失敗: $($_.Exception.Message)" }
}

# ═════════════════════════════════════════════════════════════════════════════
$dirs = Get-RemoteInstallDir
Write-Host "配置先: $($dirs.Local)  ($($dirs.Unc))"
if (-not (Test-Path $dirs.Unc)) {
    throw "配置先にアクセスできません: $($dirs.Unc)`n先に net use $Remote\C`$ /user:<ユーザー> * を実行してください。"
}

# ── ロールバック ─────────────────────────────────────────────────────────────
if ($Rollback) {
    $bak = "$($dirs.Unc)_bak_$Rollback"
    if (-not (Test-Path $bak)) { throw "バックアップがありません: $bak" }
    Stop-RemoteService
    robocopy $bak $dirs.Unc /E /XD logs /NFL /NDL /NJH /NP | Out-Host
    if ($LASTEXITCODE -ge 8) { throw "robocopy 失敗 (exit=$LASTEXITCODE)" }
    Start-RemoteService
    Test-Remote
    Write-Host "`n=== ロールバック完了: $bak ===" -ForegroundColor Cyan
    exit 0
}

# ── 1. 最新化 & ビルド ───────────────────────────────────────────────────────
Push-Location $SolutionDir
try {
    Write-Host "`n[1/5] git pull & build" -ForegroundColor Cyan
    git pull origin main
    if ($LASTEXITCODE -ne 0) { throw "git pull 失敗" }
    git log --oneline -1
    dotnet build $ProjectFile -c Release
    if ($LASTEXITCODE -ne 0) { throw "ビルド失敗（配置しません）" }

    # ── 2. publish ───────────────────────────────────────────────────────────
    Write-Host "`n[2/5] dotnet publish" -ForegroundColor Cyan
    $tmp = Join-Path $env:TEMP "ridoc_publish_$(Get-Date -Format yyyyMMddHHmmss)"
    dotnet publish $ProjectFile -c Release -r win-x64 --no-self-contained -o $tmp
    if ($LASTEXITCODE -ne 0) { throw "publish 失敗" }
    $binDir = Join-Path $ProjectDir "bin"
    $sdk = Get-ChildItem $binDir -Filter *.dll -File -ErrorAction SilentlyContinue
    if (-not $sdk) { throw "SDK DLL が見つかりません: $binDir\*.dll" }
    $sdk | Copy-Item -Destination $tmp -Force
    Write-Host "  SDK DLL 同梱: $(($sdk | ForEach-Object Name) -join ', ')"
}
finally { Pop-Location }

# ── 3. バックアップ ──────────────────────────────────────────────────────────
$stamp = Get-Date -Format yyyyMMddHHmmss
$bakDir = "$($dirs.Unc)_bak_$stamp"
Write-Host "`n[3/5] バックアップ: $bakDir" -ForegroundColor Cyan
robocopy $dirs.Unc $bakDir /E /XD logs /NFL /NDL /NJH /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "バックアップ失敗 (exit=$LASTEXITCODE)" }

# ── 4. 停止 → 配置 → 開始 ────────────────────────────────────────────────────
Write-Host "`n[4/5] 配置" -ForegroundColor Cyan
Stop-RemoteService
try {
    robocopy $tmp $dirs.Unc /E /XF appsettings.json appsettings.Development.json appsettings.Production.json /XD logs /NFL /NDL /NJH /NP | Out-Host
    if ($LASTEXITCODE -ge 8) { throw "robocopy 失敗 (exit=$LASTEXITCODE)" }
}
finally {
    Start-RemoteService
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# ── 5. 確認 ──────────────────────────────────────────────────────────────────
Write-Host "`n[5/5] 確認" -ForegroundColor Cyan
Test-Remote
Write-Host "`n=== デプロイ完了 === 戻す場合: .\ServiceTools\Deploy-Remote.ps1 -Rollback $stamp" -ForegroundColor Cyan
