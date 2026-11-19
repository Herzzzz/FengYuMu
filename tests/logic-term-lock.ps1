$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "找不到程序集：$exe" }

if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
}
$assembly = Load-TestAssembly $exe
$all = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'

function Invoke-Method($method, $target, [object[]]$arguments) {
    if ($target -is [System.Management.Automation.PSObject]) {
        $target = $target.PSObject.BaseObject
    }
    for ($i = 0; $i -lt $arguments.Length; $i++) {
        if ($arguments[$i] -is [System.Management.Automation.PSObject]) {
            $arguments[$i] = $arguments[$i].PSObject.BaseObject
        }
    }
    return $method.Invoke($target, $arguments)
}

# ---------------------------------------------------------------------------
# A. Independent paragraph-level terminology protection. Real Protect/Restore behaviour, not
# source-string checks.
# ---------------------------------------------------------------------------
$moduleType = $assembly.GetType('MapleOverlay.TranslationTerminology', $true)
$tableType = $assembly.GetType('MapleOverlay.TerminologyTable', $true)
$entryType = $assembly.GetType('MapleOverlay.TerminologyEntry', $true)
$categoryType = $assembly.GetType('MapleOverlay.TerminologyCategory', $true)
if ($null -eq $moduleType -or $null -eq $tableType -or $null -eq $entryType) {
    throw '缺少 TranslationTerminology 模块'
}
$listDefinition = [object].Assembly.GetType('System.Collections.Generic.List`1')
if ($null -eq $listDefinition) { throw '无法解析 List<T> 泛型定义' }
$stringListType = $listDefinition.MakeGenericType([string])
$entryListType = $listDefinition.MakeGenericType($entryType)

$buildMethod = $tableType.GetMethod('Build', $all)
$protectMethod = $moduleType.GetMethod('TryProtect', $all)
$restoreMethod = $moduleType.GetMethod('Restore', $all)
if ($null -eq $buildMethod -or $null -eq $protectMethod -or $null -eq $restoreMethod) {
    throw '缺少 Build/TryProtect/Restore 接口'
}

function New-Entry([string]$english, [string]$chinese, [string]$category) {
    $entry = [Activator]::CreateInstance($entryType, $true)
    $entryType.GetField('English', $all).SetValue($entry, $english)
    $entryType.GetField('Chinese', $all).SetValue($entry, $chinese)
    $entryType.GetField('Category', $all).SetValue($entry, [Enum]::Parse($categoryType, $category))
    return $entry
}
function New-Table($entries) {
    $list = [Activator]::CreateInstance($entryListType)
    foreach ($entry in $entries) { $list.Add($entry) }
    $ambiguous = [Activator]::CreateInstance($stringListType)
    $arguments = New-Object 'object[]' 2
    $arguments[0] = $list
    $arguments[1] = $ambiguous
    $table = Invoke-Method $buildMethod $null $arguments
    return @($table, $ambiguous)
}
function Get-Protected([string]$text, $table) {
    $arguments = New-Object 'object[]' 4
    $arguments[0] = $text
    $arguments[1] = $table
    $arguments[2] = $null
    $arguments[3] = $null
    $ok = [bool](Invoke-Method $protectMethod $null $arguments)
    return @($ok, $arguments[2], $arguments[3])
}
function Get-Restored([string]$translated, $segment) {
    $arguments = New-Object 'object[]' 2
    $arguments[0] = $translated
    $arguments[1] = $segment
    return Invoke-Method $restoreMethod $null $arguments
}

# 1. Canonical categories only; the two required terms resolve to their canonical Chinese.
$tableInfo = New-Table @(
    (New-Entry 'Aquamarine' '海蓝宝石' 'Item'),
    (New-Entry 'Slime Bubble' '绿水灵珠' 'Monster'),
    (New-Entry 'Slime' '史莱姆' 'Monster'),
    (New-Entry 'he' '他' 'Npc'))
$table = $tableInfo[0]
if ([int]$tableType.GetProperty('Count', $all).GetValue($table) -lt 3) { throw ("规范术语表条目过少：" + [int]$tableType.GetProperty('Count', $all).GetValue($table)) }

# 2. A whole paragraph keeps its context: the protected text carries placeholders, contains no
#    Chinese at all, and both terms survive the restore.
$paragraph = 'Winston needs 12 Aquamarine and a Slime Bubble from the cave, do not forget.'
$protected = Get-Protected $paragraph $table
if (-not $protected[0]) { throw ('保护阶段失败：' + $protected[2]) }
$segment = $protected[1]
if (-not $segment.HasTerminology) { throw '任务段落没有被识别出术语' }
if ($segment.ProtectedText -match '[\u4e00-\u9fff]') { throw '保护阶段插入了中文，会切碎语境' }
if ($segment.ProtectedText.IndexOf([char]0xE000) -lt 0) {
    throw ('保护文本不含术语占位符：' + $segment.ProtectedText)
}
if ($segment.TermHint -notmatch 'Aquamarine=海蓝宝石') { throw ('术语提示缺少 Aquamarine：' + $segment.TermHint) }
if ($segment.TermHint -notmatch 'Slime Bubble=绿水灵珠') { throw ('术语提示缺少 Slime Bubble：' + $segment.TermHint) }

