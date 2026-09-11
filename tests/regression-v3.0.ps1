param(
    [Parameter(Mandatory=$true)][string]$EquipmentImage,
    [Parameter(Mandatory=$true)][string]$RushImage,
    [Parameter(Mandatory=$true)][string]$QuestImage,
    [Parameter(Mandatory=$true)][string]$IceImage,
    [Parameter(Mandatory=$true)][string]$CharacterImage,
    [string]$CharacterCursor = '1510,798'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'

& (Join-Path $PSScriptRoot 'logic-regression-v2.2.ps1') `
    -ExpectedVersion '3.0.0.0' -ExpectedDisplayVersion 'v3.0'
& (Join-Path $PSScriptRoot 'logic-priority-modes.ps1')
& (Join-Path $PSScriptRoot 'logic-regression-v3.0.ps1')
& (Join-Path $PSScriptRoot 'logic-continuous-translation.ps1')
& (Join-Path $PSScriptRoot 'logic-independent-window.ps1')
& (Join-Path $PSScriptRoot 'logic-ai-chat-accuracy.ps1')
& (Join-Path $PSScriptRoot 'logic-hotkey-toggle.ps1')
& (Join-Path $PSScriptRoot 'regression-v3.0-character-info-pet.ps1')
& (Join-Path $PSScriptRoot 'logic-ai-chat-performance.ps1')
& (Join-Path $PSScriptRoot 'logic-overlay-paint-safety.ps1')
& (Join-Path $PSScriptRoot 'logic-chat-region-first-use.ps1')
& (Join-Path $PSScriptRoot 'logic-regression-chat-abbreviations.ps1') `
    -ExpectedVersion '3.0.0.0' -ExpectedDisplayVersion 'v3.0'
& (Join-Path $PSScriptRoot 'logic-regression-v2.2.2-updater.ps1')
& (Join-Path $PSScriptRoot 'gamepad-shortcuts.ps1')

& (Join-Path $PSScriptRoot 'regression-v2.1.0.ps1') `
    -EquipmentImage $EquipmentImage -RushImage $RushImage `
    -QuestImage $QuestImage -IceImage $IceImage
& (Join-Path $PSScriptRoot 'regression-v2.1.2.ps1') `
    -CharacterImage $CharacterImage -Cursor $CharacterCursor
& (Join-Path $PSScriptRoot 'regression-v2.3-public.ps1')
& (Join-Path $PSScriptRoot 'regression-v3.0-public.ps1')
& (Join-Path $PSScriptRoot 'regression-v3.0-continuous.ps1')
& (Join-Path $PSScriptRoot 'video-dictionary-review\validate-video-5420-dictionary.ps1')
& (Join-Path $PSScriptRoot 'benchmark-v3.0-resources.ps1')
& (Join-Path $PSScriptRoot 'compare-v2.3-v3.0.ps1')

$mainUiError = Join-Path $repoRoot 'main_ui_test_error.txt'
Remove-Item -LiteralPath $mainUiError -Force -ErrorAction SilentlyContinue
Start-Process -FilePath $exe -ArgumentList '--main-ui-test' -WorkingDirectory $repoRoot -Wait
if (Test-Path -LiteralPath $mainUiError) {
    throw "主界面渲染自检异常：$(Get-Content $mainUiError -Raw -Encoding UTF8)"
}
$mainUi = Join-Path $repoRoot 'main_ui_test.png'
if (-not (Test-Path -LiteralPath $mainUi) -or (Get-Item -LiteralPath $mainUi).Length -lt 20000) {
    throw '主界面高 DPI 渲染自检失败'
}
Start-Process -FilePath $exe -ArgumentList '--dictionary-ui-test' -WorkingDirectory $repoRoot -Wait
$dictionaryUi = Get-Content (Join-Path $repoRoot 'dictionary_ui_test.txt') -Raw -Encoding UTF8
foreach ($required in @('界面','地图/NPC','职业技能','详情说明','图标/其他')) {
    if (-not $dictionaryUi.Contains($required)) { throw "词库界面分类缺少：$required" }
}
Start-Process -FilePath $exe -ArgumentList '--hotkey-ui-test' -WorkingDirectory $repoRoot -Wait
$hotkeyUi = Join-Path $repoRoot 'hotkey_ui_test.png'
if (-not (Test-Path -LiteralPath $hotkeyUi) -or (Get-Item -LiteralPath $hotkeyUi).Length -lt 10000) {
    throw '快捷键界面渲染自检失败'
}
& (Join-Path $PSScriptRoot 'tray-recovery-integration.ps1')

Write-Output 'v3.0 全量回归通过：旧逻辑/旧五图、公开实机图、持续模式、视频词库、资源、A/B、界面、手柄、托盘与单实例'
