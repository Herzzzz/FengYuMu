$ErrorActionPreference = 'Stop'

# Real-image regression for the consumable tooltip in
# D:\TEMP\codex-clipboard-792115d3-a3d4-48bf-803b-20be0ca6d81c.png.
# Keep this acceptance strict: the live date/quantity must not be swallowed by a
# fixed dictionary sentence.  This test deliberately reports the focused-OCR
# omission if the field has no overlay label; it must not be weakened to pass.
$repoRoot = Split-Path -Parent $PSScriptRoot
$image = 'D:\TEMP\codex-clipboard-792115d3-a3d4-48bf-803b-20be0ca6d81c.png'
if (-not (Test-Path -LiteralPath $image)) { throw "缺少真实回归图：$image" }

if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
}
$assembly = Load-TestAssembly (Join-Path $repoRoot '枫语幕.exe')
$flags = [Reflection.BindingFlags]'Instance,Static,NonPublic,Public'
$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $true)
$ctor = $storeType.GetConstructor([Reflection.BindingFlags]'Instance,NonPublic,Public', $null,
    [Type[]]@([string]), $null)
$store = $ctor.Invoke([object[]]@([string](Join-Path $repoRoot '枫语幕词库.tsv')))
$storeType.GetMethod('Load', $flags).Invoke($store, @()) | Out-Null

function Invoke-Store([string]$method, [object[]]$arguments) {
    return $storeType.GetMethod($method, $flags).Invoke($store, $arguments)
}
function Chinese([object]$match) {
    $entry = $match.GetType().GetField('Entry').GetValue($match)
    return [string]$entry.GetType().GetField('Chinese').GetValue($entry)
}
function Require-ItemMatch([string]$source, [string]$expected) {
    $matches = @(Invoke-Store 'FindItemTextMatches' @($source, $null))
    if ($matches.Count -eq 0) { throw "道具说明未命中：$source" }
    $values = @($matches | ForEach-Object { Chinese $_ })
    if ($values -notcontains $expected) {
        throw "道具说明译文错误：$source -> $($values -join ' | ')（预期 $expected）"
    }
}

$title = @(Invoke-Store 'FindMatches' @('Jr. Balrog Invasion'))
if ($title.Count -eq 0 -or (@($title | ForEach-Object { Chinese $_ }) -notcontains '蝙蝠怪入侵')) {
    throw 'Jr. Balrog Invasion 标题未命中蝙蝠怪入侵'
}
Require-ItemMatch 'Untradable' '不可交易'
Require-ItemMatch 'Usable Until: 11512027 00:36 UTC' '可使用至：'
Require-ItemMatch 'I sense an overwhelming power!' '我感受到一股强大的力量！'
Require-ItemMatch 'I sense an overtwhelming power!' '我感受到一股强大的力量！'
Require-ItemMatch 'Right-click on the buff icon to cancel the transformation.' '右键点击增益图标即可取消变身。'

# Live values are not dictionary entries and must never be replaced by a fixed
# translation row.  The prefix can match, but the number itself must stay in OCR.
if (@(Invoke-Store 'FindItemTextMatches' @('9,780', $null)).Count -ne 0) {
    throw '9,780 被错误收录为可翻译道具说明，可能吞掉数量'
}

. (Join-Path $PSScriptRoot 'Start-ProductExe.ps1')
$exe = Join-Path $repoRoot '枫语幕.exe'
Start-ProductExe -FilePath $exe -ArgumentList @('--benchmark', "--benchmark-image=$image", '--benchmark-range=balanced') -WorkingDirectory $repoRoot -Wait
$report = Get-Content -LiteralPath (Join-Path $repoRoot 'last_run.txt') -Raw -Encoding UTF8
if ($report.StartsWith('错误=')) { throw "真实道具回归执行失败：$report" }
$labels = [regex]::Match($report, '覆盖标签=([^\r\n]*)').Groups[1].Value
foreach ($expected in @('蝙蝠怪入侵', '不可交易', '我感受到一股强大的力量！', '右键点击增益图标即可取消变身。')) {
    if (-not $labels.Contains($expected)) { throw "真实道具回归缺少标签：$expected；实际=$labels" }
}
if (-not $report.Contains('Usable Until:')) {
    throw '真实道具回归 OCR 未保留 Usable Until 字段，无法验收日期保留'
}
if (-not $labels.Contains('可使用至：11512027 00:36 UTC')) {
    throw "真实道具回归未保留完整动态日期：实际=$labels"
}

Write-Output "Jr. Balrog Invasion 真实道具提示回归通过：标题、不可交易、完整动态日期、变身说明均命中，数量保持原始 OCR。"
