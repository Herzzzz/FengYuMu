$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
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
    '更新路径越过了枫语幕安装目录')) {
    if (-not $source.Contains($required)) { throw "一键更新安全链路缺少：$required" }
}
Remove-ItemProperty -Path 'HKCU:\Software\FengYuMu' -Name ApplicationUpdateSuccess,
    ApplicationUpdateMessage,ApplicationUpdateDetail -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
Write-Output '程序一键更新：GitHub正式包、双SHA-256、路径边界、三文件备份、校验替换和失败回退链路通过'
