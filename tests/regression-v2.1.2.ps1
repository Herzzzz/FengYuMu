param(
    [Parameter(Mandatory=$true)][string]$CharacterImage,
    [string]$Cursor = '1510,798',
    [int]$BudgetMs = 1900
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
if (-not (Test-Path -LiteralPath $CharacterImage)) {
    throw "缺少人物属性实图：$CharacterImage"
}

Start-Process -FilePath $exe -ArgumentList @('--benchmark',
    "--benchmark-image=$CharacterImage", "--benchmark-cursor=$Cursor") `
    -WorkingDirectory $repoRoot -Wait
$result = Get-Content (Join-Path $repoRoot 'last_run.txt') -Raw -Encoding UTF8
if ($result.StartsWith('错误=')) { throw "人物属性实图执行失败：$result" }
$elapsed = [int]([regex]::Match($result, '耗时毫秒=(\d+)').Groups[1].Value)
if ($elapsed -gt $BudgetMs) { throw "人物属性实图超时：${elapsed}ms > ${BudgetMs}ms" }

$expected = @('角色属性@','名称@','职业@','等级@','生命值@','魔法值@','经验@','人气@',
    '力量@','敏捷@','智力@','运气@','能力值点数@','攻击力@','物理防御力@',
    '魔法攻击力@','魔法防御力@','命中率@','回避率@','暴击率@','暴击伤害@',
    '移动速度@','跳跃力@','暴击率：决定攻击造成暴击的概率；')
foreach ($text in $expected) {
    if (-not $result.Contains($text)) { throw "人物属性实图缺少译文：$text" }
}
if ($result -match '(?:^| \| )伤害@') { throw '浅色属性提示框仍被拆成“伤害”碎词' }

[pscustomobject]@{ 场景='经典人物属性与浅色悬浮说明'; 耗时毫秒=$elapsed; 命中=24; 结果='通过' } |
    Format-Table -AutoSize
Write-Output 'v2.1.2 实图回归：通过'
