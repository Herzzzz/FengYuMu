[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v?\d+(?:\.\d+){1,3}(?:-[a-z0-9.-]+)?$')]
    [string]$Tag,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$ZipPath,

    [string]$Sha256Path,

    [string]$MirrorBaseUrl = 'https://gitee.com/Herzzzz/maple-whisper-veil/raw/main',

    [string]$OutputDirectory = 'D:\GPT\文件\枫语幕\国内更新镜像'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-VersionPart([string]$value) {
    if ($value.StartsWith('v', [StringComparison]::OrdinalIgnoreCase)) {
        return $value.Substring(1)
    }
    return $value
}

function Assert-HttpsUrl([string]$value, [string]$fieldName) {
    $uri = $null
    if (-not [Uri]::TryCreate($value, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Fragment -or $uri.Query -or
        (-not $uri.IsDefaultPort -and $uri.Port -ne 443)) {
        throw "$fieldName 必须是无跳转 HTTPS 地址"
    }
    return $uri
}

$versionPart = Get-VersionPart $Tag
$zipName = "FengYuMu_v$versionPart.zip"
$shaName = "$zipName.sha256"
$baseUri = Assert-HttpsUrl $MirrorBaseUrl '镜像目录地址'
if ($baseUri.AbsolutePath.EndsWith('/')) {
    $base = $baseUri.AbsoluteUri.TrimEnd('/')
} else {
    $base = $baseUri.AbsoluteUri
}
$zipUrl = "$base/$zipName"
$shaUrl = "$base/$shaName"
Assert-HttpsUrl $zipUrl 'ZIP 地址' | Out-Null
Assert-HttpsUrl $shaUrl 'SHA-256 地址' | Out-Null

$zipItem = Get-Item -LiteralPath $ZipPath
$zipHash = (Get-FileHash -LiteralPath $zipItem.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
if ($Sha256Path) {
    if (-not (Test-Path -LiteralPath $Sha256Path -PathType Leaf)) {
        throw "SHA-256 文件不存在：$Sha256Path"
    }
    $sourceHash = (Get-Content -LiteralPath $Sha256Path -Raw -Encoding UTF8).Trim()
    $match = [Regex]::Match($sourceHash, '(?im)^\s*([0-9a-f]{64})(?:\s+\*?[^\r\n]+)?\s*$')
    if (-not $match.Success -or $match.Groups[1].Value.ToUpperInvariant() -ne $zipHash) {
        throw '指定的 SHA-256 文件与 ZIP 不一致，已停止生成清单'
    }
}

$output = [IO.Path]::GetFullPath($OutputDirectory)
$allowedRoot = [IO.Path]::GetFullPath('D:\GPT').TrimEnd('\') + '\'
if (-not $output.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "输出目录必须位于 D:\GPT 下：$output"
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$destinationZip = Join-Path $output $zipName
$destinationSha = Join-Path $output $shaName
$manifestPath = Join-Path $output 'latest.json'
Copy-Item -LiteralPath $zipItem.FullName -Destination $destinationZip -Force
Set-Content -LiteralPath $destinationSha -Value ("$zipHash  $zipName") -Encoding ASCII

$manifest = [ordered]@{
    tag = $Tag
    zipUrl = $zipUrl
    sha256Url = $shaUrl
    sha256 = $zipHash
}
$json = $manifest | ConvertTo-Json -Depth 3
Set-Content -LiteralPath $manifestPath -Value $json -Encoding UTF8

Write-Output "已生成国内镜像文件（未上传）：$output"
Get-Item -LiteralPath $manifestPath, $destinationZip, $destinationSha |
    Select-Object FullName, Length
