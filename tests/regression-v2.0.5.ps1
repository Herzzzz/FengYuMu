param(
    [Parameter(Mandatory=$true)][string]$EquipmentImage,
    [Parameter(Mandatory=$true)][string]$SkillImage,
    [Parameter(Mandatory=$true)][string]$QuestImage
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'

function Test-Screenshot([string]$name, [string]$image, [string[]]$expected) {
    if (-not (Test-Path -LiteralPath $image)) { throw "缺少测试图片：$image" }
    Start-Process -FilePath $exe -ArgumentList @('--benchmark', "--benchmark-image=$image") `
        -WorkingDirectory $repoRoot -Wait
    $resultPath = Join-Path $repoRoot 'last_run.txt'
    $result = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8
    $elapsed = [int]([regex]::Match($result, '耗时毫秒=(\d+)').Groups[1].Value)
    if ($elapsed -gt 2000) { throw "$name 超时：${elapsed}ms" }
    foreach ($text in $expected) {
        if (-not $result.Contains($text)) { throw "$name 缺少译文：$text" }
    }
    [pscustomobject]@{ 场景=$name; 耗时毫秒=$elapsed; 结果='通过' }
}

Test-Screenshot '装备详情' $EquipmentImage @('物理防御力','剩余强化次数')
Test-Screenshot '技能详情' $SkillImage @('突进：向前方突进一段距离')
Test-Screenshot '任务日志' $QuestImage @('麦加的锻炼','我正在岔路口接受著名剑术大师麦加的指导')
