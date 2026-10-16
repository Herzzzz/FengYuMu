$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
$assembly = Load-TestAssembly $exe
$all = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'
$clientType = $assembly.GetType('MapleOverlay.StrongOcrClient', $true)
$statusType = $assembly.GetType('MapleOverlay.StrongOcrStatus', $true)
$constructor = $clientType.GetConstructor($all, $null,
    [Type[]]@([string], [string], [int]), $null)
$recognize = $clientType.GetMethod('RecognizeAsync', $all)
$quote = $clientType.GetMethod('QuoteWindowsArgument', $all)
if ($null -eq $constructor -or $null -eq $recognize -or $null -eq $quote) {
    throw 'StrongOcrClient 可测试接口缺失'
}

$testRoot = 'D:\GPT\temp\fengyumu-strong-ocr-client-test'
if (Test-Path -LiteralPath $testRoot) {
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $testRoot | Out-Null

$fakeSource = @'
using System;
using System.IO;
using System.Threading;
class FakeStrongOcr
{
    static int Main(string[] args)
    {
        string mode = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')).Name;
        if (mode == "timeout") { Thread.Sleep(5000); return 0; }
        if (mode == "exit") { Console.Error.WriteLine("fake failure"); return 7; }
        if (mode == "malformed") { Console.WriteLine("{bad json"); return 0; }
        if (mode == "empty") {
            Console.WriteLine("{\"summary\":true,\"ok\":true,\"lines\":0}"); return 0;
        }
        if (mode == "overflow") {
            for (int i = 0; i < 540; i++) Console.WriteLine("{\"text\":\"row\",\"confidence\":0.9,\"x\":1,\"y\":1,\"width\":2,\"height\":2}");
            Console.WriteLine("{\"summary\":true,\"ok\":true,\"lines\":540}"); return 0;
        }
        if (mode == "stderr") {
            for (int i = 0; i < 540; i++) Console.Error.WriteLine("diagnostic line " + i);
            Console.WriteLine("{\"summary\":true,\"ok\":true,\"lines\":0}"); return 0;
        }
        Console.WriteLine("{\"text\":\"quote \\\" backslash \\\\ 中文\\tline\",\"confidence\":0.95,\"x\":-5,\"y\":2,\"width\":30,\"height\":12}");
        Console.WriteLine("{\"text\":\"quote \\\" backslash \\\\ 中文\\tline\",\"confidence\":0.95,\"x\":0,\"y\":2,\"width\":25,\"height\":12}");
        Console.WriteLine("{\"summary\":true,\"ok\":true,\"lines\":2,\"load_ms\":12,\"run_ms\":34}");
        return 0;
    }
}
'@
$sourcePath = Join-Path $testRoot 'FakeStrongOcr.cs'
Set-Content -LiteralPath $sourcePath -Value $fakeSource -Encoding UTF8
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$modes = @('good path 中文', 'timeout', 'exit', 'malformed', 'empty', 'overflow', 'stderr')
foreach ($mode in $modes) {
    $folder = Join-Path $testRoot $mode
    New-Item -ItemType Directory -Path $folder | Out-Null
    $helper = Join-Path $folder 'StrongOcrHelper.exe'
    & $csc /nologo /target:exe /out:$helper $sourcePath
    if ($LASTEXITCODE -ne 0) { throw "假 helper 编译失败：$mode" }
    New-Item -ItemType File -Path (Join-Path $folder 'Sdcb.SimdPaddleOCR.dll') | Out-Null
    New-Item -ItemType File -Path (Join-Path $folder 'Sdcb.SimdPaddleOCR.Models.ChineseV6Small.dll') | Out-Null
    Get-ChildItem -LiteralPath $folder -File | Sort-Object Name | ForEach-Object {
        $_.Name + "`t" + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    } | Set-Content -LiteralPath (Join-Path $folder 'component-manifest.tsv') -Encoding UTF8
}

function Invoke-StrongOcrCase([string]$mode, [int]$timeout = 1200) {
    $folder = Join-Path $testRoot $mode
    $temp = Join-Path $folder '临时 图像'
    [string]$helperPath = Join-Path $folder 'StrongOcrHelper.exe'
    [string]$tempPath = $temp
    $client = $constructor.Invoke([object[]]@($helperPath, $tempPath, [int]$timeout))
    [System.Drawing.Bitmap]$bitmap = New-Object System.Drawing.Bitmap -ArgumentList 40, 20
    try {
        $task = $recognize.Invoke($client, [object[]](,$bitmap))
        $null = $task.GetAwaiter().GetResult()
        $result = $task.Result
    }
    finally { $bitmap.Dispose() }
    $leftovers = @(Get-ChildItem -LiteralPath $temp -File -ErrorAction SilentlyContinue)
    if ($leftovers.Count -ne 0) { throw "$mode 未清理临时 PNG" }
    return $result
}

$good = Invoke-StrongOcrCase 'good path 中文'
if ($good.Status.ToString() -ne 'Success') { throw "中文路径成功用例失败：$($good.Reason)" }
if ($good.Lines.Count -ne 1) { throw "重复/越界裁剪错误：$($good.Lines.Count)" }
if ($good.Lines[0].Bounds.X -ne 0 -or $good.Lines[0].Bounds.Right -gt 40) {
    throw '强 OCR 框未裁剪到输入图片'
}
if ($good.Lines[0].Text -notmatch 'quote " backslash \\ 中文') {
    throw "JSON 转义往返失败：$($good.Lines[0].Text)"
}
if ($good.ModelLoadMilliseconds -ne 12 -or $good.RecognitionMilliseconds -ne 34) {
    throw '摘要耗时解析失败'
}

$expected = @{
    'timeout' = 'TimedOut'; 'exit' = 'ProcessFailed';
    'malformed' = 'InvalidOutput'; 'empty' = 'InvalidOutput';
    'overflow' = 'InvalidOutput'; 'stderr' = 'InvalidOutput'
}
foreach ($mode in $expected.Keys) {
    $limit = if ($mode -eq 'timeout') { 80 } else { 1200 }
    $result = Invoke-StrongOcrCase $mode $limit
    if ($result.Status.ToString() -ne $expected[$mode]) {
        throw "$mode 状态错误：$($result.Status) / $($result.Reason)"
    }
    if ($result.Lines.Count -ne 0) { throw "$mode 失败时泄漏识别行" }
}
Start-Sleep -Milliseconds 100
if (@(Get-Process -Name StrongOcrHelper -ErrorAction SilentlyContinue).Count -ne 0) {
    throw '超时后仍残留 StrongOcrHelper 进程'
}

[string]$missingHelper = Join-Path $testRoot 'missing\StrongOcrHelper.exe'
[string]$missingTemp = Join-Path $testRoot 'missing-temp'
$missing = $constructor.Invoke([object[]]@($missingHelper, $missingTemp, [int]100))
[System.Drawing.Bitmap]$bitmap = New-Object System.Drawing.Bitmap -ArgumentList 10, 10
try {
    $task = $recognize.Invoke($missing, [object[]](,$bitmap)); $null = $task.GetAwaiter().GetResult()
    if ($task.Result.Status.ToString() -ne 'Unavailable') { throw '缺失 helper 未安全降级' }
}
finally { $bitmap.Dispose() }

$quoted = [string]$quote.Invoke($null, @('D:\有 空格\a"b\'))
if ($quoted -ne '"D:\有 空格\a\"b\\"') { throw "Windows 参数转义错误：$quoted" }

# A task dictionary may contain several journal states under one task id. The visible state must
# beat a longer aggregate row that merely shares its prefix; otherwise the overlay prints accepted,
# in-progress and completed prose at once.
$overlayType = $assembly.GetType('MapleOverlay.OverlayForm', $true)
$entryType = $assembly.GetType('MapleOverlay.TranslationEntry', $true)
$matchType = $assembly.GetType('MapleOverlay.MatchResult', $true)
$scoreLong = $overlayType.GetMethod('ScoreStrongLongTextCandidate', $all)
if ($null -eq $scoreLong) { throw '强 OCR 长正文状态评分器缺失' }
$visible = 'winston lost 12 animal fossils and 12 plant fossils near perion'
$aggregate = $visible + ' before accepting the quest and after completing it returned to report the recovered fossils'
function New-LongTextMatch([string]$entryText) {
    $entry = [Activator]::CreateInstance($entryType, $true)
    $entryType.GetField('English', $all).SetValue($entry, $entryText)
    $entryType.GetField('Normalized', $all).SetValue($entry, $entryText)
    $match = [Activator]::CreateInstance($matchType, $true)
    $matchType.GetField('Entry', $all).SetValue($match, $entry)
    $matchType.GetField('Start', $all).SetValue($match, 0)
    $matchType.GetField('Length', $all).SetValue($match, $visible.Length)
    return $match
}
$alignedScore = [int]$scoreLong.Invoke($null,
    [object[]]@($visible, $visible, (New-LongTextMatch $visible), [single]0.9))
$aggregateScore = [int]$scoreLong.Invoke($null,
    [object[]]@($visible, $visible, (New-LongTextMatch $aggregate), [single]0.9))
if ($alignedScore -eq [int]::MinValue -or $alignedScore -le $aggregateScore) {
    throw "长正文评分仍偏向混合任务状态：aligned=$alignedScore aggregate=$aggregateScore"
}

$panelType = $assembly.GetType('MapleOverlay.PanelCropCandidate', $true)
$priorityType = $assembly.GetType('MapleOverlay.RecognitionPriorityKind', $true)
$panelEligible = $overlayType.GetMethod('ShouldRunStrongPanelOcr', $all)
if ($null -eq $panelEligible) { throw '强 OCR 面板调度器缺失' }
function Test-StrongPanel([string]$kind, [string]$priority, [bool]$expected) {
    $panel = [Activator]::CreateInstance($panelType, $true)
    $panelType.GetField('Kind', $all).SetValue($panel, $kind)
    $value = [Enum]::Parse($priorityType, $priority)
    $panelType.GetField('PriorityKind', $all).SetValue($panel, $value)
    $actual = [bool]$panelEligible.Invoke($null, [object[]]@(,$panel))
    if ($actual -ne $expected) {
        throw "强 OCR 面板调度错误：$kind/$priority actual=$actual expected=$expected"
    }
}
Test-StrongPanel 'quest' 'Dialogue' $true
Test-StrongPanel 'quest' 'OutsideDialogue' $false
Test-StrongPanel 'dialogue' 'Dialogue' $true
Test-StrongPanel 'item' 'Detail' $true
Test-StrongPanel 'skill' 'Detail' $true
Test-StrongPanel 'skill' 'Dialogue' $false
Test-StrongPanel 'interface' 'Dialogue' $false
Test-StrongPanel 'legacy' 'Dialogue' $false

$labelType = $assembly.GetType('MapleOverlay.OverlayLabel', $true)
$listType = [Collections.Generic.List``1].MakeGenericType($labelType)
$needsTooltip = $overlayType.GetMethod('NeedsStrongTooltipReview', $all)
if ($null -eq $needsTooltip) { throw '强 OCR 光标详情回退判定器缺失' }
function New-FastLabels([string[]]$texts) {
    $list = [Activator]::CreateInstance($listType)
    foreach ($text in $texts) {
        $label = [Activator]::CreateInstance($labelType, $true)
        $labelType.GetField('Text', $all).SetValue($label, $text)
        $null = $list.Add($label)
    }
    return ,$list
}
$structuredSource = 'REQ LEV 35 REQ STR 115 REQ DEX 0 REQ INT 0 REQ LUK 0 CATEGORY TWO HANDED SWORD ATTACK SPEED FAST'
$enoughStructured = New-FastLabels @('需要等级：35','需要力量：115','需要敏捷：0','需要智力：0','需要运气：0')
if ([bool]$needsTooltip.Invoke($null, [object[]]@($structuredSource, $enoughStructured))) {
    throw '已完整识别的装备详情仍错误启动强 OCR'
}
$longSource = 'This is a long skill description that wraps over several rows and explains the complete effect in detail for the current level.'
if (-not [bool]$needsTooltip.Invoke($null,
    [object[]]@($longSource, (New-FastLabels @('当前等级：5'))))) {
    throw '缺少正文的长详情未触发强 OCR 回退'
}
if ([bool]$needsTooltip.Invoke($null,
    [object[]]@($longSource, (New-FastLabels @('当前等级下会完整说明技能效果、触发概率、持续时间并保留所有动态数值。'))))) {
    throw '已有完整长正文仍错误启动强 OCR'
}
$dialogueSource = 'To all that desire to become a Magician talk to me. Are you ready to become a Magician like me?'
$dialogueLabels = New-FastLabels @('想成为魔法师的人……来和我谈谈吧……','你准备好成为像我一样的魔法师了吗？')
if ([bool]$needsTooltip.Invoke($null, [object[]]@($dialogueSource, $dialogueLabels))) {
    throw '已完整识别的双句 NPC 对话仍错误启动强 OCR'
}

$source = Get-Content -LiteralPath (Join-Path $repoRoot 'src\StrongOcrClient.cs') -Raw -Encoding UTF8
foreach ($forbidden in @('ReadProcessMemory', 'WriteProcessMemory', 'OpenProcess(',
        'SetWindowsHookEx', 'SendInput(', 'CreateRemoteThread')) {
    if ($source.Contains($forbidden)) { throw "强 OCR 客户端出现高风险 API：$forbidden" }
}

Write-Host 'PASS: 强 OCR 客户端 JSON/中文路径/超时/进程失败/坏输出/上限/清理/安全降级'
