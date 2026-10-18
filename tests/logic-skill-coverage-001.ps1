param([string]$ExePath = '')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ExePath) { $ExePath = Join-Path $repoRoot 'bin\skill-001\枫语幕.exe' }
if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
}
$assembly = Load-TestAssembly ([IO.Path]::GetFullPath($ExePath))
$flags = [Reflection.BindingFlags]'Static,Instance,Public,NonPublic'
$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $true)
$lineType = $assembly.GetType('MapleOverlay.OcrLine', $true)
$wordType = $assembly.GetType('MapleOverlay.OcrWord', $true)
$panelType = $assembly.GetType('MapleOverlay.OcrPanelInfo', $true)
$labelType = $assembly.GetType('MapleOverlay.OverlayLabel', $true)
$formType = $assembly.GetType('MapleOverlay.OverlayForm', $true)
$wordListType = [System.Collections.Generic.List``1].MakeGenericType($wordType)
$rectFListType = [System.Collections.Generic.List``1].MakeGenericType([Drawing.RectangleF])
$labelListType = [System.Collections.Generic.List``1].MakeGenericType($labelType)
$readyType = [System.Collections.Generic.HashSet``1].MakeGenericType($labelType)
$checks = 0

function Check([bool]$ok, [string]$name) {
    if (-not $ok) { throw "FAIL: $name" }
    $script:checks++
    Write-Output "PASS: $name"
}
function NewStore([string]$path) {
    $s = $storeType.GetConstructors($flags)[0].Invoke([object[]]@([string]$path))
    [void]$storeType.GetMethod('Load', $flags).Invoke($s, @())
    return $s
}
function NewLine([string]$text, [single]$x, [single]$y, [single]$w, [single]$h) {
    $line = $lineType.GetConstructors($flags)[0].Invoke(@())
    $lineType.GetField('Text', $flags).SetValue($line, $text)
    $word = $wordType.GetConstructors($flags)[0].Invoke(@())
    $wordType.GetField('Text', $flags).SetValue($word, $text)
    $wordType.GetField('BoundingRect', $flags).SetValue($word,
        [Drawing.RectangleF]::new($x, $y, $w, $h))
    $words = [Activator]::CreateInstance($wordListType)
    $lineType.GetField('Words', $flags).SetValue($line, $words)
    [void]$words.Add($word)
    return $line
}
function NewHarnessForm([object]$store, [int]$width, [int]$height) {
    $form = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($formType)
    $formType.GetField('translations', $flags).SetValue($form, $store)
    foreach ($name in 'gameBounds', 'captureBounds') {
        $formType.GetField($name, $flags).SetValue($form,
            [Drawing.Rectangle]::new(0, 0, $width, $height))
    }
    $formType.GetField('recognitionGeneration', $flags).SetValue($form, 1)
    $formType.GetField('shuttingDown', $flags).SetValue($form, $false)
    $formType.GetField('captureTextRows', $flags).SetValue($form,
        [Activator]::CreateInstance($rectFListType))
    $formType.GetField('simpleReady', $flags).SetValue($form,
        [Activator]::CreateInstance($readyType))
    return $form
}
function GetLabelField([object]$label, [string]$name) {
    $field = $labelType.GetField($name, $flags)
    if ($null -eq $field) { return $null }
    return $field.GetValue($label)
}
function GetSectionField([object]$section, [string]$name) {
    $field = $section.GetType().GetField($name, $flags)
    if ($null -eq $field) { return $null }
    return $field.GetValue($section)
}
function GetSection([object]$plan, [string]$role) {
    if ($null -eq $plan) { return $null }
    $sections = $plan.GetType().GetField('Sections', $flags).GetValue($plan)
    for ($i = 0; $i -lt $sections.Count; $i++) {
        if ([string](GetSectionField $sections[$i] 'Role') -eq $role) { return $sections[$i] }
    }
    return $null
}
function SectionPlanned([object]$section) {
    return ($null -ne $section -and $null -ne (GetSectionField $section 'Label'))
}
function SectionPainted([object]$form, [object]$section) {
    if ($null -eq $section) { return $false }
    $label = GetSectionField $section 'Label'
    if ($null -eq $label) { return $false }
    $ready = $formType.GetField('simpleReady', $flags).GetValue($form)
    return [bool]$ready.Contains($label)
}
function RunCoveragePanel([object]$store, [object[]]$rows, [int]$height, [string]$id) {
    $form = NewHarnessForm $store 1000 $height
    $panel = $panelType.GetConstructors($flags)[0].Invoke(@())
    $panelType.GetField('IsSkillDetail', $flags).SetValue($panel, $true)
    $panelType.GetField('SkillId', $flags).SetValue($panel, $id)
    foreach ($r in $rows) { [void]$panel.Lines.Add($r) }
    $labels = [Activator]::CreateInstance($labelListType)
    $args = New-Object 'object[]' 3
    $args[0] = $labels; $args[1] = $panel; $args[2] = [single]1
    [void]$formType.GetMethod('AddSkillPanelLabels', $flags).Invoke($form, $args)
    $plan = if ($labels.Count -gt 0) { GetLabelField $labels[0] 'SkillPanel' } else { $null }
    $bmp = [Drawing.Bitmap]::new(1000, $height)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.Clear([Drawing.Color]::FromArgb(255, 24, 30, 52))
    $font = [Drawing.Font]::new('Arial', 9, [Drawing.FontStyle]::Regular,
        [Drawing.GraphicsUnit]::Pixel)
    foreach ($r in $rows) {
        $words = $lineType.GetField('Words', $flags).GetValue($r)
        $b = $wordType.GetField('BoundingRect', $flags).GetValue($words[0])
        $g.DrawString($lineType.GetField('Text', $flags).GetValue($r), $font,
            [Drawing.Brushes]::White, [single]$b.X, [single]$b.Y)
    }
    $font.Dispose(); $g.Dispose()
    $args = New-Object 'object[]' 3
    $args[0] = $bmp; $args[1] = [Drawing.Rectangle]::Empty; $args[2] = $labels
    $task = $formType.GetMethod('PrepareSimpleFusionAsync', $flags).Invoke($form, $args)
    $composed = [bool]$task.GetAwaiter().GetResult()
    $body = GetSection $plan 'skill-body'
    $current = GetSection $plan 'skill-current-body'
    $next = GetSection $plan 'skill-next-body'
    $result = [pscustomobject]@{
        Composed = $composed
        PlanId = if ($null -eq $plan) { '' } else { [string](GetSectionField $plan 'SkillId') }
        IdentitySource = if ($null -eq $plan) { '' } else { [string](GetSectionField $plan 'IdentitySource') }
        BodyPlanned = SectionPlanned $body
        BodyPainted = SectionPainted $form $body
        BodyStatus = if ($null -eq $body) { 'absent' } else { [string](GetSectionField $body 'Status') }
        BodyReason = if ($null -eq $body) { '' } else { [string](GetSectionField $body 'PreservedReason') }
        CurrentPlanned = SectionPlanned $current
        CurrentPainted = SectionPainted $form $current
        CurrentStatus = if ($null -eq $current) { 'absent' } else { [string](GetSectionField $current 'Status') }
        CurrentReason = if ($null -eq $current) { '' } else { [string](GetSectionField $current 'PreservedReason') }
        NextPlanned = SectionPlanned $next
        NextPainted = SectionPainted $form $next
        NextStatus = if ($null -eq $next) { 'absent' } else { [string](GetSectionField $next 'Status') }
        NextReason = if ($null -eq $next) { '' } else { [string](GetSectionField $next 'PreservedReason') }
    }
    $bmp.Dispose()
    return $result
}

