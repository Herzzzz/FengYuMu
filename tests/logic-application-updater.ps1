$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = if ($env:FENGYUMU_TEST_EXE) { $env:FENGYUMU_TEST_EXE } else {
    Join-Path $repoRoot '枫语幕.exe'
}
$assembly = [Reflection.Assembly]::LoadFile($exe)
$all = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'
$updater = $assembly.GetType('MapleOverlay.ApplicationUpdater', $true)
$manifestType = $assembly.GetType('MapleOverlay.ApplicationUpdateManifest', $true)
$hashMethod = $updater.GetMethod('ComputeSha256', $all)
$childMethod = $updater.GetMethod('IsChildPath', $all)
$applyMethod = $updater.GetMethod('ApplyPreparedUpdate', $all)

$testRoot = Join-Path $repoRoot '.bench\application-updater-test'
$resolvedRepo = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\') + '\'
$resolvedTest = [IO.Path]::GetFullPath($testRoot)
if (-not $resolvedTest.StartsWith($resolvedRepo, [StringComparison]::OrdinalIgnoreCase)) {
    throw '更新测试目录越过仓库边界'
}
if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
$install = Join-Path $testRoot '安装目录'
$stage = Join-Path $install '更新临时\test\payload'
$backup = Join-Path $install '更新备份\v3.0-test'
New-Item -ItemType Directory -Path $install,$stage -Force | Out-Null
$files = @('枫语幕.exe','枫语幕词库.tsv','使用说明.txt')
foreach ($name in $files) {
    [IO.File]::WriteAllText((Join-Path $install $name), "old-$name", [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $stage $name), "new-$name", [Text.UTF8Encoding]::new($false))
}

if (-not [bool]$childMethod.Invoke($null, [object[]]@([string]$install, [string]$stage)) -or
    [bool]$childMethod.Invoke($null, [object[]]@([string]$install, [string](Join-Path $testRoot 'sibling')))) {
    throw '安全路径边界判断错误'
}
$hashes = @{}
foreach ($name in $files) {
    $hashes[$name] = [string]$hashMethod.Invoke($null, [object[]]@([string](Join-Path $stage $name)))
}
$manifest = [Activator]::CreateInstance($manifestType, $true)
$manifestType.GetProperty('InstallDirectory', $all).SetValue($manifest, $install, $null)
$manifestType.GetProperty('StageDirectory', $all).SetValue($manifest, $stage, $null)
$manifestType.GetProperty('BackupDirectory', $all).SetValue($manifest, $backup, $null)
$manifestType.GetProperty('Tag', $all).SetValue($manifest, 'v3.0-test', $null)
$manifestType.GetProperty('OldProcessId', $all).SetValue($manifest, 0, $null)
$dictionaryType = [Collections.Generic.Dictionary[string,string]]
$hashDictionary = [Activator]::CreateInstance($dictionaryType)
foreach ($name in $files) { $hashDictionary.Add($name, $hashes[$name]) }
$manifestType.GetProperty('Hashes', $all).SetValue($manifest, $hashDictionary, $null)

Add-Type -AssemblyName System.Web.Extensions
$serializer = [Web.Script.Serialization.JavaScriptSerializer]::new()
$manifestPath = Join-Path (Split-Path -Parent $stage) 'update-manifest.json'
[IO.File]::WriteAllText($manifestPath, $serializer.Serialize($manifest), [Text.UTF8Encoding]::new($false))
$result = [int]$applyMethod.Invoke($null, [object[]]@([string]$manifestPath))
if ($result -ne 0) { throw "沙盒更新执行失败，退出码 $result" }
foreach ($name in $files) {
    if ([IO.File]::ReadAllText((Join-Path $install $name)) -ne "new-$name") {
        throw "一键更新没有替换：$name"
    }
    if ([IO.File]::ReadAllText((Join-Path $backup $name)) -ne "old-$name") {
        throw "一键更新没有备份旧文件：$name"
    }
}

$source = Get-Content (Join-Path $repoRoot 'src\ApplicationUpdater.cs') -Raw -Encoding UTF8
foreach ($required in @('FengYuMu/releases/latest','安装包 SHA-256 校验失败',
    'RestoreBackup(manifest)','更新失败，已继续使用更新前版本','File.Replace',
    '更新路径越过了枫语幕安装目录','HTTPS_PROXY','Windows系统代理',
    '已自动尝试 Windows 系统代理、环境代理和直连','UpdateWebClient',
    'DomesticMirrorManifestUrl','ParseMirrorManifest','ParseSha256Text',
    'TryLoadDomesticMirrorManifest')) {
    if (-not $source.Contains($required)) { throw "一键更新安全链路缺少：$required" }
}

