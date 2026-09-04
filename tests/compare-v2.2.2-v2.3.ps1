param(
    [string]$BaselineRoot = 'D:\GPT\文件\枫语幕',
    [string]$CandidateRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$PublicRoot = 'D:\GPT\Codex\projects\2026-09-02\fengyumu-decky\tests\fixtures\public',
    [string]$ReportPath = 'D:\GPT\文件\枫语幕_v2.3_新旧对比.csv'
)

$ErrorActionPreference = 'Stop'
$cases = @(
    [pscustomobject]@{ Name='技能详情'; File='skill-shadow-shifter.jpg'; Cursor='514,469';
        Expected=@('假动作@','最高等级：10@','有几率躲避攻击。@','当前等级：5@','下一级：6@');
        Wrong=@('当前等级：51@','下一级：61@') },
    [pscustomobject]@{ Name='装备详情'; File='equipment-lionheart.png'; Cursor='464,399';
        Expected=@('高原之剑@','需要力量@','需要智力@','需要运气@','分类@','双手剑@','攻击速度@','物理攻击力@','可升级次数@');
        Wrong=@('移动速度@') },
    [pscustomobject]@{ Name='NPC转职对话'; File='npc-path-of-magician.png'; Cursor='681,424';
        Expected=@('想成为魔法师的人……来和我谈谈吧……@','你准备好成为像我一样的魔法师了吗？@','任务助手@','经验@');
        Wrong=@('全部@{X=435','可接取@','放弃任务@') },
    [pscustomobject]@{ Name='药水商店'; File='shop-potions.png'; Cursor='';
        Expected=@('红头巾@'); Wrong=@('全部@','怪物@','金币@') }
)

$variants = @(
    [pscustomobject]@{ Version='v2.2.2'; Mode='旧版'; Root=$BaselineRoot; Range='' },
    [pscustomobject]@{ Version='v2.3'; Mode='范围最大'; Root=$CandidateRoot; Range='maximum' },
    [pscustomobject]@{ Version='v2.3'; Mode='均衡'; Root=$CandidateRoot; Range='balanced' },
    [pscustomobject]@{ Version='v2.3'; Mode='范围最小'; Root=$CandidateRoot; Range='minimum' }
)

$rows = @()
foreach ($variant in $variants) {
    $exe = Join-Path $variant.Root '枫语幕.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "缺少对比程序：$exe" }
    foreach ($case in $cases) {
        $image = Join-Path $PublicRoot $case.File
        $arguments = @('--benchmark', "--benchmark-image=$image")
        if ($variant.Range) { $arguments += "--benchmark-range=$($variant.Range)" }
        if ($case.Cursor) { $arguments += "--benchmark-cursor=$($case.Cursor)" }
        Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $variant.Root -Wait
        $result = Get-Content (Join-Path $variant.Root 'last_run.txt') -Raw -Encoding UTF8
        $correct = @($case.Expected | Where-Object { $result.Contains($_) }).Count
        $wrong = @($case.Wrong | Where-Object { $result.Contains($_) }).Count
        $rows += [pscustomobject]@{
            版本=$variant.Version; 模式=$variant.Mode; 场景=$case.Name
            耗时毫秒=[int]([regex]::Match($result, '耗时毫秒=(\d+)').Groups[1].Value)
            总命中=[int]([regex]::Match($result, '命中数量=(\d+)').Groups[1].Value)
            关键正确=$correct; 关键总数=$case.Expected.Count
            关键项准确率=('{0:P1}' -f ($correct / [double]$case.Expected.Count))
            已知错误=$wrong
        }
    }
}

$rows | Export-Csv -LiteralPath $ReportPath -NoTypeInformation -Encoding UTF8
$rows | Format-Table -AutoSize
Write-Output "对比报告=$ReportPath"
