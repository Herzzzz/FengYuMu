param([string]$ExePath = '')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ExePath) { $ExePath = Join-Path $repoRoot 'bin\skill-001\枫语幕.exe' }
if (-not (Test-Path -LiteralPath $ExePath)) { throw "找不到候选 EXE：$ExePath" }
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
$lineListType = [System.Collections.Generic.List``1].MakeGenericType($lineType)
$labelListType = [System.Collections.Generic.List``1].MakeGenericType($labelType)
$rectFListType = [System.Collections.Generic.List``1].MakeGenericType([Drawing.RectangleF])
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
function NewLineWords([string]$text, [single]$x, [single]$y, [single]$w, [single]$h) {
    $line = $lineType.GetConstructors($flags)[0].Invoke(@())
    $lineType.GetField('Text', $flags).SetValue($line, $text)
    $words = [Activator]::CreateInstance($wordListType)
    $lineType.GetField('Words', $flags).SetValue($line, $words)
    $parts = $text.Split([char[]]@(' '), [StringSplitOptions]::RemoveEmptyEntries)
    $cursor = 0
    foreach ($part in $parts) {
        $at = $text.IndexOf($part, $cursor, [StringComparison]::Ordinal)
        if ($at -lt 0) { continue }
        $word = $wordType.GetConstructors($flags)[0].Invoke(@())
        $wordType.GetField('Text', $flags).SetValue($word, $part)
        $wordType.GetField('BoundingRect', $flags).SetValue($word,
            [Drawing.RectangleF]::new($x + $w * $at / [Math]::Max(1, $text.Length),
                $y, [single]($w * $part.Length / [Math]::Max(1, $text.Length)), $h))
        [void]$words.Add($word)
        $cursor = $at + $part.Length
    }
    return $line
}
function NewLineList([object[]]$rows) {
    $list = [Activator]::CreateInstance($lineListType)
    foreach ($row in $rows) { [void]$list.Add($row) }
    Write-Output -NoEnumerate $list
}
function NewPanel([object[]]$rows, [string]$id = '') {
    $panel = $panelType.GetConstructors($flags)[0].Invoke(@())
    $panelType.GetField('SkillId', $flags).SetValue($panel, $id)
    $panelType.GetField('IsSkillDetail', $flags).SetValue($panel, $true)
    foreach ($row in $rows) { [void]$panel.Lines.Add($row) }
    return $panel
}
function NewLabelList() { Write-Output -NoEnumerate ([Activator]::CreateInstance($labelListType)) }
function NewHarnessForm([object]$store, [int]$width = 1000, [int]$height = 700) {
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
function AddPanelLabels([object]$form, [object]$panel, [object]$labels) {
    $args = New-Object 'object[]' 3
    $args[0] = $labels; $args[1] = $panel; $args[2] = [single]1
    try { [void]$formType.GetMethod('AddSkillPanelLabels', $flags).Invoke($form, $args) }
    catch {
        $inner = $_.Exception.InnerException
        if ($null -ne $inner) { throw ("AddSkillPanelLabels failed: " + $inner.ToString()) }
        throw
    }
}
function GetLabelField([object]$label, [string]$name) {
    $field = $labelType.GetField($name, $flags)
    if ($null -eq $field) { return $null }
    return $field.GetValue($label)
}
function GetSectionField([object]$section, [string]$name) {
    return $section.GetType().GetField($name, $flags).GetValue($section)
}
function GetPlan([object]$labels) {
    if ($labels.Count -eq 0) { return $null }
    return GetLabelField $labels[0] 'SkillPanel'
}
function GetSections([object]$plan) {
    if ($null -eq $plan) { return @() }
    return $plan.GetType().GetField('Sections', $flags).GetValue($plan)
}
function Roles([object]$labels) {
    $result = @()
    for ($i = 0; $i -lt $labels.Count; $i++) {
        $result += [string](GetLabelField $labels[$i] 'SemanticRole')
    }
    return $result
}
function HasRole([string[]]$roles, [string]$role) { return $roles -contains $role }
function NewFrame([int]$width, [int]$height) {
    $width = [int]$width; $height = [int]$height
    if ($width -le 0 -or $height -le 0) { throw "invalid frame size ${width}x${height}" }
    $bmp = [Drawing.Bitmap]::new($width, $height)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.Clear([Drawing.Color]::FromArgb(255, 24, 30, 52))
    $g.Dispose()
    return $bmp
}
function DrawSource([Drawing.Bitmap]$bmp, [object[]]$rows) {
    $g = [Drawing.Graphics]::FromImage($bmp)
    $font = [Drawing.Font]::new('Arial', 10, [Drawing.FontStyle]::Regular,
        [Drawing.GraphicsUnit]::Pixel)
    foreach ($row in $rows) {
        $sourceText = [string]$lineType.GetField('Text', $flags).GetValue($row)
        $sourceWords = $lineType.GetField('Words', $flags).GetValue($row)
        if ($null -eq $sourceWords -or $sourceWords.Count -eq 0) { continue }
        $bounds = $wordType.GetField('BoundingRect', $flags).GetValue($sourceWords[0])
        $g.DrawString($sourceText, $font, [Drawing.Brushes]::White,
            [single]$bounds.X, [single]$bounds.Y)
    }
    $font.Dispose(); $g.Dispose()
}
function Compose([object]$form, [object]$labels, [Drawing.Bitmap]$bmp) {
    $args = New-Object 'object[]' 3
    $args[0] = $bmp; $args[1] = [Drawing.Rectangle]::Empty; $args[2] = $labels
    $task = $formType.GetMethod('PrepareSimpleFusionAsync', $flags).Invoke($form, $args)
    return [bool]$task.GetAwaiter().GetResult()
}
function ReadyCount([object]$form) {
    return [int]$formType.GetField('simpleReady', $flags).GetValue($form).Count
}
function AssertNoRowOverlap([object]$labels, [string]$name) {
    for ($i = 0; $i -lt $labels.Count; $i++) {
        $aRows = $labels[$i].SourceLineBounds
        if ($null -eq $aRows) { continue }
        for ($j = $i + 1; $j -lt $labels.Count; $j++) {
            $bRows = $labels[$j].SourceLineBounds
            if ($null -eq $bRows) { continue }
            foreach ($a in $aRows) { foreach ($b in $bRows) {
                $intersection = [Drawing.RectangleF]::Intersect($a, $b)
                if ($intersection.Width -gt 1.0 -and $intersection.Height -gt 1.0) {
                    throw "FAIL: $name 分区源行重叠 role=$($labels[$i].SemanticRole)/$($labels[$j].SemanticRole)"
                }
            } }
        }
    }
}
function RunPlan([object]$store, [object[]]$rows, [int]$width = 1000, [int]$height = 700,
    [switch]$NoCompose) {
    $form = NewHarnessForm $store $width $height
    $panel = NewPanel $rows
    $labels = [Activator]::CreateInstance($labelListType)
    AddPanelLabels $form $panel $labels
    $result = [pscustomobject]@{ Form = $form; Panel = $panel; Labels = $labels; Plan = (GetPlan $labels); Compose = $false; Ready = -1 }
    if (-not $NoCompose) {
        $bmp = NewFrame $width $height
        DrawSource $bmp $rows
        $result.Compose = Compose $form $labels $bmp
        $result.Ready = ReadyCount $form
        $bmp.Dispose()
    }
    return $result
}

$store = NewStore (Join-Path $repoRoot '枫语幕词库.tsv')
# Identity probes isolate the failure that previously selected 1200000 for every
# level-20 tooltip and borrowed Sword Mastery's description.
Check ((NewStore (Join-Path $repoRoot '枫语幕词库.tsv')).GetType() -ne $null) 'dictionary loads'
Check ([string]$storeType.GetMethod('DetectSkillId', $flags).Invoke($store, @('Critical Recovery')) -eq '4100002') 'Critical Recovery keeps its unique id despite containing Recovery'
Check ([string]$storeType.GetMethod('DetectSkillId', $flags).Invoke($store, @('Rush')) -eq '') 'shared Rush name does not choose one job id'
Check ([string]$storeType.GetMethod('DetectSkillId', $flags).Invoke($store, @('Sword Mastery')) -eq '') 'shared Sword Mastery name does not choose one job id'
Check ([string]$storeType.GetMethod('DetectSkillId', $flags).Invoke($store, @('Magic Guard Recovery')) -eq '') 'two independent visible titles are ambiguous, not the longer one'
Check ([string]$storeType.GetMethod('DetectSkillId', $flags).Invoke($store, @('Critical Recovery Recovery')) -eq '') 'an independent second Recovery still makes the line ambiguous'
Check ([string]$storeType.GetMethod('DetectSkillContentId', $flags).Invoke($store, @('[Master Level: 20]')) -eq '') 'generic master header does not resolve a skill id'
Check ([string]$storeType.GetMethod('DetectSkillContentId', $flags).Invoke($store, @('Uses HP and MP to temporarily increase the Attack Speed of the equipped claw. It only applies when the character is equipped with a claw and throwing stars.')) -eq '4101000') 'Claw Booster body resolves its own id'
Check ([string]$storeType.GetMethod('DetectSkillContentId', $flags).Invoke($store, @('Critical hits sever the enemy''s tendons, slowing them down.')) -eq '') 'ambiguous shared body does not choose one id'
$arrow = @($storeType.GetMethod('FindSkillNameMatches', $flags).Invoke($store, [object[]]@('Arrow Bomb', '3101004')))
Check ($arrow.Count -eq 1 -and [string]$arrow[0].Entry.Chinese -eq '爆炸箭') 'resolved qualified name exposes only the visible prefix'
Check (@($storeType.GetMethod('FindSkillNameMatches', $flags).Invoke($store, [object[]]@('Arrow Bomb', ''))).Count -eq 0) 'unresolved shortened name is not guessed'
Check (@($storeType.GetMethod('FindSkillNameMatches', $flags).Invoke($store, [object[]]@('Fire', '3101004'))).Count -eq 0) 'bare prefix of another skill is rejected'
Check (@($storeType.GetMethod('FindSkillNameMatches', $flags).Invoke($store, [object[]]@('Arrow Bom...', ''))).Count -eq 0) 'truncated name is rejected'
$overviewMethod = $storeType.GetMethod('TryTranslateSkillOverview', $flags)
function TryOverview([string]$text, [string]$id) {
    $args = New-Object 'object[]' 3
    $args[0] = $text; $args[1] = $id; $args[2] = ''
    $ok = [bool]$overviewMethod.Invoke($store, $args)
    return [pscustomobject]@{ Ok = $ok; Text = [string]$args[2] }
}
$never = TryOverview 'Never recover HP constantly for 30 sec.' '1001'
Check (-not $never.Ok -and $never.Text -eq '') 'negated visible sentence is not replaced by the known Recovery overview'
$unrelated = TryOverview 'An unrelated story lasts for 30 sec.' '1001'
Check (-not $unrelated.Ok -and $unrelated.Text -eq '') 'unrelated visible sentence is not replaced by the known Recovery overview'
$exactOverview = TryOverview 'Enables the user to recover HP constantly for 30 sec.' '1001'
Check ($exactOverview.Ok -and $exactOverview.Text.Contains('30秒') -and $exactOverview.Text.Contains('恢复HP')) 'exact visible overview still translates with its 30-second association'
$r2Rows = [object[]]@(
    (NewLine 'Recovery' 40 30 200 18),
    (NewLine '[Master Level: 3]' 40 60 160 14),
    (NewLine 'Never recover HP constantly for 30 sec.' 40 82 500 24)
)
$r2 = RunPlan -store $store -rows $r2Rows -width 900 -height 260
$r2Body = $r2.Labels | Where-Object { (GetLabelField $_ 'SemanticRole') -eq 'skill-body' } | Select-Object -First 1
Check ($null -eq $r2Body) 'negated body is preserved in the panel plan, not replaced by its known id'
$r2Plan = $r2.Plan
$r2Preserved = $false
foreach ($section in (GetSections $r2Plan)) {
    if ((GetSectionField $section 'Role') -eq 'skill-body' -and
        (GetSectionField $section 'Status') -eq 'preserved') { $r2Preserved = $true }
}
Check ($r2Preserved) 'the plan distinguishes an unsafe overview from an absent one'
$r2Unrelated = RunPlan -store $store -rows ([object[]]@(
    (NewLine 'Recovery' 40 30 200 18),
    (NewLine '[Master Level: 3]' 40 60 160 14),
    (NewLine 'An unrelated story lasts for 30 sec.' 40 82 500 24)
)) -width 900 -height 260
$r2UnrelatedBody = $r2Unrelated.Labels | Where-Object { (GetLabelField $_ 'SemanticRole') -eq 'skill-body' } | Select-Object -First 1
Check ($null -eq $r2UnrelatedBody) 'unrelated body is not replaced by a 30-second known overview'

# Full skill panel: name, max level, complete body, prerequisite, current and next.
$healRows = [object[]]@(
    (NewLine 'Heal' 40 30 200 18),
    (NewLine '[Master Level: 30]' 40 60 160 14),
    (NewLine 'Recovers the HP of all party members around the area. The amount of healing depends on the number of people being healed, and undead monsters around the area will receive damage.' 40 82 640 60),
    (NewLine 'Required Skill: Atleast Level 5 Recovery' 40 150 360 14),
    (NewLine '[Current Level 1] MP -12; Recovery rate 40%; Range 100%' 40 180 640 20),
    (NewLine '[Next Level 2] MP -12; Recovery rate 42%; Range 100%' 40 215 640 20)
)
$full = RunPlan -store $store -rows $healRows -width 1000 -height 500
$fullRoles = Roles $full.Labels
foreach ($role in 'skill-name', 'skill-master-level', 'skill-body', 'skill-required', 'skill-current-body', 'skill-next-body') {
    Check (HasRole $fullRoles $role) "full panel plans $role"
}
$bodyLabel = $full.Labels | Where-Object { (GetLabelField $_ 'SemanticRole') -eq 'skill-body' } | Select-Object -First 1
Check ($null -ne $bodyLabel -and (GetLabelField $bodyLabel 'Text').Contains('不死')) 'complete description includes visible undead effect'
Check ($null -ne $bodyLabel -and (GetLabelField $bodyLabel 'Text').Contains('恢复')) 'complete description includes healing effect'
$requiredLabel = $full.Labels | Where-Object { (GetLabelField $_ 'SemanticRole') -eq 'skill-required' } | Select-Object -First 1
Check ($null -ne $requiredLabel -and (GetLabelField $requiredLabel 'Text').Contains('需要技能') -and (GetLabelField $requiredLabel 'Text').Contains('5级')) 'prerequisite keeps skill name, relation and level'
$currentLabel = $full.Labels | Where-Object { (GetLabelField $_ 'SemanticRole') -eq 'skill-current-body' } | Select-Object -First 1
$nextLabel = $full.Labels | Where-Object { (GetLabelField $_ 'SemanticRole') -eq 'skill-next-body' } | Select-Object -First 1
Check ($null -ne $currentLabel -and (GetLabelField $currentLabel 'Text').Contains('当前等级：1') -and (GetLabelField $currentLabel 'Text').Contains('40%') -and (GetLabelField $currentLabel 'Text').Contains('100%')) 'current level binds its own level, recovery and range values'
Check ($null -ne $nextLabel -and (GetLabelField $nextLabel 'Text').Contains('下一级：2') -and (GetLabelField $nextLabel 'Text').Contains('42%') -and (GetLabelField $nextLabel 'Text').Contains('100%')) 'next level binds its own level and recovery value'
Check ((GetLabelField $currentLabel 'Text') -ne (GetLabelField $nextLabel 'Text')) 'current and next sections are not exchanged'
Check ($full.Compose -and $full.Ready -eq $full.Labels.Count) 'all six sections reach the final composition together'
foreach ($label in $full.Labels) {
    Check ((GetLabelField $label 'LayoutFontSize') -ge 11.0) 'every skill section uses a readable pixel font'
    Check ([string](GetLabelField $label 'LayoutFontFamily') -eq 'SimSun') 'every skill section shares one font family'
}
Check ((GetLabelField ($full.Labels | Where-Object { (GetLabelField $_ 'SemanticRole') -eq 'skill-name' } | Select-Object -First 1) 'LayoutFontSize') -ge (GetLabelField $bodyLabel 'LayoutFontSize')) 'title remains a fixed larger hierarchy'
AssertNoRowOverlap $full.Labels 'full panel'
$plan = $full.Plan
Check ($null -ne $plan -and (GetSections $plan).Count -gt 0) 'one panel plan owns its sections'
$painted = 0
foreach ($section in (GetSections $plan)) {
    if ([string](GetSectionField $section 'Status') -eq 'painted') { $painted++ }
}
Check ($painted -eq $full.Labels.Count) 'plan records painted state for every section'

# Matrix: only next, description+next, max level, missing title, combined OCR row.
$rowsOnlyNext = [object[]]@(
    (NewLine 'Heal' 40 30 200 18),
    (NewLine '[Next Level 2] MP -12; Recovery rate 42%; Range 100%' 40 80 640 20)
)
$onlyNext = RunPlan -store $store -rows $rowsOnlyNext -width 900 -height 300
Check ((HasRole (Roles $onlyNext.Labels) 'skill-next-body') -and -not (HasRole (Roles $onlyNext.Labels) 'skill-current-body') -and -not (HasRole (Roles $onlyNext.Labels) 'skill-master-level')) 'only-next panel processes only next'
Check ($onlyNext.Compose -and $onlyNext.Ready -eq $onlyNext.Labels.Count) 'only-next panel composes'
$rowsDescNext = [object[]]@(
    (NewLine 'Heal' 40 30 200 18),
    (NewLine '[Master Level: 30]' 40 60 160 14),
    (NewLine 'Recovers the HP of all party members around the area.' 40 82 520 24),
    (NewLine '[Next Level 2] MP -12; Recovery rate 42%; Range 100%' 40 120 640 20)
)
$descNext = RunPlan -store $store -rows $rowsDescNext -width 900 -height 320
Check ((HasRole (Roles $descNext.Labels) 'skill-body') -and (HasRole (Roles $descNext.Labels) 'skill-next-body') -and -not (HasRole (Roles $descNext.Labels) 'skill-current-body')) 'description plus only next has no current section'
Check ($descNext.Compose -and $descNext.Ready -eq $descNext.Labels.Count) 'description plus only next composes'
$rowsMaxed = [object[]]@(
    (NewLine 'Heal' 40 30 200 18),
    (NewLine '[Master Level: 30]' 40 60 160 14),
    (NewLine 'Recovers the HP of all party members around the area.' 40 82 520 24),
    (NewLine '[Current Level 30] MP -12; Recovery rate 60%; Range 100%' 40 120 640 20)
)
$maxed = RunPlan -store $store -rows $rowsMaxed -width 900 -height 320
Check (-not (HasRole (Roles $maxed.Labels) 'skill-next-body') -and (HasRole (Roles $maxed.Labels) 'skill-current-body')) 'max-level panel does not invent a next level'
$rowsNoTitle = [object[]]@(
    (NewLine '[Master Level: 30]' 40 30 160 14),
    (NewLine 'Recovers the HP of all party members around the area.' 40 52 520 24),
    (NewLine '[Current Level 1] MP -12; Recovery rate 40%; Range 100%' 40 90 640 20)
)
$noTitle = RunPlan -store $store -rows $rowsNoTitle -width 900 -height 300
Check (-not (HasRole (Roles $noTitle.Labels) 'skill-name') -and (HasRole (Roles $noTitle.Labels) 'skill-body') -and (HasRole (Roles $noTitle.Labels) 'skill-current-body')) 'missing title preserves other visible sections'
$rowsCombinedSingle = [object[]]@(
    (NewLine 'Heal [Master Level: 30] Recovers the HP of all party members around the area.' 40 30 700 24),
    (NewLine '[Current Level 1] MP -12; Recovery rate 40%; Range 100%' 40 80 640 20)
)
$combinedSingle = RunPlan -store $store -rows $rowsCombinedSingle -width 1000 -height 260
$singleRoles = Roles $combinedSingle.Labels
Check (-not (HasRole $singleRoles 'skill-name') -and -not (HasRole $singleRoles 'skill-master-level') -and -not (HasRole $singleRoles 'skill-body')) 'one word box containing title/master/body is not split at estimated boundaries'
$singlePlan = $combinedSingle.Plan
$estimatedSections = 0
foreach ($section in (GetSections $singlePlan)) {
    if ((GetSectionField $section 'Measured') -eq $false) { $estimatedSections++ }
}
Check ($estimatedSections -ge 1) 'the plan records the combined-word body as estimated'
Check ($combinedSingle.Compose -and $combinedSingle.Ready -eq $combinedSingle.Labels.Count) 'estimated combined-word sections are preserved while safe current composes'

$rowsCombinedWords = [object[]]@(
    (NewLineWords 'Heal [Master Level: 30] Recovers the HP of all party members around the area.' 40 30 700 24),
    (NewLineWords '[Current Level 1] MP -12; Recovery rate 40%; Range 100%' 40 80 640 20)
)
$combinedWords = RunPlan -store $store -rows $rowsCombinedWords -width 1000 -height 260
Check ((HasRole (Roles $combinedWords.Labels) 'skill-name') -and (HasRole (Roles $combinedWords.Labels) 'skill-master-level') -and (HasRole (Roles $combinedWords.Labels) 'skill-body') -and (HasRole (Roles $combinedWords.Labels) 'skill-current-body')) 'title, master and body on one OCR row split at real word boundaries'
AssertNoRowOverlap $combinedWords.Labels 'combined word rows'
Check ($combinedWords.Compose -and $combinedWords.Ready -eq $combinedWords.Labels.Count) 'combined word-row sections compose all planned labels'

# R3: a failed effect keeps its original pixels; a header success is recorded
# separately and never erases the effect.
$r3Rows = [object[]]@(
    (NewLine 'Heal' 40 30 180 18),
    (NewLine '[Master Level: 30]' 40 60 160 14),
    (NewLineWords '[Current Level: 1] UnrecognizedEffect 987654321' 40 100 380 18)
)
$r3 = RunPlan -store $store -rows $r3Rows -width 1000 -height 400
$r3Header = $r3.Labels | Where-Object { (GetLabelField $_ 'SemanticRole') -eq 'skill-current-body' } | Select-Object -First 1
Check ($null -ne $r3Header -and (GetLabelField $r3Header 'Text') -eq '当前等级：1') 'an untranslated effect leaves only the header text in the header label'
$r3EffectText = '[Current Level: 1] UnrecognizedEffect 987654321'
$r3EffectStart = 40.0 + 380.0 * $r3EffectText.IndexOf('UnrecognizedEffect') / $r3EffectText.Length
Check ($null -ne $r3Header -and (GetLabelField $r3Header 'SourceBounds').Right -le $r3EffectStart) 'the header-only source stops before the unrecognized effect'
$r3Plan = $r3.Plan
$r3EffectPreserved = $false
foreach ($section in (GetSections $r3Plan)) {
    if ((GetSectionField $section 'Role') -eq 'skill-current-body' -and
        (GetSectionField $section 'EffectStatus') -eq 'preserved') { $r3EffectPreserved = $true }
}
Check ($r3EffectPreserved) 'plan records header=planned and effect=preserved separately'
$r3Form = NewHarnessForm $store 1000 400
$r3Panel = NewPanel $r3Rows
$r3Labels = [Activator]::CreateInstance($labelListType)
AddPanelLabels $r3Form $r3Panel $r3Labels
$r3Frame = NewFrame 1000 400
DrawSource $r3Frame $r3Rows
Check ((Compose $r3Form $r3Labels $r3Frame) -and (ReadyCount $r3Form) -eq $r3Labels.Count) 'header-only R3 panel composes'
$r3Layer = $formType.GetField('simpleLayer', $flags).GetValue($r3Form)
Check ($null -ne $r3Layer -and $r3Layer.GetPixel(350, 105).A -eq 0) 'unrecognized effect pixels remain original after composition'
$r3Frame.Dispose()

# R6: prerequisite relation is preserved, not forced to "at least".
$r6AtMost = RunPlan -store $store -rows ([object[]]@(
    (NewLine 'Heal' 40 30 180 18),
    (NewLine '[Master Level: 30]' 40 60 160 14),
    (NewLine 'Required Skill: At most Level 5 Heal' 40 90 420 18)
)) -width 900 -height 240
$r6Label = $r6AtMost.Labels | Where-Object { (GetLabelField $_ 'SemanticRole') -eq 'skill-required' } | Select-Object -First 1
Check ($null -ne $r6Label -and (GetLabelField $r6Label 'Text').Contains('至多5级') -and -not (GetLabelField $r6Label 'Text').Contains('至少')) 'at-most prerequisite keeps its relation'
$r6AtLeast = RunPlan -store $store -rows ([object[]]@(
    (NewLine 'Heal' 40 30 180 18),
    (NewLine '[Master Level: 30]' 40 60 160 14),
    (NewLine 'Required Skill: At least Level 5 Heal' 40 90 420 18)
)) -width 900 -height 240
$r6Label = $r6AtLeast.Labels | Where-Object { (GetLabelField $_ 'SemanticRole') -eq 'skill-required' } | Select-Object -First 1
Check ($null -ne $r6Label -and (GetLabelField $r6Label 'Text').Contains('至少5级')) 'at-least prerequisite still says at least'
$r6Unsupported = RunPlan -store $store -rows ([object[]]@(
    (NewLine 'Heal' 40 30 180 18),
    (NewLine '[Master Level: 30]' 40 60 160 14),
    (NewLine 'Required Skill: Exactly Level 5 Heal' 40 90 420 18)
)) -width 900 -height 240
$r6UnsupportedLabel = $r6Unsupported.Labels | Where-Object { (GetLabelField $_ 'SemanticRole') -eq 'skill-required' } | Select-Object -First 1
Check ($null -eq $r6UnsupportedLabel) 'unsupported prerequisite relation is preserved, not translated as at least'

# Ambiguous shared name: name may use the shared Chinese, body must not borrow a variant.
$rushRows = [object[]]@(
    (NewLine 'Rush' 40 30 180 18),
    (NewLine '[Master Level: 20]' 40 60 160 14),
    (NewLine 'Rushes forward a certain distance in the direction you are facing. If there are monsters within the rush range, you can deal damage to up to 6 of them and knock them back a certain distance.' 40 82 620 50),
    (NewLine '[Current Level 1] MP -35; Rush, with 130 in distance; Damage 60%' 40 145 620 20)
)
$rush = RunPlan -store $store -rows $rushRows -width 1000 -height 320
$rushRoles = Roles $rush.Labels
Check (HasRole $rushRoles 'skill-name') 'shared Rush keeps a shared visible name'
$rushBody = $rush.Labels | Where-Object { (GetLabelField $_ 'SemanticRole') -eq 'skill-body' } | Select-Object -First 1
Check ($null -eq $rushBody) 'ambiguous Rush body is preserved rather than borrowing another variant'
Check ($rush.Compose -and $rush.Ready -eq $rush.Labels.Count) 'a preserved ambiguous body does not block other sections'
$rushPlan = $rush.Plan
$preserved = $false
foreach ($section in (GetSections $rushPlan)) {
    if ((GetSectionField $section 'Role') -eq 'skill-body' -and
        (GetSectionField $section 'Status') -eq 'preserved') { $preserved = $true }
}
Check ($preserved) 'the plan records the ambiguous body as preserved, separate from absent'

# New name, new numbers and generic effects outside the shipped corpus.
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('skill-001-' + [guid]::NewGuid().ToString('N') + '.tsv')
try {
    $rows = @(
        "Starweaver`t织星者`t怀旧服-职业#99000",
        "Cobalt Pulse`t钴蓝脉冲`t怀旧服-技能#9900001",
        "[Master Level: 30] Sends a focused wave through nearby targets.`t[最高等级：30] 向附近目标发射集中的冲击波。`t怀旧服-技能说明#9900001",
        "MP -19; Basic Attack 217, Mastery level 7`tMP -19；基础攻击217，熟练度等级7`t怀旧服-技能说明#9900001",
        "MP -23; Basic Attack 239, Mastery level 8`tMP -23；基础攻击239，熟练度等级8`t怀旧服-技能说明#9900001"
    )
    [IO.File]::WriteAllLines($fixture, [string[]]$rows, (New-Object Text.UTF8Encoding($false)))
    $synthetic = NewStore $fixture
    $rowsSynthetic = [object[]]@(
        (NewLine 'Cobalt Pulse' 40 30 240 18),
        (NewLine '[Master Level: 30]' 40 60 160 14),
        (NewLine 'Sends a focused wave through nearby targets.' 40 82 620 24),
        (NewLine '[Current Level 7] MP -19; Basic Attack 217, Mastery level 7' 40 115 620 20),
        (NewLine '[Next Level 8] MP -23; Basic Attack 239, Mastery level 8' 40 145 620 20)
    )
    $syntheticPanel = RunPlan -store $synthetic -rows $rowsSynthetic -width 1000 -height 300
    $syntheticRoles = Roles $syntheticPanel.Labels
    Check (HasRole $syntheticRoles 'skill-name') 'new skill name is translated from the same name index'
    Check (HasRole $syntheticRoles 'skill-body') 'new skill body uses the same overview path'
    $syntheticCurrent = $syntheticPanel.Labels | Where-Object { (GetLabelField $_ 'SemanticRole') -eq 'skill-current-body' } | Select-Object -First 1
    $syntheticNext = $syntheticPanel.Labels | Where-Object { (GetLabelField $_ 'SemanticRole') -eq 'skill-next-body' } | Select-Object -First 1
    Check ((GetLabelField $syntheticCurrent 'Text').Contains('217') -and (GetLabelField $syntheticCurrent 'Text').Contains('7') -and -not (GetLabelField $syntheticCurrent 'Text').Contains('239')) 'new current numbers stay bound to the current level'
    Check ((GetLabelField $syntheticNext 'Text').Contains('239') -and (GetLabelField $syntheticNext 'Text').Contains('8') -and -not (GetLabelField $syntheticNext 'Text').Contains('217')) 'new next numbers stay bound to the next level'
    Check ($syntheticPanel.Compose -and $syntheticPanel.Ready -eq $syntheticPanel.Labels.Count) 'out-of-corpus skill composes through generic effect vocabulary'
}
finally { if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Force } }