# Simulate the translator: it returns the protected sentence with the canonical Chinese dropped in.
# The translator keeps the placeholders and translates the rest of the sentence as one piece.
$translated = '温斯顿需要 '
foreach ($placeholder in $segment.Placeholders) { $translated += $placeholder.Token + ' ' }
$translated += '别忘了。'
$restored = Get-Restored $translated $segment
if (-not $restored.Success) { throw ('恢复失败：' + $restored.Reason) }
if ($restored.Text -notmatch '海蓝宝石') { throw ('Aquamarine 未恢复为海蓝宝石：' + $restored.Text) }
if ($restored.Text -notmatch '绿水灵珠') { throw ('Slime Bubble 未恢复为绿水灵珠：' + $restored.Text) }
if ($restored.Text -match '史莱姆') { throw '最长匹配失败：Slime 抢先命中并覆盖了 Slime Bubble' }
if ($restored.Text -notmatch '别忘了') { throw '整段上下文在恢复后丢失' }

# 3. Word boundary: "he" must not lock the "he" inside "helper".
$boundary = Get-Protected 'helper he looks' $table
if ($boundary[1].Placeholders.Count -ne 1) {
    throw ("词边界失败，锁定 " + $boundary[1].Placeholders.Count + " 段")
}

# 4. Ambiguous English is skipped and diagnosed.
$ambiguousInfo = New-Table @(
    (New-Entry 'Aquamarine' '海蓝宝石' 'Item'),
    (New-Entry 'Aquamarine' '水蓝宝石' 'Item'))
if ([int]$tableType.GetProperty('Count', $all).GetValue($ambiguousInfo[0]) -ne 0) { throw '歧义英文被当作规范术语锁定' }
if ($ambiguousInfo[1].Count -lt 1) { throw '歧义英文没有导出诊断信息' }
$ambiguousProtected = Get-Protected 'Aquamarine ore' $ambiguousInfo[0]
if ($ambiguousProtected[1].Placeholders.Count -ne 0) { throw '歧义英文仍然被锁定' }

# Count and Lookup are internal members, so they are reached through reflection.
function Get-TableCount($table) {
    return [int]$tableType.GetProperty('Count', $all).GetValue($table)
}
function Get-Lookup($table, [string]$key) {
    return $tableType.GetMethod('Lookup', $all).Invoke($table, @($key))
}
# 4b. Permanent ambiguity: four duplicate-mapping sequences must never re-add the key.
function Get-AmbiguousTable($entries) { return New-Table $entries }
$lateAmbiguity = Get-AmbiguousTable @(
    (New-Entry 'Mushroom' '蘑菇' 'Monster'),
    (New-Entry 'Mushroom' '香菇' 'Monster'),
    (New-Entry 'Mushroom' '香菇' 'Monster'),
    (New-Entry 'Mushroom' '香菇' 'Monster'))
if ((Get-TableCount $lateAmbiguity[0]) -ne 0) {
    throw ('歧义键在后续重复条目后重新进入规范表：' + (Get-TableCount $lateAmbiguity[0]))
}
if ($lateAmbiguity[1].Count -lt 1) { throw '迟到的歧义没有导出诊断' }
$lateLookup = $tableType.GetMethod('Lookup', $all).Invoke($lateAmbiguity[0], @('mushroom'))
if ($null -ne $lateLookup) { throw ('歧义键仍可被查表命中：' + $lateLookup) }

$reordered = Get-AmbiguousTable @(
    (New-Entry 'Slime' '史莱姆' 'Monster'),
    (New-Entry 'Slime' '史莱姆' 'Monster'),
    (New-Entry 'Slime' '黏液怪' 'Monster'),
    (New-Entry 'Slime' '黏液怪' 'Monster'))
if ((Get-TableCount $reordered[0]) -ne 0) { throw '歧义在第二轮重复后才出现却未被永久剔除' }

$mixed = Get-AmbiguousTable @(
    (New-Entry 'Aquamarine' '海蓝宝石' 'Item'),
    (New-Entry 'Aquamarine' '水蓝宝石' 'Item'),
    (New-Entry 'Aquamarine' '水蓝宝石' 'Item'),
    (New-Entry 'Aquamarine' '水蓝宝石' 'Item'),
    (New-Entry 'Slime Bubble' '绿水灵珠' 'Monster'))