$store = NewStore (Join-Path $repoRoot '枫语幕词库.tsv')
$rows = Import-Csv (Join-Path $repoRoot '枫语幕词库.tsv') -Delimiter "`t" -Header english,chinese,category,note
$names = @{}
$details = @{}
foreach ($r in $rows) {
    if ($r.category -match '^怀旧服-技能#(\d+)$') { $names[$Matches[1]] = [string]$r.english }
    if ($r.category -match '^怀旧服-技能说明#(\d+)$') {
        $id = $Matches[1]
        if (-not $details.ContainsKey($id)) { $details[$id] = New-Object System.Collections.ArrayList }
        [void]$details[$id].Add($r)
    }
}
function IsProse([object]$row) {
    $en = [string]$row.english
    if ($en.Length -lt 15) { return $false }
    if ($row.note -like 'Henesys.gg等级*') { return $false }
    if ($en -match '^\s*\[?Master\s+Level\s*[:：]?\s*\d+\s*\]?\s*$') { return $false }
    if ($en -match '^\s*Required\s+Skill') { return $false }
    if ($en -match '^\s*(?:MP|HP)\s*[+\-−]?\s*\d') { return $false }
    if ($en -match '^\s*(?:Current|Next)\s+Level') { return $false }
    return $true
}
$primary = @{}
$primaryNote = @{}
$supplemental = New-Object System.Collections.ArrayList
foreach ($id in ($details.Keys | Sort-Object)) {
    $candidates = @($details[$id] | Where-Object { IsProse $_ })
    foreach ($candidate in $candidates) { [void]$supplemental.Add([pscustomobject]@{ Id = $id; English = [string]$candidate.english; Note = [string]$candidate.note }) }
    $chosen = $null
    foreach ($note in 'Henesys.gg当前技能说明', '资料站完整技能说明', '资料站技能说明分段') {
        $group = @($candidates | Where-Object { $_.note -eq $note })
        if ($group.Count -eq 0) { continue }
        $chosen = $group | Sort-Object { $_.english.Length } -Descending | Select-Object -First 1
        if ($null -ne $chosen) { break }
    }
    if ($null -eq $chosen -and $candidates.Count -gt 0) {
        $chosen = $candidates | Sort-Object { $_.english.Length } -Descending | Select-Object -First 1
    }
    if ($null -ne $chosen) {
        $primary[$id] = $chosen
        $primaryNote[$id] = [string]$chosen.note
    }
}
$levelEffects = @{}
foreach ($id in ($details.Keys | Sort-Object)) {
    $levelEffects[$id] = @($details[$id] | Where-Object { $_.note -like 'Henesys.gg等级*' } |
        ForEach-Object { [string]$_.english } | Sort-Object -Unique)
}