# List names: explicit context only, shared names allowed, level digits protected.
$listForm = NewHarnessForm $store 400 200
function NewListContext([string[]]$texts) {
    $lines = [Activator]::CreateInstance($lineListType)
    for ($i = 0; $i -lt $texts.Count; $i++) {
        [void]$lines.Add((NewLine $texts[$i] 40 (40 + $i * 24) 240 18))
    }
    Write-Output -NoEnumerate ([pscustomobject]@{ Lines = $lines })
}
function TryList([object]$lines, [int]$target) {
    $typed = [Activator]::CreateInstance($lineListType)
    foreach ($line in $lines) { [void]$typed.Add($line) }
    $labels = [Activator]::CreateInstance($labelListType)
    $args = New-Object 'object[]' 4
    $args[0] = $labels; $args[1] = $typed[$target]; $args[2] = $typed; $args[3] = [single]1
    $ok = [bool]$formType.GetMethod('TryAddSkillListNameLabel', $flags).Invoke($listForm, $args)
    return [pscustomobject]@{ Ok = $ok; Labels = $labels }
}
$headerHeal = NewListContext @('SKILL INVENTORY', 'Heal')
$result = TryList $headerHeal.Lines 1
Check ($result.Ok -and $result.Labels.Count -eq 1 -and (GetLabelField $result.Labels[0] 'Text') -eq '群体治愈') 'header plus exact skill row translates the list name'
$headerLevel = NewListContext @('SKILL INVENTORY', 'Magic Guard 3')
$result = TryList $headerLevel.Lines 1
Check ($result.Ok -and $result.Labels.Count -eq 1 -and (GetLabelField $result.Labels[0] 'Text') -eq '魔法盾') 'list name with a trailing level translates only the name'
Check ((GetLabelField $result.Labels[0] 'KeepClearBounds').Width -gt 0 -and
    (GetLabelField $result.Labels[0] 'KeepClearBounds').Left -ge (GetLabelField $result.Labels[0] 'SourceBounds').Right) 'list level digits are outside the replaced name span and protected'