if ((Get-TableCount $mixed[0]) -ne 1) {
    throw ('歧义键与正常键混合时条目数错误：' + (Get-TableCount $mixed[0]))
}
if ($null -ne $tableType.GetMethod('Lookup', $all).Invoke($mixed[0], @('aquamarine'))) {
    throw '歧义键与正常键混合时歧义键仍可命中'
}
if ($tableType.GetMethod('Lookup', $all).Invoke($mixed[0], @('slime bubble')) -ne '绿水灵珠') {
    throw '正常键在歧义键存在时丢失'
}

$sixDuplicates = Get-AmbiguousTable @(
    (New-Entry 'Ribbon Pig' '缎带猪' 'Monster'),
    (New-Entry 'Ribbon Pig' '蝴蝶结猪' 'Monster'),
    (New-Entry 'Ribbon Pig' '蝴蝶结猪' 'Monster'),
    (New-Entry 'Ribbon Pig' '蝴蝶结猪' 'Monster'),
    (New-Entry 'Ribbon Pig' '蝴蝶结猪' 'Monster'),
    (New-Entry 'Ribbon Pig' '缎带猪' 'Monster'))
if ((Get-TableCount $sixDuplicates[0]) -ne 0) { throw '六条重复映射后歧义键复活' }
if ($null -ne $tableType.GetMethod('Lookup', $all).Invoke($sixDuplicates[0], @('ribbon pig'))) {
    throw '六条重复映射后歧义键仍可命中'
}

# 5. Counters, levels and percentages come back verbatim.
$values = Get-Protected 'Collect 5/12 fossils at Level 17+ for 60% progress' $table
$valueSegment = $values[1]
foreach ($expected in @('5/12', 'Level 17+', '60%')) {
    $found = $false
    foreach ($placeholder in $valueSegment.Placeholders) {
        if ($placeholder.Source -eq $expected) { $found = $true; break }
    }
    if (-not $found) { throw ("数值/等级/百分比没有被保护：" + $expected) }
}
$valueTranslated = '收集 '
foreach ($placeholder in $valueSegment.Placeholders) { $valueTranslated += $placeholder.Token + ' ' }
$valueTranslated += '的进度'
$valueRestored = Get-Restored $valueTranslated $valueSegment
if (-not $valueRestored.Success) { throw ('数值恢复失败：' + $valueRestored.Reason) }
foreach ($expected in @('5/12', 'Level 17+', '60%')) {
    if ($valueRestored.Text -notmatch [regex]::Escape($expected)) {
        throw ("数值/等级/百分比没有被原样恢复：" + $expected)
    }
}

# 6. Missing, duplicated and tampered placeholders must fail safely with a reason.
$baseSegment = (Get-Protected 'Bring 12 Aquamarine' $table)[1]
$missing = Get-Restored 'Bring 12 items' $baseSegment
if ($missing.Success) { throw '占位符缺失却恢复了' }
if ([string]$missing.Reason -eq '') { throw '占位符缺失没有给出原因' }
if ([string]$missing.Text -ne '') { throw '失败时返回了半恢复文本' }

$token = $baseSegment.Placeholders[0].Token
$duplicated = Get-Restored ('Bring ' + $token + ' and ' + $token) $baseSegment
if ($duplicated.Success) { throw '占位符重复却恢复了' }
if ([string]$duplicated.Text -ne '') { throw '占位符重复时返回了半恢复文本' }

$broken = $token.Substring(0, $token.Length - 1) + [char]0x0078
$tampered = Get-Restored ($baseSegment.ProtectedText.Replace($token, $broken)) $baseSegment
if ($tampered.Success) { throw '占位符被篡改却恢复了' }
if ([string]$tampered.Text -ne '') { throw '占位符篡改时返回了半恢复文本' }

# 7. An ordinary sentence is not force-gamified.
$plain = Get-Protected 'Good morning, have a nice day.' $table
if ($plain[1].HasTerminology) { throw '普通句被强行游戏化' }
if ($plain[1].ProtectedText -ne 'Good morning, have a nice day.') { throw '普通句被改写' }
$plainRestore = Get-Restored $plain[1].ProtectedText $plain[1]
if (-not $plainRestore.Success -or $plainRestore.Text -ne 'Good morning, have a nice day.') {
    throw '普通句在无占位符时无法原样通过'
}

Write-Output ('术语锁定 A：Aquamarine=海蓝宝石、Slime Bubble=绿水灵珠（最长匹配优先于 Slime）、' +
    '整段保护不插入中文且保持上下文完整、词边界（he 不命中 helper）、歧义英文跳过并诊断、' +
    '5/12 与 Level 17+ 与 60% 原样恢复、占位符缺失/重复/篡改安全失败且不返回半恢复文本、' +
    '普通句不被强行游戏化')
