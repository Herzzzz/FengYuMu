$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $repoRoot '枫语幕.exe'))
$flags = [Reflection.BindingFlags]'Instance,Static,NonPublic,Public'

$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $true)
$ctor = $storeType.GetConstructor([Reflection.BindingFlags]'Instance,NonPublic,Public', $null,
    [Type[]]@([string]), $null)
if ($null -eq $ctor) { throw '未找到词库构造函数' }
$store = $ctor.Invoke([object[]]@([string](Join-Path $repoRoot '枫语幕词库.tsv')))
$storeType.GetMethod('Load', [Reflection.BindingFlags]'Instance,NonPublic,Public').Invoke($store, @()) | Out-Null

function Invoke-Store([string]$method, [object[]]$arguments) {
    $target = $storeType.GetMethod($method, $flags)
    if ($null -eq $target) { throw "词库 API 缺失：$method" }
    return $target.Invoke($store, $arguments)
}

function Get-MatchEntries($matches) {
    @($matches | ForEach-Object {
        if ($null -eq $_) { return }
        $_.GetType().GetField('Entry').GetValue($_)
    })
}

function Assert-SkillText([string]$name, [string]$english, [string]$detailId,
    [string[]]$requiredChinese) {
    $matches = @(Invoke-Store 'FindSkillTextMatches' @($english, $detailId))
    if ($matches.Count -eq 0) { throw "$name 未找到技能说明匹配：$english" }
    $entries = @(Get-MatchEntries $matches)
    foreach ($entry in $entries) {
        $category = [string]$entry.GetType().GetField('Category').GetValue($entry)
        if (-not $category.EndsWith('#' + $detailId, [StringComparison]::Ordinal)) {
            throw "$name 错配技能 ID：$category"
        }
    }
    $chinese = @($entries | ForEach-Object {
        [string]$_.GetType().GetField('Chinese').GetValue($_)
    })
    foreach ($required in $requiredChinese) {
        if (-not ($chinese | Where-Object { $_.Contains($required) })) {
            throw "$name 缺少完整译文片段：$required；实际=$($chinese -join ' | ')"
        }
    }
    return $entries
}

$recoveryId = [string](Invoke-Store 'DetectSkillId' @('Recovery'))
if ($recoveryId -ne '1001') { throw "Recovery 技能 ID 错误：$recoveryId" }

$overview = Invoke-Store 'GetSkillOverview' @($recoveryId)
if ($null -eq $overview) { throw 'Recovery 缺少技能详情说明句' }
$overviewChinese = [string]$overview.GetType().GetField('Chinese').GetValue($overview)
foreach ($required in @('最高等级：3', '可在30秒内持续恢复HP。')) {
    if (-not $overviewChinese.Contains($required)) {
        throw "Recovery 详情说明句不完整：$required；实际=$overviewChinese"
    }
}

Assert-SkillText 'Recovery完整说明句' `
    '[Master Level : 3] Enables the user to recover HP constantly for 30 sec.' `
    $recoveryId @('最高等级：3', '可在30秒内持续恢复HP。') | Out-Null
Assert-SkillText 'Recovery正文说明句' `
    'Enables the user to recover HP constantly for 30 sec.' `
    $recoveryId @('可在30秒内持续恢复HP。') | Out-Null

$levels = @(
    [pscustomobject]@{ English='MP -5; Recover HP 24 in 30 sec. Cooldown: 2 min.'; Values=@('MP -5', '24', '30 秒', '2 分钟') },
    [pscustomobject]@{ English='MP -10; Recover HP 48 in 30 sec. Cooldown: 2 min.'; Values=@('MP -10', '48', '30 秒', '2 分钟') },
    [pscustomobject]@{ English='MP -15; Recover HP 72 in 30 sec. Cooldown: 2 min.'; Values=@('MP -15', '72', '30 秒', '2 分钟') }
)
foreach ($level in $levels) {
    Assert-SkillText "Recovery动态等级 $($level.English)" $level.English $recoveryId $level.Values | Out-Null
}

# Exercise the paint-safety API with a complete Recovery sentence: semantic text must reach
# the compositor intact and retain its source Y coordinate. Single-line skill detail is
# deliberately kept single-line; only a multi-line OCR source may request wrapping.
$labelType = $assembly.GetType('MapleOverlay.OverlayLabel', $true)
$safetyType = $assembly.GetType('MapleOverlay.OverlayPaintSafety', $true)
$prepare = $safetyType.GetMethod('TryPrepare',
    [Reflection.BindingFlags]'Static,NonPublic,Public')
