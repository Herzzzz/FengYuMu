param(
    [string]$PublicRoot = (Join-Path $PSScriptRoot 'fixtures\public-v3')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'

# Only high-value scenes are scored. inventory-sorting.png is intentionally excluded,
# and quest-dr-kim-gyazo.png is the same scene as quest-dr-kim.png so it is not double-counted.
$cases = @(
    [pscustomobject]@{
        Name='技能列表'; File='skill-tab-triple-throw.png'; MaximumLabels=8
        Scenes=@('技能:'); Expected=@('冒险岛勇士@','暗器伤人@','假动作@','武器用毒液@')
    },
    [pscustomobject]@{
        Name='装备详情 25/0 纠错'; File='equipment-rat-mouth.png'; MaximumLabels=18
        Scenes=@('装备物品:'); Expected=@('老鼠嘴@','需要等级：25@','需要力量：0@','需要敏捷：0@','需要智力：0@','需要运气：0@','智力：+14@')
    },
    [pscustomobject]@{
        Name='装备详情 50/0 纠错'; File='equipment-crimsonheart-cloak.png'; MaximumLabels=19
        Scenes=@('装备物品:'); Expected=@('需要等级：50@','需要力量：0@','需要敏捷：0@','需要智力：0@','需要运气：0@','魔法防御力：38@')
    },
    [pscustomobject]@{
        Name='低分辨率人物属性'; File='character-stats-warrior.png'; MaximumLabels=27
        Scenes=@('人物:','人物视觉:'); Expected=@('名称@','职业@','生命值@','能力值点数@','物理防御力@','暴击伤害@','跳跃力@')
    },
    [pscustomobject]@{
        Name='人物属性与装备并存'; File='character-equipment-dark-knight.png'; MaximumLabels=38
        Scenes=@('人物:','装备物品:','人物视觉:'); Expected=@('角色属性@','类型：枪@','物理攻击力：66@','名称@','能力值点数@','魔法防御力@','跳跃力@')
    },
    [pscustomobject]@{
        Name='任务长句'; File='quest-dr-kim.png'; MaximumLabels=4
        Scenes=@('任务:'); Expected=@('任务助手@','哦……这是我亲手打造的旷世杰作！有了这张蓝图，我就能造出最坚不可摧的机器人！@')
    },
    [pscustomobject]@{
        Name='NPC 对话'; File='npc-jack-dialogue.png'; MaximumLabels=5
        Scenes=@('NPC对话:'); Expected=@('哦，别担心——你只是快疯了而已！@','打起精神来，高手！@')
    }
)

$results = @()
foreach ($mode in @('maximum','balanced','minimum')) {
    foreach ($case in $cases) {
        $image = Join-Path $PublicRoot $case.File
        if (-not (Test-Path -LiteralPath $image)) { throw "缺少公开样本：$image" }
        Start-Process -FilePath $exe -ArgumentList @('--benchmark',
            "--benchmark-image=$image", "--benchmark-range=$mode") -WorkingDirectory $repoRoot -Wait
        $result = Get-Content (Join-Path $repoRoot 'last_run.txt') -Raw -Encoding UTF8
        if ($result.StartsWith('错误=')) { throw "$($case.Name)/$mode 执行失败：$result" }
        $elapsed = [int]([regex]::Match($result, '耗时毫秒=(\d+)').Groups[1].Value)
        $hits = [int]([regex]::Match($result, '命中数量=(\d+)').Groups[1].Value)
        if ($elapsed -gt 1900) { throw "$($case.Name)/$mode 超时：${elapsed}ms" }
        foreach ($scene in $case.Scenes) {
            if (-not $result.Contains($scene)) { throw "$($case.Name)/$mode 场景缺少：$scene" }
        }
        foreach ($text in $case.Expected) {
            if (-not $result.Contains($text)) { throw "$($case.Name)/$mode 缺少：$text" }
        }
        if ($hits -gt $case.MaximumLabels) {
            throw "$($case.Name)/$mode 杂项过多：$hits > $($case.MaximumLabels)"
        }
        $results += [pscustomobject]@{
            模式=$mode; 场景=$case.Name; 耗时毫秒=$elapsed
            关键字段=$case.Expected.Count; 标签总数=$hits; 上限=$case.MaximumLabels; 结果='通过'
        }
    }
}

$results | Format-Table -AutoSize
Write-Output 'v3.0 公开实机回归：3 档 × 7 个高价值场景关键字段全命中、杂项数量受控（低价值背包图未计分）'
