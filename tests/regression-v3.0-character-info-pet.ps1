$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
$image = Join-Path $repoRoot 'tests\fixtures\character-info-pet-item-list-20260909.png'
if (-not (Test-Path -LiteralPath $image)) { throw "缺少用户角色/宠物信息问题图：$image" }

$process = Start-Process -FilePath $exe -ArgumentList @('--benchmark',
    "--benchmark-image=$image", '--benchmark-range=maximum', '--benchmark-cursor=621,205') `
    -WorkingDirectory $repoRoot -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "角色/宠物信息问题图执行失败：$($process.ExitCode)" }
$report = Get-Content (Join-Path $repoRoot 'last_run.txt') -Raw -Encoding UTF8
$labels = [regex]::Match($report, '(?m)^覆盖标签=(.*)$').Groups[1].Value
$countMatch = [regex]::Match($report, '(?m)^命中数量=(\d+)\r?$')
if (-not $countMatch.Success) { throw '角色/宠物信息问题图缺少命中统计' }
$count = [int]$countMatch.Groups[1].Value
if ($count -gt 12) { throw "角色/宠物信息问题图仍过度铺字：$count 个｜$labels" }
foreach ($forbidden in @('角色属性@','生命值@','魔法值@','经验@','力量@','敏捷@','智力@','运气@',
    '攻击力@','物理防御力@','魔法攻击力@','魔法防御力@','命中率@','回避率@','暴击率@','移动速度@','跳跃力@')) {
    if ($labels.Contains($forbidden)) { throw "角色/宠物信息仍套用人物属性网格：$forbidden｜$labels" }
}
foreach ($required in @('角色信息@','查看宠物信息@')) {
    if (-not $labels.Contains($required)) { throw "角色/宠物信息缺少必要标签：$required｜$labels" }
}
Write-Output "用户角色/宠物信息问题图通过：保留必要信息，$count 个标签，无人物属性网格污染"