if ($null -eq $prepare) { throw '缺少详情标签绘制安全 API' }
$detailLabel = [Activator]::CreateInstance($labelType, $true)
$detailText = '[最高等级：3] 可在30秒内持续恢复HP。'
$labelType.GetField('Text').SetValue($detailLabel, $detailText)
$labelType.GetField('Wrap').SetValue($detailLabel, $false)
$labelType.GetField('Bounds').SetValue($detailLabel,
    (New-Object System.Drawing.RectangleF -ArgumentList 0, 37, 190, 26))
$paintArgs = New-Object 'object[]' 3
$paintArgs[0] = $detailLabel; $paintArgs[1] = $null
$paintArgs[2] = [System.Drawing.RectangleF]::Empty
if (-not [bool]$prepare.Invoke($null, $paintArgs) -or [string]$paintArgs[1] -ne $detailText) {
    throw 'Recovery 完整说明句未完整进入可绘制标签'
}
$preparedBounds = [System.Drawing.RectangleF]$paintArgs[2]
if ($preparedBounds.Width -le 0 -or $preparedBounds.Height -le 0) {
    throw "Recovery 详情标签无有效覆盖框：$preparedBounds"
}
if ([bool]$labelType.GetField('Wrap').GetValue($detailLabel)) {
    throw '单行 Recovery 详情被错误标记为可换行'
}
if ($preparedBounds.Y -ne 37) {
    throw "单行 Recovery 详情覆盖框 Y 被测量上移：$($preparedBounds.Y)"
}

# A long description may overlap a structured level field in OCR geometry. Structured fields
# are preserved instead of being removed as short contained fragments, so neighboring values
# such as 当前等级/下一级 remain visible.
$listGeneric = [System.Collections.Generic.List[object]].GetGenericTypeDefinition()
$labelListType = $listGeneric.MakeGenericType([Type[]]@($labelType))
$labels = [Activator]::CreateInstance($labelListType)
$removeContained = $assembly.GetType('MapleOverlay.OverlayForm', $true).GetMethod(
    'RemoveContainedLabels', [Reflection.BindingFlags]'Static,NonPublic,Public')
if ($null -eq $removeContained) { throw '缺少详情标签相邻字段保护 API' }
$fieldLabel = [Activator]::CreateInstance($labelType, $true)
$labelType.GetField('Text').SetValue($fieldLabel, '当前等级：1')
$labelType.GetField('Bounds').SetValue($fieldLabel,
    (New-Object System.Drawing.RectangleF -ArgumentList 0, 37, 70, 26))
$labels.Add($detailLabel)
$labels.Add($fieldLabel)
$removeArguments = New-Object 'object[]' 1
$removeArguments[0] = $labels
$removeContained.Invoke($null, $removeArguments) | Out-Null
if ($labels.Count -ne 2) {
    throw '详情说明覆盖框遮掉了相邻结构化等级字段'
}

$source = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
$detailMethod = [regex]::Match($source,
    '(?s)private void AddDetailBlockLabel\(.*?\r?\n\s*\}\r?\n\s*private static void MergeAdjacentLongLabels').Value
if ($detailMethod.Length -eq 0) { throw '缺少技能详情标签构造方法' }
if (-not ($detailMethod -match 'lines\.Count\s*>\s*1')) {
    throw '技能详情标签未按源 OCR 行数区分单行/多行'
}
if (-not ($detailMethod -match '(?i)Wrap\s*=\s*(?:wrap|isMultiLine|multiLine|lines\.Count\s*>\s*1)')) {
    throw '技能详情标签未锁定单行 Wrap=false/多行 Wrap=true 策略'
}
$paintMethod = [regex]::Match($source,
    '(?s)private void PaintOverlayLabel\(.*?\r?\n\s*\}\r?\n\s*private static GraphicsPath RoundedRect').Value
if ($paintMethod.Length -eq 0) { throw '缺少技能详情绘制方法' }
if ($paintMethod -match '(?m)\br\.Y\s*=') {
    throw '技能详情绘制测量会改写覆盖框 Y，可能把单行原文上移'
}
$skillPanel = [regex]::Match($source,
    '(?s)private void AddSkillPanelLabels\(.*?\r?\n\s*\}\r?\n\s*private static List<OcrLine> FindBestPanelProseBlock').Value