Check ((GetLabelField $result.Labels[0] 'SourceBoundaryEstimated') -eq $true) 'single-word name/level split is recorded as estimated, not measured'
$result = TryList (NewListContext @('Heal')).Lines 0
Check (-not $result.Ok -and $result.Labels.Count -eq 0) 'a bare skill name without list context is not replaced'
$result = TryList (NewListContext @('SKILL INVENTORY', 'Heal please')).Lines 1
Check (-not $result.Ok -and $result.Labels.Count -eq 0) 'an ordinary sentence containing heal is not replaced'
$result = TryList (NewListContext @('SKILL INVENTORY', 'Arrow Bom...')).Lines 1
Check (-not $result.Ok -and $result.Labels.Count -eq 0) 'a truncated list name is not guessed'
$result = TryList (NewListContext @('SKILL INVENTORY', 'Rush')).Lines 1
Check ($result.Ok -and $result.Labels.Count -eq 1 -and (GetLabelField $result.Labels[0] 'Text') -eq '突进') 'shared list translation is allowed without choosing a job id'
$farList = NewLineList @(
    (NewLine 'SKILL INVENTORY' 40 30 240 18),
    (NewLine 'Heal' 900 590 200 18)
)
$result = TryList $farList 1
Check (-not $result.Ok -and $result.Labels.Count -eq 0) 'a header in one panel does not make a far-away standalone Heal a list row'
$farWithLocalNames = NewLineList @(
    (NewLine 'SKILL INVENTORY' 40 30 240 18),
    (NewLine 'Haste' 40 60 200 18),
    (NewLine 'Magic Guard' 40 84 200 18),
    (NewLine 'Heal' 900 590 200 18)
)
$result = TryList $farWithLocalNames 3
Check (-not $result.Ok -and $result.Labels.Count -eq 0) 'a local list elsewhere does not make a distant player name a list row'
$playerOnly = NewLineList @((NewLine 'Heal' 900 590 200 18))
$result = TryList $playerOnly 0
Check (-not $result.Ok -and $result.Labels.Count -eq 0) 'a standalone player name is not a skill-list row'
$listFrame = NewFrame 400 200
DrawSource $listFrame @($headerLevel.Lines[1])
$typedHeaderLevel = [Activator]::CreateInstance($lineListType)
foreach ($line in $headerLevel.Lines) { [void]$typedHeaderLevel.Add($line) }
$listLabel = [Activator]::CreateInstance($labelListType)
$listArgs = New-Object 'object[]' 4
$listArgs[0] = $listLabel; $listArgs[1] = $typedHeaderLevel[1]; $listArgs[2] = $typedHeaderLevel; $listArgs[3] = [single]1
[void]$formType.GetMethod('TryAddSkillListNameLabel', $flags).Invoke($listForm, $listArgs)
Check ((Compose $listForm $listLabel $listFrame) -and (ReadyCount $listForm) -eq 1) 'list name paints while its level guard remains protected'
$guard = GetLabelField $listLabel[0] 'KeepClearBounds'
$layer = $formType.GetField('simpleLayer', $flags).GetValue($listForm)
Check ($null -ne $layer -and $layer.GetPixel([int]$guard.Left + 2, [int]$guard.Top + 2).A -eq 0) 'original level-number pixels survive list-name composition'
$listFrame.Dispose()

