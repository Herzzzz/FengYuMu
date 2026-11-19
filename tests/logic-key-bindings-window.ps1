$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$assemblyPath = Join-Path $repoRoot '枫语幕.exe'
$dictionaryPath = Join-Path $repoRoot '枫语幕词库.tsv'
if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
}
$assembly = Load-TestAssembly $assemblyPath
$staticFlags = [Reflection.BindingFlags]'Static,NonPublic,Public'
$instanceFlags = [Reflection.BindingFlags]'Instance,NonPublic,Public'

$policy = $assembly.GetType('MapleOverlay.SettingsPanelPolicy', $true)
$isKeyBindings = $policy.GetMethod('IsKeyBindingsWindow', $staticFlags)
$isSettings = $policy.GetMethod('IsSettingsWindow', $staticFlags)
if ($null -eq $isKeyBindings -or $null -eq $isSettings) {
    throw '缺少按键设置面板判定 API'
}

foreach ($sample in @(
    'KEY BINDINGS Keyboard/Mouse Preset QUICK SLOTS KEYS',
    'KEY EINOINGS preset QUICK SL KEYS',
    'key bindings controller preset')) {
    if (-not [bool]$isKeyBindings.Invoke($null, @([string]$sample))) {
        throw "按键设置窗口未识别：$sample"
    }
    if (-not [bool]$isSettings.Invoke($null, @([string]$sample))) {
        throw "按键设置没有进入设置专属通道：$sample"
    }
}
foreach ($sample in @('Heal YOUS', 'KEY EINOINGS', 'Preset', 'Keyboard/Mouse')) {
    if ([bool]$isKeyBindings.Invoke($null, @([string]$sample))) {
        throw "孤立按键文字误判为完整按键设置窗口：$sample"
    }
}

$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $true)
$storeCtor = $storeType.GetConstructor($instanceFlags, $null, [Type[]]@([string]), $null)
$store = $storeCtor.Invoke(@([string]$dictionaryPath))
$storeType.GetMethod('Load', $instanceFlags).Invoke($store, @()) | Out-Null
$findSettings = $storeType.GetMethod('FindSettingsTextMatches', $instanceFlags)
if ($null -eq $findSettings) { throw '缺少设置专属词库匹配 API' }

function Get-ChineseValues([string]$text) {
    $values = @()
    foreach ($match in @($findSettings.Invoke($store, @([string]$text)))) {
        $entry = $match.GetType().GetField('Entry', $instanceFlags).GetValue($match)
        $values += [string]$entry.GetType().GetField('Chinese', $instanceFlags).GetValue($entry)
    }
    return @($values)
}

$expected = @{
    'KEY BINDINGS' = '按键设置'
    'KEY EINOINGS' = '按键设置'
    'Keyboard/Mouse' = '键盘/鼠标'
    'Preset' = '预设'
    'QUICK SLOTS KEYS' = '快捷栏按键'
    'QUICK SL KEYS' = '快捷栏按键'
    'DEFAULT' = '恢复默认'
    'CLEAR ALL' = '全部清除'
}
foreach ($key in $expected.Keys) {
    $values = @(Get-ChineseValues $key)
    if ($values -notcontains $expected[$key]) {
        throw "按键设置词条不可达：$key -> $($values -join ' / ')"
    }
}

$classifier = $assembly.GetType('MapleOverlay.SceneClassifier', $true)
$classify = $classifier.GetMethod('Classify', $staticFlags)
$headerEvidence = $classify.Invoke($null, @('KEY EINOINGS', $store, $true))
$headerKinds = [string]$headerEvidence.GetType().GetField('Kinds', $instanceFlags).GetValue($headerEvidence)
if ($headerKinds -notmatch 'Interface') {
    throw "OCR 错字标题没有被归入界面窗口：$headerKinds"
}

$sceneSource = Get-Content (Join-Path $repoRoot 'src\SceneRecognition.cs') -Raw -Encoding UTF8
$overlaySource = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
foreach ($required in @(
    'bool keyBindingsWindow = SettingsPanelPolicy.IsKeyBindingsWindow(result.Text);',
    'if (keyBindingsWindow)',
    'if (evidence.MainPanelHeader) snapshot.Add(SceneKind.Interface, 126);')) {
    if (-not $sceneSource.Contains($required)) { throw "场景隔离缺失：$required" }
}
foreach ($required in @(
    'bool dominantKeyBindings = SettingsPanelPolicy.IsKeyBindingsWindow(result.Text);',
    'if (dominantKeyBindings && !mainInterface) continue;',
    'SettingsPanelPolicy.IsSettingsWindow')) {
    if (-not $overlaySource.Contains($required)) { throw "面板规划隔离缺失：$required" }
}

Write-Output '按键设置窗口回归通过：OCR 标题错字、界面场景隔离、设置专属词库与强识别防误触均通过'
