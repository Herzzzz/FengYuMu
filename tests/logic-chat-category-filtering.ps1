$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
}
$assembly = Load-TestAssembly $exe
$all = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'

$categoryType = $assembly.GetType('MapleOverlay.ChatSourceCategory', $true)
$styleType = $assembly.GetType('MapleOverlay.ChatVisualStyle', $true)
$recordType = $assembly.GetType('MapleOverlay.ChatTranslationRecord', $true)
$historyType = $assembly.GetType('MapleOverlay.ChatTranslationHistory', $true)
$policyType = $assembly.GetType('MapleOverlay.ChatVisualStylePolicy', $true)
$historyCtor = $historyType.GetConstructor($all, $null, [Type[]]@([int]), $null)
$history = $historyCtor.Invoke(@([int]24000))
$add = $historyType.GetMethod('Add', $all)
$select = $historyType.GetMethod('Select', $all)

function New-Record([string]$translation, [string]$categoryName) {
    $record = [Activator]::CreateInstance($recordType, $true)
    $style = [Activator]::CreateInstance($styleType, $true)
    $category = [Enum]::Parse($categoryType, $categoryName)
    $styleType.GetField('Category', $all).SetValue($style, $category)
    $recordType.GetField('Original', $all).SetValue($record, "source-$translation")
    $recordType.GetField('Translation', $all).SetValue($record, $translation)
    $recordType.GetField('Style', $all).SetValue($record, $style)
    return $record
}

$add.Invoke($history, @((New-Record '普通消息' 'Normal'))) | Out-Null
$add.Invoke($history, @((New-Record '组队消息' 'Party'))) | Out-Null
$add.Invoke($history, @((New-Record '家族消息' 'Guild'))) | Out-Null

$nullableCategory = [Type]::GetType("System.Nullable``1").MakeGenericType($categoryType)
function Select-Category([string]$categoryName) {
    if ([String]::IsNullOrEmpty($categoryName)) {
        return @($select.Invoke($history, @($null)))
    }
    $category = [Enum]::Parse($categoryType, $categoryName)
    $boxed = [Activator]::CreateInstance($nullableCategory, @($category))
    return @($select.Invoke($history, @($boxed)))
}
function Get-Translations($records) {
    return @($records | ForEach-Object {
        [string]$recordType.GetField('Translation', $all).GetValue($_)
    })
}

$allTranslations = @(Get-Translations (Select-Category ''))
if ($allTranslations.Count -ne 3 -or $allTranslations -notcontains '组队消息') {
    throw "全部频道没有保留所有消息：$($allTranslations -join ' / ')"
}
$partyTranslations = @(Get-Translations (Select-Category 'Party'))
if ($partyTranslations.Count -ne 1 -or $partyTranslations[0] -ne '组队消息') {
    throw "组队频道混入其他消息：$($partyTranslations -join ' / ')"
}
$guildTranslations = @(Get-Translations (Select-Category 'Guild'))
if ($guildTranslations.Count -ne 1 -or $guildTranslations[0] -ne '家族消息') {
    throw "家族频道混入其他消息：$($guildTranslations -join ' / ')"
}

$classify = $policyType.GetMethod('ClassifySource', $all)
$chatKindType = $assembly.GetType('MapleOverlay.ChatVisualKind', $true)
$chatKind = [Enum]::Parse($chatKindType, 'Chat')
$party = $classify.Invoke($null, @('[Party] Alice: need one more',
    [Drawing.Color]::Empty, $chatKind))
$normal = $classify.Invoke($null, @('Alice: hello', [Drawing.Color]::Empty, $chatKind))
if ([string]$party -ne 'Party' -or [string]$normal -ne 'Normal') {
    throw "频道来源识别错误：$party / $normal"
}

$source = Get-Content (Join-Path $repoRoot 'src\SimpleForms.cs') -Raw -Encoding UTF8
foreach ($required in @(
    'AddFilter("全部", null);',
    'AddFilter("组队", ChatSourceCategory.Party);',
    'if (!category.HasValue || record.Style.Category == category.Value)',
    'Button button = new FlatPillButton')) {
    if (-not $source.Contains($required)) { throw "聊天分类 UI/隔离基线缺失：$required" }
}

Write-Output '聊天分类回归通过：组队信息只出现在“全部”和“组队”，不会混入普通或家族；频道栏使用无打光扁平按钮'