# Corpus baseline and controlled 41-panel replay.
$dictionaryRows = Import-Csv (Join-Path $repoRoot '枫语幕词库.tsv') -Delimiter "`t" -Header english,chinese,category,note
$detailRows = @($dictionaryRows | Where-Object { $_.category -like '怀旧服-技能说明#*' })
$levelRows = @($detailRows | Where-Object { $_.note -like 'Henesys.gg等级*' })
$detailIds = @($detailRows | ForEach-Object { $_.category.Split('#')[1] } | Sort-Object -Unique)
$levelIds = @($levelRows | ForEach-Object { $_.category.Split('#')[1] } | Sort-Object -Unique)
$multi = 0
foreach ($id in $levelIds) {
    $distinct = @($levelRows | Where-Object { $_.category -eq "怀旧服-技能说明#$id" } |
        ForEach-Object { $_.english } | Sort-Object -Unique)
    if ($distinct.Count -ge 2) { $multi++ }
}
Check ($detailIds.Count -eq 264 -and $levelRows.Count -eq 3804 -and $multi -eq 164) 'corpus baseline matches 264 ids / 3804 effects / 164 multi-effect skills'
$fixturePath = Join-Path $repoRoot 'tests/fixtures/skill-screenshot-geometry-r15.json'
$fixtures = (Get-Content $fixturePath -Raw | ConvertFrom-Json).fixtures
$allComposed = 0
$plannedSections = 0
$roleCounts = @{ body = 0; current = 0; next = 0 }
$preservedSections = @()
foreach ($fixture in $fixtures) {
    $lines = @()
    $malformed = $false
    foreach ($row in $fixture.rows) {
        $text = [string]$row.text
        if ($text.Contains("`t") -and $text -match '\d') { $malformed = $true }
        $b = $row.bounds
        $lines += (NewLine $text $b[0] $b[1] $b[2] $b[3])
    }
    $run = RunPlan -store $store -rows $lines -width ([int]$fixture.width) -height ([int]$fixture.height)
    if (-not $run.Compose -or $run.Ready -ne $run.Labels.Count) {
        throw "FAIL: controlled panel $($fixture.case) planned=$($run.Labels.Count) ready=$($run.Ready)"
    }
    $allComposed++
    $plannedSections += $run.Labels.Count
    $plan = $run.Plan
    foreach ($section in (GetSections $plan)) {
        $role = [string](GetSectionField $section 'Role')
        if ($role -eq 'skill-body') { $roleCounts.body++ }
        elseif ($role -eq 'skill-current-body') { $roleCounts.current++ }
        elseif ($role -eq 'skill-next-body') { $roleCounts.next++ }
        if ([string](GetSectionField $section 'Status') -eq 'preserved') {
            $preservedSections += ("$($fixture.case):${role}:" + [string](GetSectionField $section 'PreservedReason'))
        }
    }
    AssertNoRowOverlap $run.Labels "fixture $($fixture.case)"
}
Check ($allComposed -eq 41) 'all 41 controlled screenshot-geometry panels compose every planned section'
Check (($roleCounts.body + $roleCounts.current + $roleCounts.next) -ge 99) 'visible body/current/next coverage is at least 99 of 104 controlled sections'
Write-Output ("INFO: controlledSections=" + $plannedSections + " body=" + $roleCounts.body +
    " current=" + $roleCounts.current + " next=" + $roleCounts.next +
    " preserved=" + $preservedSections.Count)
