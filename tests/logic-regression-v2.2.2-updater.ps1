$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $repoRoot '枫语幕.exe'))
$type = $assembly.GetType('MapleOverlay.AiInstallForm', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'

$findAsset = $type.GetMethod('FindRuntimeAssetUrl', $flags)
$buildUrl = $type.GetMethod('BuildRuntimeUrlFromNightlyTag', $flags)
if ($null -eq $findAsset -or $null -eq $buildUrl) {
    throw '模型更新器解析方法缺失'
}

$stableOnly = '{"assets":[{"name":"nightly-tag.txt","browser_download_url":"https://example.invalid/nightly-tag.txt"}]}'
if ($null -ne $findAsset.Invoke($null, [object[]]@($stableOnly))) {
    throw '稳定版只有 nightly-tag.txt 时被误认成 Vulkan 运行包'
}

$direct = '{"assets":[{"name":"llama-b10621-bin-win-vulkan-x64.zip","browser_download_url":"https://example.invalid/runtime.zip"}]}'
$directUrl = [string]$findAsset.Invoke($null, [object[]]@($direct))
if ($directUrl -ne 'https://example.invalid/runtime.zip') {
    throw "Vulkan 运行包解析失败：$directUrl"
}

$nightlyUrl = [string]$buildUrl.Invoke($null, [object[]]@("b10621`r`n"))
$expected = 'https://github.com/ggml-org/llama.cpp/releases/download/b10621/llama-b10621-bin-win-vulkan-x64.zip'
if ($nightlyUrl -ne $expected) {
    throw "nightly 运行包地址构造失败：$nightlyUrl"
}

$invalidRejected = $false
try { $buildUrl.Invoke($null, [object[]]@('../bad')) | Out-Null }
catch {
    $errorType = $_.Exception.GetType().FullName
    $innerType = if ($null -ne $_.Exception.InnerException) { $_.Exception.InnerException.GetType().FullName } else { '' }
    $invalidRejected = $errorType -eq 'System.IO.InvalidDataException' -or $innerType -eq 'System.IO.InvalidDataException'
}
if (-not $invalidRejected) { throw 'nightly 版本标记没有进行安全校验' }

$source = Get-Content (Join-Path $repoRoot 'src\OfflineChat.cs') -Raw -Encoding UTF8
foreach ($required in @(
    'releases/latest/download/nightly-tag.txt',
    '运行库沿用当前版本，继续检查模型',
    '模型与知识库已正常检查；显卡运行库暂时无法联网核对')) {
    if (-not $source.Contains($required)) { throw "模型更新器回归缺少：$required" }
}

Write-Output 'v2.2.2 模型更新器回归：稳定版/nightly 解析、标记校验、失败隔离通过'
