$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $repoRoot '枫语幕.exe'))
$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $true)
$constructor = $storeType.GetConstructor(
    [Reflection.BindingFlags]'Instance,NonPublic,Public', $null,
    [Type[]]@([string]), $null)
$dictionaryPath = [string](Join-Path $repoRoot '枫语幕词库.tsv')
$constructorArgs = New-Object 'object[]' 1
$constructorArgs[0] = $dictionaryPath.ToString()
$store = $constructor.Invoke($constructorArgs)
$storeType.GetMethod('Load').Invoke($store, @()) | Out-Null

function Invoke-Store([string]$method, [object[]]$arguments) {
    $storeType.GetMethod($method).Invoke($store, $arguments)
}

function Assert-Translation([string]$name, $matches, [string]$expected) {
    $values = @($matches | ForEach-Object {
        $entry = $_.GetType().GetField('Entry').GetValue($_)
        $entry.GetType().GetField('Chinese').GetValue($entry)
    })
    if (-not ($values -match [regex]::Escape($expected))) {
        throw "$name 未命中：$expected"
    }
}

# 任务：名称先锁定 1009，再用有漏字和误字的整段 OCR 匹配同一任务。
$taskOcr = "I arn now under the tutelage of the famous sword muter Mai in A Split Road " +
    "For my first lesson she told me to hunt Blue Snails Shroorns and Fed Snails " +
    "Mai also said to meet up with Biggs in Southperrv tor a useful quest"
Assert-Translation '任务整段' (Invoke-Store 'FindTaskMatches' @($taskOcr, '1009')) '岔路口'

# 技能：两项不同职业技能都按技能 ID 限定，验证不是 Rush 单项特例。
$rushOcr = 'Rush tward a certain distance inth direction you are facing. It there are monsters within the rushrange. you can deal damage to up tootf them and knock them back a certain distance.'
Assert-Translation '突进说明' (Invoke-Store 'FindSkillTextMatches' @($rushOcr, '1321003')) '最多可攻击4只'
$clawOcr = 'Increases Claw Mastery and Attack Power and gives a chance to recover used throwing stars when using throwing star attacks Only applies when a claw is equipped'
Assert-Translation '精准暗器说明' (Invoke-Store 'FindSkillTextMatches' @($clawOcr, '4100000')) '提高拳套熟练度和攻击力'

# 装备：通用属性不依赖某一件装备。
Assert-Translation '装备需求' (Invoke-Store 'FindMatches' @('REQ STR 10')) '需要力量'
Assert-Translation '装备防御' (Invoke-Store 'FindMatches' @('Weapon Def: +14')) '物理防御力'
Assert-Translation '强化次数' (Invoke-Store 'FindMatches' @('Remaining Enhancements: 5')) '剩余强化次数'

# 长句与聊天隔离：界面长句可容错，聊天俚语不得进入 F8 覆盖。
$cabOcr = 'The regular fee applies for all non beginners The Ant Tunnel is located deep inside the center of Victoria Island where danger awaits Would you like to go there for 10000 mesos'
Assert-Translation '界面长句' (Invoke-Store 'FindInterfaceTextMatches' @($cabOcr)) '非新手玩家需支付正常车费'
$chatMatches = Invoke-Store 'FindMatches' @('wth')
if ($chatMatches.Count -ne 0) { throw '聊天俚语泄漏到 F8 覆盖' }

Write-Output '逻辑回归：8/8 通过'