foreach ($reason in $preservedSections) { Write-Output "PRESERVED: $reason" }

# Scope guards: no per-skill branch, no OCR/AI round, list policy is explicit.
$planSource = Get-Content (Join-Path $repoRoot 'src\SkillPanelComposition.cs') -Raw -Encoding UTF8
$listSource = Get-Content (Join-Path $repoRoot 'src\SkillNameListPolicy.cs') -Raw -Encoding UTF8
$buildSource = Get-Content (Join-Path $repoRoot 'src\build.ps1') -Raw -Encoding UTF8
$safetySource = Get-Content (Join-Path $repoRoot 'src\SkillOverviewSafety.cs') -Raw -Encoding UTF8
$mainSource = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
foreach ($name in 'Heal', 'Rush', 'Arrow Bomb', 'Sword Mastery', 'Polearm', 'Warrior', 'Magician', 'Scania') {
    Check (-not ($planSource -match [regex]::Escape($name)) -and
        -not ($listSource -match [regex]::Escape($name))) "no $name-specific branch in the new skill planner"
}
Check ($planSource.Contains('SplitSkillSemanticRows') -and $listSource.Contains('IsListContext') -and
    $listSource.Contains('TryAddSkillListNameLabel')) 'shared planner and explicit list context are wired'
Check ($buildSource.Contains('SkillPanelComposition.cs') -and $buildSource.Contains('SkillNameListPolicy.cs') -and
    $buildSource.Contains('SkillOverviewSafety.cs')) 'build includes planner, list policy and overview safety sources'
Check (-not ($planSource -match '(?i)\b(?:RunOcr|OcrCalls|AiRequest|Prompt|Retry)\b')) 'planner adds no OCR or AI round'
Check (-not ($mainSource.Contains('independentSkillNames')) -and
    $mainSource.Contains('TryAddSkillListNameLabel(output, line, allLines, ocrScale)')) 'main dictionary pass routes bare skill names through explicit list context'

Write-Output "SKILL-001 logic: $checks checks passed; candidate SHA256=$((Get-FileHash $ExePath -Algorithm SHA256).Hash)"
Write-Output 'Evidence boundary: controlled OCR geometry, actual built EXE reflection, native GDI+ font measurement; original 36 screenshots and Windows native OCR/real game remain UNVERIFIED.'
