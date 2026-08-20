param(
    [Parameter(Mandatory=$true)][string]$SkillImage,
    [Parameter(Mandatory=$true)][string]$ItemImage,
    [Parameter(Mandatory=$true)][string]$ShopImage,
    [Parameter(Mandatory=$true)][string]$QuestImage
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'

function Test-Panel([string]$name, [string]$image, [string]$cursor,
    [string[]]$expected, [int]$budgetMs) {
    if (-not (Test-Path -LiteralPath $image)) { throw "缺少测试图片：$image" }
    Start-Process -FilePath $exe -ArgumentList @('--benchmark',
        "--benchmark-image=$image", "--benchmark-cursor=$cursor") `
        -WorkingDirectory $repoRoot -Wait
    $result = Get-Content (Join-Path $repoRoot 'last_run.txt') -Raw -Encoding UTF8
    if ($result.StartsWith('错误=')) { throw "$name 执行失败：$result" }
    $elapsed = [int]([regex]::Match($result, '耗时毫秒=(\d+)').Groups[1].Value)
    if ($elapsed -gt $budgetMs) { throw "$name 超时：${elapsed}ms > ${budgetMs}ms" }
    foreach ($text in $expected) {
        if (-not $result.Contains($text)) { throw "$name 缺少译文：$text" }
    }
    [pscustomobject]@{ 场景=$name; 耗时毫秒=$elapsed; 结果='通过' }
}

$results = @()
$results += Test-Panel '技能列表与悬浮详情' $SkillImage '185,560' `
    @('隐士之道@','影网@') 1200
$results += Test-Panel '角色信息、物品列表与装备需求' $ItemImage '944,315' `
    @('角色信息@','公民身份@','查看宠物信息@','需要敏捷@','可用职业：') 1000
$results += Test-Panel '玩家商店与装备详情' $ShopImage '621,432' `
    @('卖家信息@','购买道具@','离开商店@','Zhang 已进入@','售价1,999金币@',
      '需要等级@','剩余强化次数：7@') 1300
$results += Test-Panel '任务窗口、任务列表与按钮' $QuestImage '806,641' `
    @('可接取@','进行中@','已完成@','详情@','任务助手@','放弃任务@',
      '麦加的锻炼@','比格斯的物品收集@') 1700

$results | Format-Table -AutoSize
Write-Output 'v2.1.1 实图回归：4/4 通过'
