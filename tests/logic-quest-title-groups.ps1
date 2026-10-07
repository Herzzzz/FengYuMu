$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$assemblyPath = Join-Path $repoRoot '枫语幕.exe'
$dictionaryPath = Join-Path $repoRoot '枫语幕词库.tsv'
if (-not (Test-Path -LiteralPath $assemblyPath)) { throw "找不到现有程序集：$assemblyPath" }
if (-not (Test-Path -LiteralPath $dictionaryPath)) { throw "找不到词库：$dictionaryPath" }

# This is a dictionary-only regression test. It intentionally loads the existing EXE
# and never builds or edits src, so it does not disturb another agent's source changes.
$assembly = [Reflection.Assembly]::LoadFile($assemblyPath)
$flags = [Reflection.BindingFlags]'Instance,NonPublic,Public'
$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $true)
$storeCtor = $storeType.GetConstructor($flags, $null, [Type[]]@([string]), $null)
if ($null -eq $storeCtor) { throw '未找到词库构造函数' }
$store = $storeCtor.Invoke(@([string]$dictionaryPath))
$storeType.GetMethod('Load', $flags).Invoke($store, @()) | Out-Null

$detectTaskId = $storeType.GetMethod('DetectTaskId', $flags)
$resolveTitle = $storeType.GetMethod('ResolveUniqueTaskTitleId', $flags)
$findMatches = $storeType.GetMethod('FindMatches', $flags)
$findTaskNames = $storeType.GetMethod('FindTaskNameMatches', $flags)
if ($null -eq $detectTaskId -or $null -eq $resolveTitle -or
    $null -eq $findMatches -or $null -eq $findTaskNames) {
    throw 'EXE 缺少任务标题/词库匹配 API'
}

function Get-FieldValue($value, [string]$fieldName) {
    if ($null -eq $value) { return $null }
    $field = $value.GetType().GetField($fieldName, $script:flags)
    if ($null -eq $field) { throw "对象缺少字段：$fieldName" }
    return $field.GetValue($value)
}

function Get-Rows([string]$english) {
    if ($script:rowsByEnglish.ContainsKey($english)) {
        return @($script:rowsByEnglish[$english])
    }
    return @()
}

function Assert-Equal([string]$actual, [string]$expected, [string]$label) {
    if ($actual -ne $expected) {
        throw "$label：实际 [$actual]，预期 [$expected]"
    }
}

