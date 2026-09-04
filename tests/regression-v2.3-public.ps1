param(
    [string]$PublicRoot = 'D:\GPT\Codex\projects\2026-09-02\fengyumu-decky\tests\fixtures\public'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'

$cases = @(
    [pscustomobject]@{
        Name='公开样本：技能详情 Shadow Shifter'; File='skill-shadow-shifter.jpg'; Cursor='514,469'
        Expected=@('假动作@','最高等级：10@','有几率躲避攻击。@','当前等级：5@','下一级：6@')
        Forbidden=@('当前等级：51@','下一级：61@'); Budget=1900
    },
    [pscustomobject]@{
        Name='公开样本：装备详情 Lionheart'; File='equipment-lionheart.png'; Cursor='464,399'
        # Requirement labels may now include a repaired OCR value (for example 需要智力：0).
        # Match the semantic prefix so both the frozen v2.3 label and the richer v3 label pass.
        Expected=@('高原之剑@','需要力量','需要智力','需要运气','类型：','双手剑@','攻击速度','物理攻击力','可升级次数@')
        Forbidden=@('移动速度@'); Budget=1300
    },
    [pscustomobject]@{
        Name='公开样本：NPC 转职对话'; File='npc-path-of-magician.png'; Cursor='681,424'
        Expected=@('想成为魔法师的人……来和我谈谈吧……@','你准备好成为像我一样的魔法师了吗？@','任务助手@','经验@')
        Forbidden=@('全部@{X=435','可接取@','放弃任务@'); Budget=1400
    },
    [pscustomobject]@{
        Name='公开样本：药水商店'; File='shop-potions.png'; Cursor=''
        # A list without an open tooltip is fourth priority. Keep the reliably identified
        # item name, but do not count repeated ALL/MESO or the OCR typo Honster as success.
        Expected=@('红头巾@')
        Forbidden=@('全部@','怪物@','金币@'); Budget=1300
    }
)

$results = @()
foreach ($mode in @('maximum','balanced','minimum')) {
    foreach ($case in $cases) {
        $image = Join-Path $PublicRoot $case.File
        if (-not (Test-Path -LiteralPath $image)) { throw "缺少公开样本：$image" }
        $arguments = @('--benchmark', "--benchmark-image=$image", "--benchmark-range=$mode")
        if ($case.Cursor) { $arguments += "--benchmark-cursor=$($case.Cursor)" }
        Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $repoRoot -Wait
        $result = Get-Content (Join-Path $repoRoot 'last_run.txt') -Raw -Encoding UTF8
        if ($result.StartsWith('错误=')) { throw "$($case.Name)/$mode 执行失败：$result" }
        $elapsed = [int]([regex]::Match($result, '耗时毫秒=(\d+)').Groups[1].Value)
        $hits = [int]([regex]::Match($result, '命中数量=(\d+)').Groups[1].Value)
        if ($elapsed -gt $case.Budget) { throw "$($case.Name)/$mode 超时：${elapsed}ms" }
        foreach ($text in $case.Expected) {
            if (-not $result.Contains($text)) { throw "$($case.Name)/$mode 缺少：$text" }
        }
        foreach ($text in $case.Forbidden) {
            if ($result.Contains($text)) { throw "$($case.Name)/$mode 误覆盖：$text" }
        }
        $results += [pscustomobject]@{ 模式=$mode; 场景=$case.Name; 耗时毫秒=$elapsed; 命中=$hits; 结果='通过' }
    }
}

$results | Format-Table -AutoSize
Write-Output 'v2.3 新公开样本回归：3 档 × 4 场景全部通过'
