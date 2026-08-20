param(
    [Parameter(Mandatory=$true)][string]$EquipmentImage,
    [Parameter(Mandatory=$true)][string]$RushImage,
    [Parameter(Mandatory=$true)][string]$QuestImage,
    [Parameter(Mandatory=$true)][string]$IceImage
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'

function Test-Screenshot([string]$name, [string]$image, [string[]]$expected,
    [string[]]$forbidden, [int]$budgetMs) {
    if (-not (Test-Path -LiteralPath $image)) { throw "缺少测试图片：$image" }
    Start-Process -FilePath $exe -ArgumentList @('--benchmark', "--benchmark-image=$image") `
        -WorkingDirectory $repoRoot -Wait
    $result = Get-Content (Join-Path $repoRoot 'last_run.txt') -Raw -Encoding UTF8
    $elapsed = [int]([regex]::Match($result, '耗时毫秒=(\d+)').Groups[1].Value)
    if ($elapsed -gt $budgetMs) { throw "$name 超时：${elapsed}ms > ${budgetMs}ms" }
    foreach ($text in $expected) {
        if (-not $result.Contains($text)) { throw "$name 缺少译文：$text" }
    }
    foreach ($text in $forbidden) {
        if ($result.Contains($text)) { throw "$name 出现误覆盖：$text" }
    }
    [pscustomobject]@{ 场景=$name; 耗时毫秒=$elapsed; 结果='通过' }
}

$results = @()
$results += Test-Screenshot '装备详情（二次录像截图）' $EquipmentImage `
    @('需要等级','物理防御力','剩余强化次数') @() 1600
$results += Test-Screenshot '人物属性＋突进详情' $RushImage `
    @('名称@','能力值点数@','暴击伤害@','突进：向前方突进一段距离') @() 1700
$results += Test-Screenshot '任务日志整段' $QuestImage `
    @('麦加的锻炼','我正在岔路口接受著名剑术大师麦加的指导') @('智力@{X=266') 1800
$results += Test-Screenshot '寒冰充能整段与等级效果' $IceImage `
    @('寒冰充能@','将你的剑或钝器附魔冰元素一段时间','持续90秒','持续97秒') @('力量@') 1200
$results | Format-Table -AutoSize
Write-Output '实图回归：4/4 通过'