# Keep a raw view as well as the runtime view. This proves the group category has no
# '#' suffix, while reflection proves the loaded entry carries an empty TaskId.
$rowsByEnglish = @{}
foreach ($line in [IO.File]::ReadLines($dictionaryPath, [Text.Encoding]::UTF8)) {
    if ([String]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#')) { continue }
    $parts = $line -split "`t"
    if ($parts.Count -lt 3) { continue }
    $english = $parts[0].Trim()
    if (-not $rowsByEnglish.ContainsKey($english)) { $rowsByEnglish[$english] = @() }
    $rowsByEnglish[$english] = @($rowsByEnglish[$english]) + (, $parts)
}

$groupTitles = @(
    @{ English = "Father's Mushroom Stew"; Chinese = '父亲的蘑菇炖汤' },
    @{ English = "Maple World's Unique Fun: Match Cards"; Chinese = '冒险岛世界的独特乐趣：记忆卡' },
    @{ English = "Maple World's Unique Fun: Omok"; Chinese = '冒险岛世界的独特乐趣：五子棋' },
    @{ English = "Winston's Fossil Dig-up"; Chinese = '芳博士的化石挖掘' }
)

foreach ($spec in $groupTitles) {
    $rows = @(Get-Rows $spec.English)
    if ($rows.Count -ne 1) { throw "任务组标题行数异常：$($spec.English) -> $($rows.Count)" }
    $row = $rows[0]
    Assert-Equal $row[1] $spec.Chinese "任务组译文：$($spec.English)"
    Assert-Equal $row[2] '怀旧服-任务组' "任务组分类：$($spec.English)"
    if ($row[2].Contains('#')) { throw "任务组不应带 taskId：$($spec.English)" }

    $detected = [string]$detectTaskId.Invoke($store, @([string]$spec.English))
    Assert-Equal $detected '' "任务组不得分配 taskId：$($spec.English)"
    $nameMatches = @($findTaskNames.Invoke($store, @([string]$spec.English)))
    if ($nameMatches.Count -ne 0) {
        throw "任务组不应进入具体任务标题索引：$($spec.English)"
    }

    $allMatches = @($findMatches.Invoke($store, @([string]$spec.English)))
    $groupEntries = @()
    foreach ($match in $allMatches) {
        $entry = Get-FieldValue $match 'Entry'
        if ([string](Get-FieldValue $entry 'Category') -eq '怀旧服-任务组') {
            $groupEntries += ,$entry
        }
    }
    if ($groupEntries.Count -ne 1) {
        throw "运行时未找到唯一任务组匹配：$($spec.English) -> $($groupEntries.Count)"
    }
    Assert-Equal ([string](Get-FieldValue $groupEntries[0] 'TaskId')) '' "运行时任务组 TaskId：$($spec.English)"
    if ([bool](Get-FieldValue $groupEntries[0] 'IsTaskName') -or
        [bool](Get-FieldValue $groupEntries[0] 'IsTaskText')) {
        throw "任务组错误进入任务索引：$($spec.English)"
    }
}

$tasks = @(
    @{ Id = '80026'; English = 'Welcome to the Free Market!'; Chinese = '欢迎来到自由市场！'; Description = "Lewis in the Free Market Entrance is looking for an adventurer to help him. It seems the Free Market can be accessed through Henesys or Perion. Lewis in the Free Market Entrance asked me to deliver a letter to Chief Stan in Henesys to help revitalize the Free Market. I delivered Lewis's letter to Chief Stan in Henesys." },
    @{ Id = '80027'; English = "Chief Stan's Reply"; Chinese = '长老斯坦的回信'; Description = "Chief Stan in Henesys seems to want to send a reply to Lewis in the Free Market Entrance. Chief Stan in Henesys asked me to deliver a reply to Lewis in the Free Market Entrance. I delivered Chief Stan's letter to Lewis in the Free Market Entrance." },
    @{ Id = '80028'; English = 'Asking Perion for Help'; Chinese = '向勇士部落求助'; Description = "Lewis in the Free Market Entrance seems to have another favor. This time, Lewis in the Free Market Entrance asked me to deliver a letter to Dances with Balrog in Perion. I delivered Lewis's letter to Dances with Balrog in Perion." },
    @{ Id = '80029'; English = "Dances with Balrog's Reply"; Chinese = '武术教练的回信'; Description = "Dances with Balrog in Perion seems to want to send a reply to Lewis in the Free Market Entrance. Dances with Balrog in Perion asked me to deliver a reply to Lewis in the Free Market Entrance. I delivered Dances with Balrog's letter to Lewis in the Free Market Entrance." }
)

foreach ($spec in $tasks) {
    $titleRows = @(Get-Rows $spec.English)
    if ($titleRows.Count -ne 1) { throw "具体任务标题行数异常：$($spec.English) -> $($titleRows.Count)" }
    $titleRow = $titleRows[0]
    Assert-Equal $titleRow[1] $spec.Chinese "具体任务译文：$($spec.English)"
    Assert-Equal $titleRow[2] "怀旧服-任务#$($spec.Id)" "具体任务分类：$($spec.English)"

    $resolved = [string]$resolveTitle.Invoke($store, @([string]$spec.English))
    Assert-Equal $resolved $spec.Id "唯一标题解析：$($spec.English)"
    $detected = [string]$detectTaskId.Invoke($store, @([string]$spec.English))
    Assert-Equal $detected $spec.Id "具体任务 DetectTaskId：$($spec.English)"
    $nameMatches = @($findTaskNames.Invoke($store, @([string]$spec.English)))
    $matchingIds = @()
    foreach ($match in $nameMatches) {
        $entry = Get-FieldValue $match 'Entry'
        if ([bool](Get-FieldValue $entry 'IsTaskName')) {
            $matchingIds += [string](Get-FieldValue $entry 'TaskId')
        }
    }
    if ($matchingIds -notcontains $spec.Id) {
        throw "具体任务未进入标题索引：$($spec.English) -> $($matchingIds -join ',')"
    }

    $descriptionRows = @(Get-Rows $spec.Description)
    if ($descriptionRows.Count -ne 1) { throw "任务说明行数异常：#$($spec.Id) -> $($descriptionRows.Count)" }
    Assert-Equal $descriptionRows[0][2] "怀旧服-任务说明#$($spec.Id)" "任务说明分类：#$($spec.Id)"
    $descriptionId = [string]$detectTaskId.Invoke($store, @([string]$spec.Description))
    Assert-Equal $descriptionId $spec.Id "任务说明 DetectTaskId：#$($spec.Id)"
}

# The site uses "Welcome to the Free Market!" both as the chain heading and as
# concrete quest #80026. We keep only the concrete title row because bare OCR text
# cannot distinguish two identical strings; the concrete resolver must still win.
$welcomeMatches = @($findMatches.Invoke($store, @([string]'Welcome to the Free Market!')))
$welcomeTaskEntries = @()
foreach ($match in $welcomeMatches) {
    $entry = Get-FieldValue $match 'Entry'
    if ([string](Get-FieldValue $entry 'Category') -eq '怀旧服-任务#80026') {
        $welcomeTaskEntries += ,$entry
    }
}
if ($welcomeTaskEntries.Count -ne 0) {
    throw 'Welcome 具体任务不应被普通匹配索引吞掉；应通过 DetectTaskId/任务标题索引解析'
}
Assert-Equal ([string]$detectTaskId.Invoke($store, @([string]'Welcome to the Free Market!'))) '80026' 'Welcome 同名组标题/具体任务冲突解析'
Assert-Equal ([string]$resolveTitle.Invoke($store, @([string]'Welcome to the Free Market!'))) '80026' 'Welcome 具体任务标题解析'

Write-Output '任务标题专项测试通过：4 个任务组为 display-only/无 taskId，#80026-#80029 标题与说明均可解析，Welcome 同名链标题不影响具体任务 ID'
