$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
$dict = Join-Path $repoRoot '枫语幕词库.tsv'
if (-not (Test-Path -LiteralPath $exe)) { throw "找不到程序集：$exe" }
if (-not (Test-Path -LiteralPath $dict)) { throw "找不到词库：$dict" }
if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) { . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1') }
$assembly = Load-TestAssembly $exe
$flags = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'
function TypeOf([string]$name) { return $assembly.GetType('MapleOverlay.' + $name, $true) }
function NewInternal([string]$name) {
    $t = TypeOf $name
    $ctor = @($t.GetConstructors($flags) | Where-Object { $_.GetParameters().Count -eq 0 })[0]
    if ($null -ne $ctor) { return $ctor.Invoke(@()) }
    return [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($t)
}
function SetField($object, [string]$name, $value) { $object.GetType().GetField($name, $flags).SetValue($object, $value) }
function GetField($object, [string]$name) { return $object.GetType().GetField($name, $flags).GetValue($object) }

$storeType = TypeOf 'TranslationStore'
$storeCtor = @($storeType.GetConstructors($flags) | Where-Object { $_.GetParameters().Count -eq 1 })[0]
$store = $storeCtor.Invoke(@([string]$dict))
$storeType.GetMethod('Load', $flags).Invoke($store, @()) | Out-Null
$mayaArgs = [object[]]@([string]"Maya's First Collection", [string]"", [string]"")
$maya = $storeType.GetMethod('SelectJournal', $flags).Invoke($store, $mayaArgs)
if ($null -eq $maya) { throw 'Maya journal record not found' }
$mayaEnglish = [string](GetField $maya 'English')
$mayaChinese = [string](GetField $maya 'Chinese')
$mayaTitle = [string](GetField $maya 'ChineseTitle')
if ([string]::IsNullOrWhiteSpace($mayaEnglish) -or [string]::IsNullOrWhiteSpace($mayaChinese)) { throw 'Maya record is incomplete' }

$lineType = TypeOf 'OcrLine'
$wordType = TypeOf 'OcrWord'
function NewLine([string]$text, [single]$x, [single]$y, [single]$w, [single]$h) {
    $line = NewInternal 'OcrLine'
    SetField $line 'Text' $text
    $word = NewInternal 'OcrWord'
    SetField $word 'Text' $text
    SetField $word 'BoundingRect' ([Drawing.RectangleF]::new($x, $y, $w, $h))
    $wordsField = $line.GetType().GetField('Words', $flags)
    if ($null -eq $wordsField) { throw ('OcrLine has no Words field: ' + $line.GetType().FullName) }
    $words = $wordsField.GetValue($line)
    if ($null -eq $words) { throw ('OcrLine Words is null; ctor=' + $line.GetType().GetConstructors($flags)[0]) }
    $words.Add($word)
    return $line
}
$lineListType = [System.Collections.Generic.List``1].MakeGenericType($lineType)
$labelListType = [System.Collections.Generic.List``1].MakeGenericType((TypeOf 'OverlayLabel'))
$rectListType = [System.Collections.Generic.List``1].MakeGenericType([Drawing.Rectangle])
$labelType = TypeOf 'OverlayLabel'
$docType = TypeOf 'QuestJournalDocument'
$formType = TypeOf 'OverlayForm'
$form = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($formType)
SetField $form 'translations' $store
$formType.GetProperty('Bounds', $flags).SetValue($form, [Drawing.Rectangle]::Empty, $null)
SetField $form 'captureBounds' ([Drawing.Rectangle]::new(0, 0, 1000, 700))

function NewFrame([single]$factor, [bool]$summary, [int]$objectiveCount, [string[]]$ProseLines = @()) {
    $w = [int][Math]::Ceiling(1000 * $factor)
    $h = [int][Math]::Ceiling(700 * $factor)
    $bmp = [Drawing.Bitmap]::new($w, $h)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([Drawing.Color]::FromArgb(255, 32, 48, 48))
    function P([single]$x, [single]$y, [single]$ww, [single]$hh) {
        return [Drawing.Rectangle]::new([int]($x * $factor), [int]($y * $factor),
            [int]($ww * $factor), [int]($hh * $factor))
    }
    $paper = P 400 290 410 300
    $summaryCard = P 400 150 410 140
    $g.FillRectangle([Drawing.Brushes]::White, $paper)
    $g.FillRectangle([Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 225, 222, 211)), $summaryCard)
    $font = [Drawing.Font]::new('Arial', [single](14 * $factor), [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    $g.DrawString("Maya's First Collection", $font, [Drawing.Brushes]::Black, 410 * $factor, 315 * $factor)
    $rows = @()
    $words = $mayaEnglish.Split(' ')
    $current = ''
    foreach ($word in $words) {
        if (($current + ' ' + $word).Length -gt 52) { $rows += $current; $current = $word }
        else { if ($current.Length -gt 0) { $current += ' ' }; $current += $word }
    }
    if ($current.Length -gt 0) { $rows += $current }
    for ($i = 0; $i -lt $rows.Count; $i++) {
        $g.DrawString($rows[$i], $font, [Drawing.Brushes]::Black, 410 * $factor, (346 + $i * 26) * $factor)
    }
    for ($i = 0; $i -lt $ProseLines.Count; $i++) {
        $g.DrawString($ProseLines[$i], $font, [Drawing.Brushes]::Black, 410 * $factor, (346 + ($rows.Count + $i) * 26) * $factor)
    }
    if ($summary) { $g.DrawString('Quest Summary', $font, [Drawing.Brushes]::Black, 410 * $factor, 160 * $factor) }
    for ($i = 0; $i -lt $objectiveCount; $i++) {
        $g.DrawString(('Objective item ' + ($i + 1) + ' 0 / 1'), $font, [Drawing.Brushes]::Black,
            420 * $factor, (205 + $i * 26) * $factor)
    }
    $g.Dispose()
    $lines = [Activator]::CreateInstance($lineListType)
    if ($summary) { $lines.Add((NewLine 'Quest Summary' (410 * $factor) (160 * $factor) (180 * $factor) (20 * $factor))) }
    $lines.Add((NewLine "Maya's First Collection" (410 * $factor) (315 * $factor) (300 * $factor) (20 * $factor)))
    for ($i = 0; $i -lt $rows.Count; $i++) {
        $lines.Add((NewLine $rows[$i] (410 * $factor) ((346 + $i * 26) * $factor) (380 * $factor) (20 * $factor)))
    }
    for ($i = 0; $i -lt $ProseLines.Count; $i++) {
        $lines.Add((NewLine $ProseLines[$i] (410 * $factor) ((346 + ($rows.Count + $i) * 26) * $factor) (380 * $factor) (20 * $factor)))
    }
    for ($i = 0; $i -lt $objectiveCount; $i++) {
        $lines.Add((NewLine ('Objective item ' + ($i + 1) + ' 0 / 1') (420 * $factor) ((205 + $i * 26) * $factor) (300 * $factor) (20 * $factor)))
    }
    return [pscustomobject]@{ Bitmap = $bmp; Lines = $lines; Factor = $factor; Rows = $rows; Paper = $paper; FirstBodyTop = (346 * $factor); LastBodyBottom = ((346 + ($rows.Count + $ProseLines.Count - 1) * 26 + 20) * $factor); ProseLines = $ProseLines }
}

function Invoke-JournalBuilder($scene) {
    SetField $form 'captureBounds' ([Drawing.Rectangle]::new(0, 0, $scene.Bitmap.Width, $scene.Bitmap.Height))
    $output = [Activator]::CreateInstance($labelListType)
    $measured = [Drawing.RectangleF]::new(385 * $scene.Factor, 30 * $scene.Factor, 445 * $scene.Factor, 555 * $scene.Factor)
    $ok = $formType.GetMethod('TryAddQuestJournal', $flags).Invoke($form,
        [object[]]@($output, $scene.Lines, $scene.Lines, $scene.Bitmap, [single]1, $true, $measured))
    if (-not $ok -or $output.Count -ne 1) { return $null }
    return $output[0]
}

function NewJournalLabel([Drawing.Rectangle]$area, [string]$title, [string]$body, [single]$height) {
    $label = NewInternal 'OverlayLabel'
    SetField $label 'Bounds' ([Drawing.RectangleF]::new($area.X, $area.Y, $area.Width, $area.Height))
    SetField $label 'SourceBounds' ([Drawing.RectangleF]::new($area.X, $area.Y, $area.Width, $area.Height))
    SetField $label 'PanelBounds' ([Drawing.RectangleF]::new($area.X, $area.Y, $area.Width, $area.Height))
    SetField $label 'HasSourceBounds' $true
    SetField $label 'Text' (($(if ($title.Length -gt 0) { $title + "`n`n" } else { '' })) + $body)
    SetField $label 'Wrap' $true
    SetField $label 'SemanticRole' 'task-journal'
    SetField $label 'Confidence' ([Enum]::Parse((TypeOf 'LabelConfidence'), 'High'))
    $doc = NewInternal 'QuestJournalDocument'
    SetField $doc 'Title' $title
    SetField $doc 'Body' $body
    SetField $doc 'Background' ([Drawing.Color]::FromArgb(250, 248, 241))
    SetField $doc 'FontHeight' $height
    SetField $label 'Journal' $doc
    return $label
}

function Invoke-JournalPlan($label) {
    $area = [Drawing.Rectangle]::Ceiling((GetField $label 'SourceBounds'))
    $measureBmp = [Drawing.Bitmap]::new(1, 1)
    $measure = [Drawing.Graphics]::FromImage($measureBmp)
    $values = [Activator]::CreateInstance($rectListType)
    $args = [object[]]@($label, $area, [Drawing.Rectangle]::new(0, 0, 1000, 700), $values, $measure, $null, '')
    $ok = $formType.GetMethod('TryPrepareJournalDraw', $flags).Invoke($form, $args)
    $draw = $args[5]
    $measure.Dispose(); $measureBmp.Dispose()
    if (-not $ok -or $null -eq $draw) { return $null }
    return [pscustomobject]@{ Draw = $draw; Layout = (GetField $draw 'JournalLayout'); Font = (GetField $draw 'Font') }
}

function LayoutMetrics($layout) {
    $type = GetField $layout 'Type'
    $content = GetField $layout 'Content'
    return [pscustomobject]@{
        Unit = GetField $type 'Unit'
        BodySize = [double](GetField $type 'BodySize')
        TitleSize = [double](GetField $type 'TitleSize')
        Content = $content
        MaxLineWidth = [double](GetField $layout 'MaxLineWidth')
        PageCount = [int](GetField $layout 'PageCount')
        Pages = GetField $layout 'Pages'
        Overflow = [string](GetField $layout 'Overflow')
    }
}

function Check([bool]$ok, [string]$name) {
    if (-not $ok) { throw $name }
    $script:checks++
    Write-Output ('PASS: ' + $name)
}
$script:checks = 0
$evidence = [System.Collections.Generic.List[object]]::new()

# 1. Short body, summary pane, one objective count. Production region selection + plan.
$cases = @(
    @{ Name = 'short-with-summary'; Factor = 1.0; Summary = $true; Objectives = 1; Body = $mayaChinese; Title = $mayaTitle },
    @{ Name = 'short-without-summary'; Factor = 1.0; Summary = $false; Objectives = 0; Body = $mayaChinese; Title = $mayaTitle },
    @{ Name = 'three-objectives'; Factor = 1.0; Summary = $true; Objectives = 3; Body = $mayaChinese; Title = $mayaTitle }
)
foreach ($case in $cases) {
    $scene = NewFrame $case.Factor $case.Summary $case.Objectives
    try {
        $label = Invoke-JournalBuilder $scene
        Check ($null -ne $label) ($case.Name + ': production journal label built')
        $source = GetField $label 'SourceBounds'
        $doc = GetField $label 'Journal'
        $layout = GetField $doc 'Layout'
        $m = LayoutMetrics $layout
        Check ($source.Top -le $scene.FirstBodyTop) ($case.Name + ': replacement region covers the first body row')
        Check ($source.Right -ge (410 * $case.Factor + 380 * $case.Factor)) ($case.Name + ': replacement region covers the measured right edge')
        Check ($source.Right -le 830 * $case.Factor) ($case.Name + ': replacement region stays inside the measured panel')
        if ($case.Summary) {
            $summaryBottom = 180 * $case.Factor
            Check ($source.Top -ge $summaryBottom) ($case.Name + ': Quest Summary row is outside the body region')
        }
        if ($case.Objectives -gt 0) {
            for ($i = 0; $i -lt $case.Objectives; $i++) {
                $counter = [Drawing.RectangleF]::new(420 * $case.Factor, (205 + $i * 26) * $case.Factor, 300 * $case.Factor, 20 * $case.Factor)
                Check (-not $source.IntersectsWith($counter)) ($case.Name + ': objective counter ' + ($i + 1) + ' is protected')
            }
        }
        Check ($m.Unit -eq [Drawing.GraphicsUnit]::Pixel) ($case.Name + ': plan font unit is Pixel')
        Check ($m.BodySize -ge 15) ($case.Name + ': body base size is at least 15px')
        Check ($m.TitleSize -gt $m.BodySize) ($case.Name + ': title is larger than body')
        Check ($m.MaxLineWidth -le $m.Content.Width + 0.01) ($case.Name + ': measured line width fits content width')
        Check ($m.PageCount -ge 1) ($case.Name + ': full body has at least one page')
        $evidence.Add([pscustomobject]@{
            case = $case.Name; scale = $case.Factor
            source = @($source.X, $source.Y, $source.Width, $source.Height)
            bodyFontSize = $m.BodySize; titleFontSize = $m.TitleSize
            fontUnit = [string]$m.Unit
            content = @($m.Content.X, $m.Content.Y, $m.Content.Width, $m.Content.Height)
            pageCount = $m.PageCount; maxLineWidth = $m.MaxLineWidth
            overflow = $m.Overflow
            firstBodyTop = $scene.FirstBodyTop; sourceCoversFirst = $source.Top -le $scene.FirstBodyTop
            summaryProtected = (-not $case.Summary) -or $source.Top -ge 180 * $case.Factor
        })
    } finally { $scene.Bitmap.Dispose() }
}

# 1b. QUEST-R1: prose ending in "of 50"/"of 20" is not an objective counter.
$layoutType = TypeOf 'QuestJournalLayout'
$isCounterLike = $layoutType.GetMethod('IsCounterLike', $flags)
$isCounterLine = $layoutType.GetMethod('IsCounterLine', $flags)
foreach ($phrase in @('Please take care of 50', 'Please collect a total of 20', 'care of 20')) {
    Check (-not [bool]$isCounterLike.Invoke($null, @([string]$phrase))) ('R1 prose is not a counter: ' + $phrase)
}
foreach ($phrase in @('stump 30 f 30', 'Pink Anthurium 0 / 1', 'O / 2O', '30 / 30')) {
    Check ([bool]$isCounterLike.Invoke($null, @([string]$phrase))) ('R1 real counter structure recognized: ' + $phrase)
}
Check (-not [bool]$isCounterLike.Invoke($null, @('A30 f 30'))) 'R1 counter pair needs a word boundary'
$targetCard = [Drawing.RectangleF]::new(400, 190, 310, 70)
$targetLine = [Drawing.RectangleF]::new(420, 205, 150, 20)
$outsideLine = [Drawing.RectangleF]::new(100, 500, 150, 20)
$targetCards = [System.Collections.Generic.List[Drawing.RectangleF]]::new()
$targetCards.Add($targetCard)
$noCards = [System.Collections.Generic.List[Drawing.RectangleF]]::new()
Check ([bool]$isCounterLine.Invoke($null, @('O / O', $targetLine, $targetCards, [single]1))) 'R1 target-column location allows an all-confusable slash count'
Check (-not [bool]$isCounterLine.Invoke($null, @('O / O', $outsideLine, $noCards, [single]1))) 'R1 all-confusable slash count outside the target column is rejected'
Check (-not [bool]$isCounterLine.Invoke($null, @('Please take care of 50', $targetLine, $targetCards, [single]1))) 'R1 prose is not a counter even in a misleading target column'

$proseLines = @('Please take care of 50', 'Please collect a total of 20')
$scene = NewFrame 1.0 $true 1 $proseLines
try {
    $label = Invoke-JournalBuilder $scene
    Check ($null -ne $label) 'R1 prose body with "of 50"/"of 20" still builds a journal label'
    $source = GetField $label 'SourceBounds'
    $lastProseBottom = 346 + ($scene.Rows.Count + 1) * 26 + 20
    Check ($source.Top -le $scene.FirstBodyTop) 'R1 prose body replacement region still covers the first body row'
    Check ($source.Bottom -ge $lastProseBottom) 'R1 prose body replacement region is not emptied by counter detection'
    Check ((GetField (GetField $label 'Journal') 'Layout') -ne $null) 'R1 prose body still receives the shared typography plan'
    $evidence.Add([pscustomobject]@{
        case = 'r1-prose-not-counter'; scale = 1.0
        source = @($source.X, $source.Y, $source.Width, $source.Height)
        firstBodyTop = $scene.FirstBodyTop; lastProseBottom = $lastProseBottom
        sourceCoversFirst = $source.Top -le $scene.FirstBodyTop
        sourceCoversLastProse = $source.Bottom -ge $lastProseBottom
        counterExamples = @('Please take care of 50', 'Please collect a total of 20')
    })
} finally { $scene.Bitmap.Dispose() }

# 1c. QUEST-R2: actual-GDI+ emoji/supplementary plan, no hang and no character loss.
function Test-LoneSurrogate([string]$value) {
    for ($i = 0; $i -lt $value.Length; $i++) {
        if ([char]::IsHighSurrogate($value[$i])) {
            if ($i + 1 -ge $value.Length -or -not [char]::IsLowSurrogate($value[$i + 1])) { return $true }
            $i++
        } elseif ([char]::IsLowSurrogate($value[$i])) { return $true }
    }
    return $false
}
$emoji = [char]::ConvertFromUtf32(0x1F600)
$rocket = [char]::ConvertFromUtf32(0x1F680)
$supplementary = [char]::ConvertFromUtf32(0x2000B)
foreach ($case in @(
    @{ Name = 'emoji-a'; Title = ('A' + $emoji); Body = ('A' + $emoji); ExpectPages = $false },
    @{ Name = 'supplementary-long'; Title = ('标题' + $emoji + $supplementary); Body = ((1..24 | ForEach-Object { '第' + $_ + '段：汉字' + $emoji + $rocket + $supplementary + '与补充平面字符必须完整换行，不能丢字，也不能拆开代理对。' }) -join "`n`n"); ExpectPages = $true }
)) {
    $label = NewJournalLabel ([Drawing.Rectangle]::new(400, 300, 410, 290)) $case.Title $case.Body 20
    $plan = Invoke-JournalPlan $label
    Check ($null -ne $plan) ('R2 actual-GDI+ plan returns for ' + $case.Name)
    $layout = $plan.Layout
    $m = LayoutMetrics $layout
    Check ((($m.Pages -join '') -eq $case.Body)) ('R2 ' + $case.Name + ' preserves every character')
    Check (-not (Test-LoneSurrogate (($m.Pages -join '')))) ('R2 ' + $case.Name + ' pages contain no lone surrogates')
    $titleLines = GetField $layout 'TitleLines'
    Check ((($titleLines -join '') -eq $case.Title)) ('R2 ' + $case.Name + ' title preserves every character')
    Check (-not (Test-LoneSurrogate (($titleLines -join '')))) ('R2 ' + $case.Name + ' title contains no lone surrogates')
    if ($case.ExpectPages) { Check ($m.PageCount -gt 1) ('R2 ' + $case.Name + ' paginates') }
    $evidence.Add([pscustomobject]@{
        case = ('r2-' + $case.Name); scale = 1.0
        bodyFontSize = $m.BodySize; titleFontSize = $m.TitleSize
        fontUnit = [string]$m.Unit
        pageCount = $m.PageCount; overflow = $m.Overflow
        titleCharacters = $case.Title.Length; bodyCharacters = $case.Body.Length
        pagesNoLoneSurrogates = $true
    })
}

# 2. Real GDI+ plan for long and short manual bodies (pagination, no truncation).
foreach ($case in @(
    @{ Name = 'manual-short'; Body = '这是一段较短的任务正文。'; Title = '短正文' },
    @{ Name = 'manual-long'; Body = (1..24 | ForEach-Object { '第' + $_ + '段：这是一段用于验证任务正文排版、自动换行和分页的完整中文内容，不能静默截断。' }) -join "`n`n"; Title = '长正文' }
)) {
    try {
        $label = NewJournalLabel ([Drawing.Rectangle]::new(400, 300, 410, 290)) $case.Title $case.Body 20
        $plan = Invoke-JournalPlan $label
        Check ($null -ne $plan) ($case.Name + ': composition plan built with actual GDI+ metrics')
        $layout = $plan.Layout
        $m = LayoutMetrics $layout
        if ($case.Name -eq 'manual-long') {
            Check ($m.PageCount -gt 1) ($case.Name + ': long body paginates instead of shrinking')
            Check ((($m.Pages -join '') -eq $case.Body)) ($case.Name + ': pagination preserves all characters')
        } else {
            Check ($m.PageCount -eq 1) ($case.Name + ': short body stays on one page')
        }
        Check ($m.BodySize -ge 15) ($case.Name + ': body base size stays readable')
        Check ($m.MaxLineWidth -le $m.Content.Width + 0.01) ($case.Name + ': actual measured line width fits content')
        $evidence.Add([pscustomobject]@{
            case = $case.Name; scale = 1.0
            source = @((GetField $label 'SourceBounds').X, (GetField $label 'SourceBounds').Y, (GetField $label 'SourceBounds').Width, (GetField $label 'SourceBounds').Height)
            bodyFontSize = $m.BodySize; titleFontSize = $m.TitleSize
            fontUnit = [string]$m.Unit
            content = @($m.Content.X, $m.Content.Y, $m.Content.Width, $m.Content.Height)
            pageCount = $m.PageCount; maxLineWidth = $m.MaxLineWidth
            overflow = $m.Overflow
        })
    } finally { }
}

# 3. 100/125/150/200% scale. Region, typography and measured widths must stay bounded.
$previous = 0.0
foreach ($factor in @(1.0, 1.25, 1.5, 2.0)) {
    $scene = NewFrame $factor $true 1
    try {
        $label = Invoke-JournalBuilder $scene
        Check ($null -ne $label) ('scale ' + $factor + ': production journal label built')
        $doc = GetField $label 'Journal'
        $layout = GetField $doc 'Layout'
        $m = LayoutMetrics $layout
        Check ($m.Unit -eq [Drawing.GraphicsUnit]::Pixel) ('scale ' + $factor + ': font unit is Pixel')
        Check ($m.BodySize -ge 15) ('scale ' + $factor + ': body base size is readable')
        Check ($m.BodySize -ge $previous) ('scale ' + $factor + ': body base size does not shrink')
        Check ($m.MaxLineWidth -le $m.Content.Width + 0.01) ('scale ' + $factor + ': measured width fits')
        $previous = $m.BodySize
        $evidence.Add([pscustomobject]@{
            case = 'scale-' + $factor; scale = $factor
            source = @((GetField $label 'SourceBounds').X, (GetField $label 'SourceBounds').Y, (GetField $label 'SourceBounds').Width, (GetField $label 'SourceBounds').Height)
            bodyFontSize = $m.BodySize; titleFontSize = $m.TitleSize
            fontUnit = [string]$m.Unit
            content = @($m.Content.X, $m.Content.Y, $m.Content.Width, $m.Content.Height)
            pageCount = $m.PageCount; maxLineWidth = $m.MaxLineWidth
            overflow = $m.Overflow
        })
    } finally { $scene.Bitmap.Dispose() }
}

# 4. Repeated F8: the same confirmed panel must produce the same region/plan.
$scene = NewFrame 1.0 $true 1
try {
    $firstLabel = Invoke-JournalBuilder $scene
    $firstLayout = GetField (GetField $firstLabel 'Journal') 'Layout'
    $firstMetrics = LayoutMetrics $firstLayout
    $firstSource = GetField $firstLabel 'SourceBounds'
    $secondLabel = Invoke-JournalBuilder $scene
    $secondLayout = GetField (GetField $secondLabel 'Journal') 'Layout'
    $secondMetrics = LayoutMetrics $secondLayout
    $secondSource = GetField $secondLabel 'SourceBounds'
    Check (($firstSource -eq $secondSource) -and ($firstMetrics.PageCount -eq $secondMetrics.PageCount) -and
        ($firstMetrics.BodySize -eq $secondMetrics.BodySize) -and ($firstMetrics.Overflow -eq $secondMetrics.Overflow)) `
        'repeated F8 planning is deterministic'
    $plan = Invoke-JournalPlan $firstLabel
    Check ($null -ne $plan -and $null -ne $plan.Layout) 'repeated F8 plan is reusable by the compositor'
    $evidence.Add([pscustomobject]@{ case = 'repeat-f8'; scale = 1.0; pageCount = $firstMetrics.PageCount;
        bodyFontSize = $firstMetrics.BodySize; overflow = $firstMetrics.Overflow; reused = $true })
} finally { $scene.Bitmap.Dispose() }

$outDir = Join-Path $repoRoot 'coordination/QUEST-001/evidence'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$outPath = Join-Path $outDir 'layout-metrics.json'
$payload = [pscustomobject]@{
    schema = 1
    annotation = 'Actual GDI+ QuestJournalLayout metrics from synthetic panels; no OCR, AI, capture or in-game visual acceptance.'
    checks = $checks
    cases = $evidence
    realGameVisual = 'UNVERIFIED'
}
$payload | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $outPath -Encoding UTF8
Write-Output ('Quest panel layout: ' + $checks + ' checks passed; metrics=' + $outPath)
