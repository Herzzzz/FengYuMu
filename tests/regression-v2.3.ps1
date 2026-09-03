param(
    [Parameter(Mandatory=$true)][string]$EquipmentImage,
    [Parameter(Mandatory=$true)][string]$RushImage,
    [Parameter(Mandatory=$true)][string]$QuestImage,
    [Parameter(Mandatory=$true)][string]$IceImage,
    [Parameter(Mandatory=$true)][string]$CharacterImage,
    [string]$CharacterCursor = '1510,798'
)

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'logic-regression-v2.2.ps1') `
    -ExpectedVersion '2.3.0.0' -ExpectedDisplayVersion 'v2.3'
& (Join-Path $PSScriptRoot 'logic-priority-modes.ps1')
& (Join-Path $PSScriptRoot 'regression-v2.1.0.ps1') `
    -EquipmentImage $EquipmentImage -RushImage $RushImage `
    -QuestImage $QuestImage -IceImage $IceImage
& (Join-Path $PSScriptRoot 'regression-v2.1.2.ps1') `
    -CharacterImage $CharacterImage -Cursor $CharacterCursor
& (Join-Path $PSScriptRoot 'regression-v2.3-public.ps1')

Write-Output 'v2.3 完整回归：逻辑、原有 5 图、新增 4 图三档全部通过'