if ($skillPanel.Length -eq 0) { throw '缺少技能详情面板语义构造方法' }
foreach ($required in @(
    'SkillStructuredPrefixLength',
    'StripSkillStructuredPrefix',
    'SkillStructuredBodyStart',
    'GetOcrTextSpanBounds',
    'GetOcrTextSpanBounds(row, rowText, 0, bodyStart)',
    'string text = StripSkillStructuredPrefix(combined.ToString())',
    'GetOcrTextSpanBounds(tight, tightText, bodyStart',
    'AddDetailBlockLabelAt(output, raw',
    'overviewBody = StripSkillStructuredPrefix(overview.English)',
    'TranslationEntry fallback = overview',
    'hasStructuredPrefix',
    'fallbackAllowed = false')) {
    if (-not $source.Contains($required)) { throw "技能详情结构化字段/正文分离缺少：$required" }
}
if ($skillPanel.Contains('AddDetailBlockLabel(output, prose, overview.Chinese')) {
    throw '带结构字段的技能总览仍会直接作为最长整句覆盖字段'
}
if (-not $skillPanel.Contains('AddDetailBlockLabel(output, prose, fallback.Chinese')) {
    throw '技能总览 fallback 未使用剥离结构字段后的正文译文'
}
foreach ($required in @(
    'AddSkillPanelLabels',
    'SelectBestDetailLines(candidate.Lines, candidate.Match.Entry)',
    'List<RectangleF> used = new List<RectangleF>()',
    'RectangleF.Intersect(previous, raw)',
    'AddDetailBlockLabelAt(output, raw, candidate.Match.Entry.Chinese, ocrScale',
    'OverlayPaintSafety.TryPrepare',
    'graphics.MeasureString(labelText, overlayFont',
    'graphics.DrawString(labelText, overlayFont',
    'StringFormat.GenericTypographic',
    'IsStructuredPanelFieldLabel(labels[i].Text)',
    'covered >= 0.80f',
    'Bounds = new RectangleF(raw.Left / ocrScale',
    'raw.Top / ocrScale + captureBounds.Top - Bounds.Top')) {
    if (-not $source.Contains($required)) { throw "技能详情语义/布局保护缺少：$required" }
}

# A task pane and an inventory/equipment pane can be visible in the same frame. The item
# scene is global evidence for crop scheduling only; it must not turn task prose rows into
# equipment rows or borrow one shared equipment context.
$buildLabels = [regex]::Match($source,
    '(?s)private List<OverlayLabel> BuildLabels\(.*?\r?\n\s*\}\r?\n\s*private static void KeepHighCoverageMatches').Value
if ($buildLabels.Length -eq 0) { throw '缺少统一标签构建入口' }
$equipmentCall = [regex]::Match($buildLabels,
    '(?s)if \(TryTranslateStructuredLine\(line\.Text,.*?out structuredLine\)\)').Value
if ($equipmentCall.Length -eq 0) { throw '缺少统一结构化行翻译入口' }
if ($equipmentCall -match 'equipmentPanel\s*\|\|\s*globalEquipmentContext\s*\|\|\s*lineEquipmentStructure') {
    throw '任务与背包并存时仍把全局装备上下文直接作用于任务行'
}
if (-not $buildLabels.Contains('equipmentPanel') -or
    -not $buildLabels.Contains('lineEquipmentStructure')) {
    throw '装备语义未限制在当前装备面板或装备结构行'
}
if (-not $buildLabels.Contains('PanelContextPolicy.UseEquipmentSemantics')) {
    throw '任务与背包并存时未通过 PanelContextPolicy 隔离装备语义'
}
if (-not $buildLabels.Contains('taskTextContext = scope == LabelBuildScope.FocusedPanel')) {
    throw '任务行缺少聚焦面板上下文策略'
}

$policyType = $assembly.GetType('MapleOverlay.PanelContextPolicy', $true)
$useEquipment = $policyType.GetMethod('UseEquipmentSemantics',
    [Reflection.BindingFlags]'Static,NonPublic,Public')
if ($null -eq $useEquipment) { throw '缺少 PanelContextPolicy.UseEquipmentSemantics API' }
foreach ($case in @(
    @{ Local = $false; Explicit = $false; Expected = $false; Name = '任务/普通行' },
    @{ Local = $true; Explicit = $false; Expected = $true; Name = '当前装备面板行' },
    @{ Local = $false; Explicit = $true; Expected = $true; Name = '自标识装备结构行' })) {
    $actual = [bool]$useEquipment.Invoke($null, [object[]]@($case.Local, $case.Explicit))
    if ($actual -ne $case.Expected) {
        throw "装备上下文策略错误：$($case.Name) actual=$actual expected=$($case.Expected)"
    }
}

Write-Output 'Recovery 技能详情回归通过：说明句完整、1/2/3级动态数值保留、单行不换行、多行按源行换行、Y不因测量上移、相邻等级字段不被覆盖、任务/背包上下文隔离'
