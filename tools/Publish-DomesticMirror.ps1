[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Token,

    [string]$Directory = 'D:\GPT\文件\枫语幕\国内更新镜像',

    [string]$Owner = 'Herzzzz',

    [string]$Repo = 'maple-whisper-veil',

    [string]$Branch = 'main'
)

# Uploads the domestic update mirror to Gitee. The updater validates the manifest's
# fixed SHA-256 before it ever follows the ZIP URL, so the three files must be
# published together and the manifest must match the uploaded ZIP byte for byte.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$apiRoot = "https://gitee.com/api/v5/repos/$Owner/$Repo/contents"
$directory = [IO.Path]::GetFullPath($Directory)
$manifestPath = Join-Path $directory 'latest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "找不到本地清单：$manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$tag = [string]$manifest.tag
$zipName = "FengYuMu_$tag.zip"
$shaName = "$zipName.sha256"
$zipPath = Join-Path $directory $zipName
$shaPath = Join-Path $directory $shaName
foreach ($path in @($zipPath, $shaPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "缺少镜像文件：$path" }
}

# The manifest must describe exactly the ZIP sitting next to it.
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToUpperInvariant()
if ($zipHash -ne ([string]$manifest.sha256).ToUpperInvariant()) {
    throw "清单 SHA-256 与本地 ZIP 不一致：$($manifest.sha256) vs $zipHash"
}
$shaText = (Get-Content -LiteralPath $shaPath -Raw -Encoding UTF8).Trim()
if ($shaText -notmatch [Regex]::Escape($zipHash)) {
    throw '本地 .sha256 文件与 ZIP 不一致'
}
Write-Host "本地校验通过：$zipName  SHA-256=$zipHash"

function Get-RemoteSha([string]$path) {
    try {
        $uri = "$apiRoot/$([Uri]::EscapeDataString($path))?ref=$Branch"
        $response = Invoke-RestMethod -Uri $uri -Headers @{ Authorization = "token $Token" } `
            -TimeoutSec 30 -ErrorAction Stop
        # Gitee answers a missing path with an empty array rather than a 404, and strict
        # mode turns a missing property into a terminating error. Only an object that
        # actually carries sha describes a file we can update in place.
        $shaProperty = $response.PSObject.Properties['sha']
        if ($null -eq $shaProperty) { return '' }
        return [string]$shaProperty.Value
    } catch {
        # 404 means the file does not exist yet, which is the normal case on a first
        # upload. Strict mode is on and a connection-level failure has no Response
        # property at all, so the status has to be read defensively.
        $status = 0
        try { $status = [int]$_.Exception.Response.StatusCode } catch { $status = 0 }
        if ($status -eq 404) { return '' }
        if ("$($_.Exception.Message)" -match '\b404\b') { return '' }
        throw
    }
}

function Publish-File([string]$path, [string]$message) {
    $full = Join-Path $directory $path
    $bytes = [IO.File]::ReadAllBytes($full)
    $content = [Convert]::ToBase64String($bytes)
    $existing = Get-RemoteSha $path
    $body = @{
        access_token = $Token
        content      = $content
        message      = $message
        branch       = $Branch
    }
    if ($existing.Length -gt 0) { $body['sha'] = $existing }
    $method = if ($existing.Length -gt 0) { 'Put' } else { 'Post' }
    $uri = "$apiRoot/$([Uri]::EscapeDataString($path))"
    Invoke-RestMethod -Uri $uri -Method $method -Body $body -TimeoutSec 180 | Out-Null
    $verb = if ($existing.Length -gt 0) { '已更新' } else { '已新建' }
    Write-Host "  $verb $path  ($($bytes.Length) 字节)"
}

# Order matters: the ZIP and its checksum must exist before the manifest advertises
# them, otherwise a player could pull a manifest pointing at a missing asset.
Write-Host '上传国内镜像到 Gitee…'
Publish-File $zipName "release: upload $zipName"
Publish-File $shaName "release: upload $shaName"
Publish-File 'latest.json' "release: point manifest at $tag"

Write-Host ''
Write-Host '上传完成，开始校验远端可读性…'
foreach ($path in @($zipName, $shaName, 'latest.json')) {
    $raw = "https://gitee.com/$Owner/$Repo/raw/$Branch/$path"
    $head = Invoke-WebRequest -Uri $raw -Method Head -TimeoutSec 30 -UseBasicParsing
    if ($head.StatusCode -ne 200) { throw "远端不可读：$raw (HTTP $($head.StatusCode))" }
    Write-Host "  [OK] $path  HTTP $($head.StatusCode)"
}

$remoteManifest = (Invoke-WebRequest -Uri "https://gitee.com/$Owner/$Repo/raw/$Branch/latest.json" `
    -TimeoutSec 30 -UseBasicParsing).Content | ConvertFrom-Json
$remoteShaProperty = $remoteManifest.PSObject.Properties['sha256']
$remoteSha = if ($null -eq $remoteShaProperty) { '' } else { [string]$remoteShaProperty.Value }
if ($remoteSha -ne $zipHash) {
    throw "远端清单 SHA-256 与本地不一致：$remoteSha"
}
Write-Host "  远端清单 SHA-256 与本地一致：$zipHash"
Write-Output "国内镜像已发布：$tag（$zipName）"