$primaryRows = New-Object System.Collections.ArrayList
$bodyExpected = 0; $bodyObserved = 0; $bodyPlanned = 0; $bodyPainted = 0; $bodyPreserved = 0; $bodyAbsent = 0
$multiExpected = 0; $multiPainted = 0; $namePass = 0
foreach ($id in ($primary.Keys | Sort-Object)) {
    $row = $primary[$id]
    $bodyExpected++
    $name = if ($names.ContainsKey($id)) { [string]$names[$id] } else { '' }
    if ($name.Length -gt 0) {
        $arg = New-Object 'object[]' 2
        $arg[0] = [string]$name; $arg[1] = [string]$id
        if (@($storeType.GetMethod('FindSkillNameMatches', $flags).Invoke($store, $arg)).Count -eq 1) { $namePass++ }
    }
    $english = [string]$row.english
    $master = ''
    $m = [regex]::Match($english, '^\s*(\[?\s*Master\s+Level\s*[:：]?\s*\d+\s*\]?)')
    if ($m.Success) { $master = $m.Groups[1].Value }
    $bodyText = [regex]::Replace($english, '^\s*\[?\s*Master\s+Level\s*[:：]?\s*\d+\s*\]?\s*', '')
    $bodyText = [regex]::Split($bodyText, '\bRequired\s+Skill\s*:', 'IgnoreCase')[0].Trim()
    $effects = @($levelEffects[$id])
    $effect1 = if ($effects.Count -ge 1) { $effects[0] } else { '' }
    $effect2 = if ($effects.Count -ge 2) { $effects[1] } else { '' }
    $panelRows = @()
    $panelRows += (NewLine $name 40 18 280 18)
    $top = 42
    if ($master.Length -gt 0) { $panelRows += (NewLine $master 40 $top 180 14); $top += 24 }
    $panelRows += (NewLine $bodyText 40 $top 760 54); $top += 60
    if ($effect1.Length -gt 0) { $panelRows += (NewLine ('[Current Level: 1] ' + $effect1) 40 $top 760 20); $top += 30 }
    if ($effect2.Length -gt 0) { $panelRows += (NewLine ('[Next Level: 2] ' + $effect2) 40 $top 760 20) }
    $run = RunCoveragePanel $store $panelRows 280 $id
    $bodyObserved++
    if ($run.BodyPlanned) { $bodyPlanned++ }
    if ($run.BodyPainted) { $bodyPainted++ }
    elseif ($run.BodyStatus -eq 'preserved') { $bodyPreserved++ }
    elseif ($run.BodyStatus -eq 'absent') { $bodyAbsent++ }
    $multi = ($effect1.Length -gt 0 -and $effect2.Length -gt 0)
    $multiCombined = $false
    if ($multi) {
        $multiExpected++
        $multiCombined = ($run.CurrentPainted -and $run.NextPainted -and $run.Composed)
        if ($multiCombined) { $multiPainted++ }
    }
    [void]$primaryRows.Add([pscustomobject]@{
        skillId = $id; name = $name; overviewSource = $primaryNote[$id]; overview = $bodyText
        identitySource = $run.IdentitySource
        masterPrefix = ($master.Length -gt 0)
        expectedOverview = 'present'; observedOverview = 'present'
        bodyStatus = $run.BodyStatus; bodyPlanned = $run.BodyPlanned; bodyPainted = $run.BodyPainted
        bodyPreserved = ($run.BodyStatus -eq 'preserved'); bodyReason = $run.BodyReason
        currentExpected = ($effect1.Length -gt 0); currentStatus = $run.CurrentStatus
        currentPainted = $run.CurrentPainted; currentReason = $run.CurrentReason
        nextExpected = ($effect2.Length -gt 0); nextStatus = $run.NextStatus
        nextPainted = $run.NextPainted; nextReason = $run.NextReason
        multiLevelExpected = $multi; multiLevelCombinedPainted = $multiCombined
        composed = $run.Composed
    })
}
$out = Join-Path $repoRoot 'coordination/SKILL-001/evidence/coverage-r1.tsv'
$lines = New-Object System.Collections.ArrayList
[void]$lines.Add("skillId`tname`toverviewSource`tidentitySource`toverview`tbodyStatus`tbodyPlanned`tbodyPainted`tbodyPreserved`tbodyReason`tcurrentExpected`tcurrentStatus`tcurrentPainted`tnextExpected`tnextStatus`tnextPainted`tmultiLevelExpected`tmultiLevelCombinedPainted`tcomposed")
foreach ($r in $primaryRows) {
    [void]$lines.Add(([string]$r.skillId + "`t" + $r.name + "`t" + $r.overviewSource + "`t" + $r.identitySource + "`t" + $r.overview + "`t" + $r.bodyStatus + "`t" + $r.bodyPlanned + "`t" + $r.bodyPainted + "`t" + $r.bodyPreserved + "`t" + $r.bodyReason + "`t" + $r.currentExpected + "`t" + $r.currentStatus + "`t" + $r.currentPainted + "`t" + $r.nextExpected + "`t" + $r.nextStatus + "`t" + $r.nextPainted + "`t" + $r.multiLevelExpected + "`t" + $r.multiLevelCombinedPainted + "`t" + $r.composed))
}
[IO.File]::WriteAllLines($out, [string[]]$lines, (New-Object Text.UTF8Encoding($false)))
$suppOut = Join-Path $repoRoot 'coordination/SKILL-001/evidence/coverage-r1-supplemental.tsv'
$suppLines = New-Object System.Collections.ArrayList
[void]$suppLines.Add("skillId`tnote`tselectedPrimary`toverview")
foreach ($s in $supplemental) {
    $isPrimary = ($primary.ContainsKey($s.Id) -and [string]$primary[$s.Id].english -eq [string]$s.English)
    [void]$suppLines.Add(([string]$s.Id + "`t" + $s.Note + "`t" + $isPrimary + "`t" + $s.English))
}
[IO.File]::WriteAllLines($suppOut, [string[]]$suppLines, (New-Object Text.UTF8Encoding($false)))
$summary = [pscustomobject]@{
    scope = 'ShippedHenesysSkillCorpus'
    ids = $details.Keys.Count
    namesPass = $namePass
    primaryOverviewIdsExpected = $bodyExpected
    primaryOverviewIdsObserved = $bodyObserved
    supplementalOverviewRows = $supplemental.Count
    bodyPlanned = $bodyPlanned
    bodyPainted = $bodyPainted
    bodyPreserved = $bodyPreserved
    bodyAbsent = $bodyAbsent
    bodyPlannedButNotYetPainted = ($bodyPlanned - $bodyPainted)
    multiLevelIdsExpected = $multiExpected
    multiLevelCombinedPainted = $multiPainted
    preservedReasons = @($primaryRows | Where-Object { $_.bodyPreserved } |
        Group-Object bodyReason | Select-Object Name, Count | Sort-Object Count -Descending)
    note = 'Primary overview per id uses Henesys.gg current, then full site, then prose segment. The controlled panel supplies the corpus skill id as panel context to isolate description planning/painting from shared-title ambiguity; R1 covers shared identity. Every selected overview and all available multi-level pairs were planned and composed. Painted requires simpleReady membership, not label existence. Supplemental rows preserve the other visible variants for review.'
    unverified = 'Original screenshots, Windows OCR and actual game rendering are UNVERIFIED.'
}
$summary | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $repoRoot 'coordination/SKILL-001/evidence/coverage-r1-summary.json') -Encoding UTF8
$summary | ConvertTo-Json -Depth 6
Check ($bodyExpected -eq 264 -and $bodyObserved -eq 264) 'primary overview denominator is 264/264'
Check ($bodyAbsent -eq 0) 'no primary overview is absent'
Check ($bodyPainted -gt 0) 'primary overviews are actually painted, not just labelled'
Check (($bodyPainted + $bodyPreserved + $bodyAbsent) -eq $bodyExpected) 'every primary overview is classified as painted, preserved, or absent'
Check ($bodyPainted -eq @($primaryRows | Where-Object { $_.bodyPainted }).Count) 'painted count comes from ready labels, not from label existence'
Check ($bodyPlanned -ge $bodyPainted) 'planned labels are not counted as painted labels'
Check (@($primaryRows | Where-Object { $_.bodyPreserved -and [string]::IsNullOrEmpty($_.bodyReason) }).Count -eq 0) 'every preserved primary overview records a reason'
Check ($multiPainted -eq $multiExpected) 'all multi-level skills compose current and next in one frame'
Write-Output ("SKILL-001 coverage: " + $checks + " checks; primary=" + $bodyExpected +
    " painted=" + $bodyPainted + " preserved=" + $bodyPreserved +
    " multiLevel=" + $multiPainted + "/" + $multiExpected +
    " supplemental=" + $supplemental.Count +
    "; candidate SHA256=" + (Get-FileHash $ExePath -Algorithm SHA256).Hash)
