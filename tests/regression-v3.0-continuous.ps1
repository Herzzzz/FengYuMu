param(
    [string]$PublicRoot = (Join-Path $PSScriptRoot 'fixtures\public-v3')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'

$cases = @(
    [pscustomobject]@{ Name='技能'; File='skill-tab-triple-throw.png'; Scene='技能:'; Expected='暗器伤人@' },
    [pscustomobject]@{ Name='装备25'; File='equipment-rat-mouth.png'; Scene='装备物品:'; Expected='需要等级：25@' },
    [pscustomobject]@{ Name='装备50'; File='equipment-crimsonheart-cloak.png'; Scene='装备物品:'; Expected='需要等级：50@' },
    [pscustomobject]@{ Name='低分辨率人物'; File='character-stats-warrior.png'; Scene='人物视觉:'; Expected='能力值点数@' },
    [pscustomobject]@{ Name='人物与装备'; File='character-equipment-dark-knight.png'; Scene='人物视觉:'; Expected='物理攻击力：66@' },
    [pscustomobject]@{ Name='任务'; File='quest-dr-kim.png'; Scene='任务:'; Expected='任务助手@' },
    [pscustomobject]@{ Name='NPC对话'; File='npc-jack-dialogue.png'; Scene='NPC对话:'; Expected='打起精神来，高手！@' }
)

$results = @()
foreach ($case in $cases) {
    $image = Join-Path $PublicRoot $case.File
    if (-not (Test-Path -LiteralPath $image)) { throw "缺少公开样本：$image" }
    Start-Process -FilePath $exe -ArgumentList @('--benchmark','--benchmark-scene-probe',
        '--benchmark-range=balanced', "--benchmark-image=$image") -WorkingDirectory $repoRoot -Wait
    $result = Get-Content (Join-Path $repoRoot 'last_run.txt') -Raw -Encoding UTF8
    if ($result.StartsWith('错误=')) { throw "$($case.Name) 自动探测失败：$result" }
    $elapsed = [int]([regex]::Match($result, '耗时毫秒=(\d+)').Groups[1].Value)
    $probe = [int]([regex]::Match($result, '快速探测:(\d+)ms').Groups[1].Value)
    if ($elapsed -gt 2200) { throw "$($case.Name) 持续模式响应超时：${elapsed}ms" }
    if (-not $result.Contains('快速探测[') -or -not $result.Contains('精确识别[')) {
        throw "$($case.Name) 未执行两阶段识别"
    }
    if (-not $result.Contains($case.Scene)) { throw "$($case.Name) 场景缺少：$($case.Scene)" }
    if (-not $result.Contains($case.Expected)) { throw "$($case.Name) 翻译缺少：$($case.Expected)" }
    $results += [pscustomobject]@{ 场景=$case.Name; 探测毫秒=$probe; 总响应毫秒=$elapsed; 结果='通过' }
}

Start-Process -FilePath $exe -ArgumentList @('--benchmark','--benchmark-scene-probe') `
    -WorkingDirectory $repoRoot -Wait
$idle = Get-Content (Join-Path $repoRoot 'last_run.txt') -Raw -Encoding UTF8
$idleElapsed = [int]([regex]::Match($idle, '耗时毫秒=(\d+)').Groups[1].Value)
if (-not $idle.Contains('快速探测[普通界面]') -or -not $idle.Contains('命中数量=0')) {
    throw '无面板画面没有在快速探测阶段停止'
}
if ($idle.Contains('精确识别[') -or $idleElapsed -gt 350) {
    throw "无面板画面仍执行昂贵 OCR 或响应过慢：${idleElapsed}ms"
}

$results | Format-Table -AutoSize
Write-Output "持续自动翻译公开实机回归：7/7 场景通过；无面板 ${idleElapsed}ms 即停止，未进入昂贵 OCR"
