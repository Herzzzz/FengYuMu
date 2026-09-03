param(
    [string]$BaselineRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) '.bench\v2.3'),
    [string]$PublicRoot = (Join-Path $PSScriptRoot 'fixtures\public-v3'),
    [string]$ReportPath = (Join-Path (Split-Path -Parent $PSScriptRoot) '.bench\v2.3-v3.0-ab.csv')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$cases = @(
    [pscustomobject]@{ Name='技能'; File='skill-tab-triple-throw.png'; Expected=@('冒险岛勇士@','暗器伤人@','假动作@','武器用毒液@') },
    [pscustomobject]@{ Name='装备25'; File='equipment-rat-mouth.png'; Expected=@('老鼠嘴@','需要等级：25@','需要力量：0@','需要敏捷：0@','需要智力：0@','需要运气：0@','智力：+14@') },
    [pscustomobject]@{ Name='装备50'; File='equipment-crimsonheart-cloak.png'; Expected=@('需要等级：50@','需要力量：0@','需要敏捷：0@','需要智力：0@','需要运气：0@','魔法防御力：38@') },
    [pscustomobject]@{ Name='低分辨率人物'; File='character-stats-warrior.png'; Expected=@('名称@','职业@','生命值@','能力值点数@','物理防御力@','暴击伤害@','跳跃力@') },
    [pscustomobject]@{ Name='人物与装备'; File='character-equipment-dark-knight.png'; Expected=@('角色属性@','类型：枪@','物理攻击力：66@','名称@','能力值点数@','魔法防御力@','跳跃力@') },
    [pscustomobject]@{ Name='任务'; File='quest-dr-kim.png'; Expected=@('任务助手@','哦……这是我亲手打造的旷世杰作！有了这张蓝图，我就能造出最坚不可摧的机器人！@') },
    [pscustomobject]@{ Name='NPC对话'; File='npc-jack-dialogue.png'; Expected=@('哦，别担心——你只是快疯了而已！@','打起精神来，高手！@') }
)
$variants = @(
    [pscustomobject]@{ Version='v2.3'; Root=$BaselineRoot },
    [pscustomobject]@{ Version='v3.0'; Root=$repoRoot }
)

$rows = @()
foreach ($variant in $variants) {
    $exe = Join-Path $variant.Root '枫语幕.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "缺少对比程序：$exe" }
    foreach ($case in $cases) {
        $image = Join-Path $PublicRoot $case.File
        Start-Process -FilePath $exe -ArgumentList @('--benchmark','--benchmark-range=balanced',
            "--benchmark-image=$image") -WorkingDirectory $variant.Root -Wait
        $result = Get-Content (Join-Path $variant.Root 'last_run.txt') -Raw -Encoding UTF8
        $correct = 0
        foreach ($expected in $case.Expected) { if ($result.Contains($expected)) { $correct++ } }
        $rows += [pscustomobject]@{
            Version=$variant.Version; Scene=$case.Name
            Correct=$correct; Total=$case.Expected.Count
            ElapsedMs=[int]([regex]::Match($result,'耗时毫秒=(\d+)').Groups[1].Value)
            Hits=[int]([regex]::Match($result,'命中数量=(\d+)').Groups[1].Value)
        }
    }
}

$v2 = @($rows | Where-Object Version -eq 'v2.3')
$v3 = @($rows | Where-Object Version -eq 'v3.0')
$v2Correct = ($v2 | Measure-Object Correct -Sum).Sum
$v3Correct = ($v3 | Measure-Object Correct -Sum).Sum
$total = ($v3 | Measure-Object Total -Sum).Sum
if ($v3Correct -lt $v2Correct -or $v3Correct -ne $total) {
    throw "v3.0 同图人工关键项退化：v2.3=$v2Correct/$total，v3.0=$v3Correct/$total"
}
$directory = Split-Path -Parent $ReportPath
if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
$rows | Export-Csv -LiteralPath $ReportPath -NoTypeInformation -Encoding UTF8
$rows | Format-Table -AutoSize
Write-Output ("v2.3/v3.0 同图 A/B 通过：关键项 {0}/{2} -> {1}/{2}；平均耗时 {3}ms -> {4}ms；低价值背包图未计分" -f `
    $v2Correct,$v3Correct,$total,[int](($v2 | Measure-Object ElapsedMs -Average).Average),
    [int](($v3 | Measure-Object ElapsedMs -Average).Average))
