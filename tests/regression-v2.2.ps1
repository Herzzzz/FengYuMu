param(
    [Parameter(Mandatory=$true)][string]$EquipmentImage,
    [Parameter(Mandatory=$true)][string]$RushImage,
    [Parameter(Mandatory=$true)][string]$QuestImage,
    [Parameter(Mandatory=$true)][string]$IceImage,
    [Parameter(Mandatory=$true)][string]$CharacterImage,
    [string]$CharacterCursor = '1510,798'
)

$ErrorActionPreference = 'Stop'

# EquipmentImage also contains an item list and item tooltip, so this set covers both
# equipment and ordinary item-panel behavior. Rush/Ice cover short and long skill text.
& (Join-Path $PSScriptRoot 'logic-regression-v2.2.ps1')
& (Join-Path $PSScriptRoot 'regression-v2.1.0.ps1') `
    -EquipmentImage $EquipmentImage -RushImage $RushImage `
    -QuestImage $QuestImage -IceImage $IceImage
& (Join-Path $PSScriptRoot 'regression-v2.1.2.ps1') `
    -CharacterImage $CharacterImage -Cursor $CharacterCursor

Write-Output 'v2.2.2 完整冻结基线：逻辑 + 5张实图全部通过'
