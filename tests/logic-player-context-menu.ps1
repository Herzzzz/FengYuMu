$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$imagePath = 'D:\TEMP\codex-clipboard-20fc3ca0-07dd-4334-ab84-fef02d7ea5da.png'
if (-not (Test-Path -LiteralPath $imagePath)) { throw "缺少玩家右键菜单实图：$imagePath" }
if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
}
$assembly = Load-TestAssembly (Join-Path $repoRoot '枫语幕.exe')
Add-Type -AssemblyName System.Drawing
$flags = [Reflection.BindingFlags]'Instance,Static,NonPublic,Public'
$visionType = $assembly.GetType('MapleOverlay.ClassicSceneVision', $true)
$findPanels = $visionType.GetMethod('FindForegroundPanels', $flags)
if ($null -eq $findPanels) { throw '缺少前景面板检测 API' }
$bitmap = [Drawing.Bitmap]::new($imagePath)
try {
    $regions = @($findPanels.Invoke($null, [object[]]@($bitmap)))
    $menu = @($regions | Where-Object {
        [string]$_.Kind -eq 'DarkBlue' -and $_.Bounds.Left -ge 940 -and
            $_.Bounds.Left -le 1000 -and $_.Bounds.Top -ge 580 -and $_.Bounds.Top -le 640 -and
            $_.Bounds.Width -ge 170 -and $_.Bounds.Height -ge 230
    })
    if ($menu.Count -ne 1) {
        $summary = ($regions | ForEach-Object { "$($_.Kind):$($_.Bounds):$($_.Score)" }) -join ' | '
        throw "玩家右键菜单没有被识别为紧凑深蓝面板：$summary"
    }
}
finally { $bitmap.Dispose() }

$sceneSource = Get-Content (Join-Path $repoRoot 'src\SceneRecognition.cs') -Raw -Encoding UTF8
foreach ($required in @('FindPlayerContextMenuComponents', 'IsPlayerMenuPixel',
        'The player context menu is a compact cyan-blue stack')) {
    if (-not $sceneSource.Contains($required)) { throw "缺少玩家菜单视觉检测保护：$required" }
}

$dictionaryPath = Join-Path $repoRoot '枫语幕词库.tsv'
$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $true)
$ctor = $storeType.GetConstructor($flags, $null, [Type[]]@([string]), $null)
$store = $ctor.Invoke(@([string]$dictionaryPath))
$storeType.GetMethod('Load', $flags).Invoke($store, @()) | Out-Null
$findInterface = $storeType.GetMethod('FindInterfaceTextMatches', $flags)
foreach ($pair in @(
    @{ English = 'CHARACTER INFO'; Chinese = '角色信息' },
    @{ English = 'SAVE NAME'; Chinese = '保存名字' },
    @{ English = 'TRADE REQUEST'; Chinese = '交易请求' },
    @{ English = 'WHISPER'; Chinese = '悄悄话' },
    @{ English = 'INVITE TO CHAT'; Chinese = '邀请聊天' },
    @{ English = 'ADD FRIEND'; Chinese = '加为好友' },
    @{ English = 'BLACKLIST'; Chinese = '加入黑名单' },
    @{ English = 'REPORT'; Chinese = '举报' })) {
    $matches = @($findInterface.Invoke($store, @([string]$pair.English)))
    $values = @($matches | ForEach-Object { $_.Entry.Chinese })
    if ($values -notcontains $pair.Chinese) {
        throw "玩家菜单词条不可达：$($pair.English) -> $($values -join ' / ')"
    }
}
Write-Output '玩家右键菜单回归通过：实图紧凑深蓝面板被定位，8项菜单词条可匹配'