$parseMirror = $updater.GetMethod('ParseMirrorManifest', $all)
$parseHash = $updater.GetMethod('ParseSha256Text', $all)
if ($null -eq $parseMirror -or $null -eq $parseHash) {
    throw '国内镜像清单解析入口缺失'
}
$mirrorUrl = 'https://gitee.com/Herzzzz/maple-whisper-veil/raw/main/latest.json'
$hashA = [string]::new([char]'a', 64)
$hashB = [string]::new([char]'b', 64)
$hashC = [string]::new([char]'c', 64)
$hashD = [string]::new([char]'d', 64)
$hashE = [string]::new([char]'e', 64)
$validMirror = [string](@{
    tag = 'v3.2.2'
    zipUrl = 'https://gitee.com/Herzzzz/maple-whisper-veil/raw/main/FengYuMu_v3.2.2.zip'
    sha256Url = 'https://gitee.com/Herzzzz/maple-whisper-veil/raw/main/FengYuMu_v3.2.2.zip.sha256'
    sha256 = $hashA
} | ConvertTo-Json -Compress)
$parsedMirror = $parseMirror.Invoke($null, [object[]]@($validMirror, $mirrorUrl))
if ($parsedMirror.Tag -ne 'v3.2.2' -or
    $parsedMirror.ZipUrl -ne 'https://gitee.com/Herzzzz/maple-whisper-veil/raw/main/FengYuMu_v3.2.2.zip' -or
    $parsedMirror.Sha256.Length -ne 64) {
    throw '国内镜像有效清单解析结果错误'
}

function Assert-MirrorRejected([string]$json, [string]$label) {
    $rejected = $false
    try {
        [void]$parseMirror.Invoke($null, [object[]]@($json, $mirrorUrl))
    }
    catch { $rejected = $true }
    if (-not $rejected) { throw "国内镜像恶意/错误 fixture 未被拒绝：$label" }
}
Assert-MirrorRejected ((@{
    tag = 'v3.2.2'; zipUrl = 'http://evil.example/FengYuMu_v3.2.2.zip';
    sha256 = $hashA
} | ConvertTo-Json -Compress)) '非 HTTPS ZIP 地址'
Assert-MirrorRejected ((@{
    tag = 'v3.2.2'; zipUrl = 'https://evil.example/FengYuMu_v3.2.2.zip';
    sha256 = $hashA
} | ConvertTo-Json -Compress)) '跨域 ZIP 地址'
Assert-MirrorRejected ((@{
    tag = 'v3.2.2'; zipUrl = 'https://gitee.com/Herzzzz/maple-whisper-veil/raw/main/other.zip';
    sha256 = $hashA
} | ConvertTo-Json -Compress)) 'ZIP 文件名不匹配'
Assert-MirrorRejected ((@{
    tag = 'v3.2.2/../../x'; zipUrl = 'https://gitee.com/Herzzzz/maple-whisper-veil/raw/main/FengYuMu_v3.2.2.zip';
    sha256 = $hashA
} | ConvertTo-Json -Compress)) '恶意版本号'
Assert-MirrorRejected ((@{
    tag = 'v3.2.2'; zipUrl = 'https://gitee.com/Herzzzz/maple-whisper-veil/raw/main/FengYuMu_v3.2.2.zip';
    sha256 = '1234'
} | ConvertTo-Json -Compress)) '错误固定 SHA-256'
Assert-MirrorRejected ((@{
    tag = 'v3.2.2'; zipUrl = 'https://gitee.com/Herzzzz/maple-whisper-veil/raw/main/FengYuMu_v3.2.2.zip'
} | ConvertTo-Json -Compress)) '缺失 SHA-256'

$validHashArgs = [object[]]::new(2)
$validHashArgs[0] = $hashB + "  FengYuMu_v3.2.2.zip`r`n"
$validHashArgs[1] = 'FengYuMu_v3.2.2.zip'
$parsedHash = $parseHash.Invoke($null, $validHashArgs)
if ($parsedHash -ne $hashB.ToUpperInvariant()) { throw '国内镜像 SHA-256 fixture 解析错误' }
$wrongNameRejected = $false
try {
    $wrongNameArgs = [object[]]::new(2)
    $wrongNameArgs[0] = $hashC + "  FengYuMu_v3.2.2.zip`r`n"
    $wrongNameArgs[1] = 'FengYuMu_v3.2.1.zip'
    [void]$parseHash.Invoke($null, $wrongNameArgs)
}
catch { $wrongNameRejected = $true }
if (-not $wrongNameRejected) { throw '国内镜像错误 SHA-256 文件名 fixture 未被拒绝' }
$wrongHashRejected = $false
try {
    [void]$updater.GetMethod('EnsureSha256Matches', $all).Invoke($null,
        [object[]]@($hashD, $hashE))
}
catch { $wrongHashRejected = $true }
if (-not $wrongHashRejected) { throw '国内镜像错误 SHA-256 fixture 未被拒绝' }

Remove-ItemProperty -Path 'HKCU:\Software\FengYuMu' -Name ApplicationUpdateSuccess,
    ApplicationUpdateMessage,ApplicationUpdateDetail -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
Write-Output '程序一键更新：GitHub正式包、双SHA-256、路径边界、三文件备份、校验替换和失败回退链路通过'
