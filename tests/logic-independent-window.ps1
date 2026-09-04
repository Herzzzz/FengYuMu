$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
$assembly = [Reflection.Assembly]::LoadFile($exe)

$labelType = $assembly.GetType('MapleOverlay.OverlayLabel', $true)
$listType = [Type]::GetType('System.Collections.Generic.List`1').MakeGenericType($labelType)
$labels = [Activator]::CreateInstance($listType)
foreach ($item in @(
    @('第二条', 200, 10),
    @('第一条', 100, 10),
    @('第一条', 100, 15),
    @('这是一条较长的完整对话翻译，用于测试独立浮窗整块显示。', 300, 10))) {
    $label = [Activator]::CreateInstance($labelType, $true)
    $labelType.GetField('Text').SetValue($label, [string]$item[0])
    $labelType.GetField('Bounds').SetValue($label,
        (New-Object System.Drawing.RectangleF -ArgumentList `
            ([single]$item[2]), ([single]$item[1]), ([single]80), ([single]20)))
    $labels.Add($label)
}

$contentType = $assembly.GetType('MapleOverlay.TranslationWindowContent', $true)
$build = $contentType.GetMethod('Build', [Reflection.BindingFlags]'Static,NonPublic,Public')
$invokeArgs = New-Object 'object[]' 1
$invokeArgs[0] = $labels
$text = [string]$build.Invoke($null, $invokeArgs)
$lines = @($text -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 })
if ($lines.Count -ne 3 -or $lines[0] -ne '第一条' -or $lines[1] -ne '第二条') {
    throw "独立浮窗没有按画面顺序去重：$text"
}

$source = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
$forms = Get-Content (Join-Path $repoRoot 'src\SimpleForms.cs') -Raw -Encoding UTF8
foreach ($required in @(
    'GetValue("IndependentWindowEnabled", 0)',
    'ShowCurrentTranslations()',
    'translationWindow.Hide()',
    '!visibleTranslation || independentWindowEnabled')) {
    if (-not $source.Contains($required)) { throw "独立浮窗接入缺少：$required" }
}
foreach ($required in @(
    '独立翻译浮窗（集中显示，减少遮挡）',
    'FormBorderStyle.SizableToolWindow',
    'TopMost = true',
    'WS_EX_NOACTIVATE',
    'TranslationWindowContent.Build',
    'overlay.ApplyIndependentWindow')) {
    if (-not $forms.Contains($required)) { throw "独立浮窗界面缺少：$required" }
}

$errorPath = Join-Path $repoRoot 'translation_window_ui_test_error.txt'
$imagePath = Join-Path $repoRoot 'translation_window_ui_test.png'
Remove-Item -LiteralPath $errorPath -Force -ErrorAction SilentlyContinue
Start-Process -FilePath $exe -ArgumentList '--translation-window-ui-test' `
    -WorkingDirectory $repoRoot -Wait
if (Test-Path -LiteralPath $errorPath) {
    throw "独立浮窗界面自检失败：$(Get-Content $errorPath -Raw -Encoding UTF8)"
}
if (-not (Test-Path -LiteralPath $imagePath) -or (Get-Item -LiteralPath $imagePath).Length -lt 5000) {
    throw '独立浮窗界面截图未生成或内容为空'
}

Write-Output '独立翻译浮窗：位置排序、同文去重、可拖动缩放、置顶、截图前隐藏与原位覆盖回退通过'
