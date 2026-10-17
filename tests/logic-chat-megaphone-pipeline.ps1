$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
}
$assembly = Load-TestAssembly $exe
$all = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'
$script:bindingFlags = $all

$chatType = $assembly.GetType('MapleOverlay.OfflineChatForm', $true)
$overlayType = $assembly.GetType('MapleOverlay.OverlayForm', $true)
$floatingType = $assembly.GetType('MapleOverlay.AiTranslationWindowForm', $true)
$styleType = $assembly.GetType('MapleOverlay.ChatVisualStyle', $true)
$categoryType = $assembly.GetType('MapleOverlay.ChatSourceCategory', $true)
$kindType = $assembly.GetType('MapleOverlay.ChatVisualKind', $true)
$frameType = $assembly.GetType('MapleOverlay.ChatCaptureFrame', $true)
$lineType = $assembly.GetType('MapleOverlay.ChatCaptureLine', $true)
$evidenceType = $assembly.GetType('MapleOverlay.ChatColorEvidence', $true)
$analyze = $evidenceType.GetMethod('Analyze', $all)
$findStyle = $frameType.GetMethod('FindStyle', $all, $null, [Type[]]@([string], [int]), $null)
$findStyles = $frameType.GetMethod('FindStyles', $all)
$addSourceChanged = $chatType.GetMethod('AddSourceChangedChatLines', $all)
$framesShareStyles = $overlayType.GetMethod('FramesShareStyles',
    [Reflection.BindingFlags]'Static,NonPublic,Public')
$styleListType = [System.Collections.Generic.List``1].MakeGenericType($styleType)

$script:checks = 0
function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++
    Write-Host ("PASS: {0}" -f $message)
}
function Get-Field($value, [string]$name) {
    if ($null -eq $value) { throw "Get-Field($name): 值为空" }
    $field = $value.GetType().GetField($name, $script:bindingFlags)
    if ($null -ne $field) { Write-Output -NoEnumerate $field.GetValue($value); return }
    $property = $value.GetType().GetProperty($name, $script:bindingFlags)
    if ($null -ne $property) { Write-Output -NoEnumerate $property.GetValue($value, $null); return }
    throw "Get-Field($name): 类型 $($value.GetType().FullName) 没有该字段或属性"
}
function New-Style([string]$category, [string]$kind, [bool]$hasBackground) {
    $style = [Activator]::CreateInstance($styleType, $true)
    $styleType.GetField('Category', $all).SetValue($style, [Enum]::Parse($categoryType, $category))
    $styleType.GetField('Kind', $all).SetValue($style, [Enum]::Parse($kindType, $kind))
    $styleType.GetField('HasBackground', $all).SetValue($style, $hasBackground)
    return $style
}
function Add-CaptureLine($frame, [string]$text, $style, [float]$top) {
    $line = [Activator]::CreateInstance($lineType, $true)
    $lineType.GetField('Text', $all).SetValue($line, $text)
    $lineType.GetField('Style', $all).SetValue($line, $style)
    $lineType.GetField('Bounds', $all).SetValue($line,
        [System.Drawing.RectangleF]::new(10, $top, 400, 20))
    $lines = Get-Field $frame 'Lines'
    $lines.Add($line)
}
function Get-FoundCategory($frame, [string]$text, [int]$occurrence) {
    $style = $findStyle.Invoke($frame, [object[]]@($text, $occurrence))
    return [string](Get-Field $style 'Category')
}
function To-StringList([string[]]$values) {
    $list = [System.Collections.Generic.List[string]]::new()
    foreach ($value in $values) { $list.Add($value) }
    return ,$list
}
function To-StyleList([object[]]$values) {
    $list = [Activator]::CreateInstance($styleListType)
    foreach ($value in $values) { $list.Add($value) }
    return ,$list
}

# A. Physical-row association: different players with the same sentence body,
# absent players, repeated identical posts, bullet/badge noise and wrapped tails.
$frame = [Activator]::CreateInstance($frameType, $true)
Add-CaptureLine $frame 'Alice: selling hat 40k' (New-Style 'Buddy' 'Chat' $false) 10
Add-CaptureLine $frame 'Bob: selling hat 40k' (New-Style 'Party' 'Chat' $false) 40
Add-CaptureLine $frame 'Alice: hello' (New-Style 'Megaphone' 'Megaphone' $true) 70
Add-CaptureLine $frame 'Alice: hello' (New-Style 'Guild' 'Chat' $false) 100
Add-CaptureLine $frame 'Jae • where do i bring the magic box?' (New-Style 'Megaphone' 'Megaphone' $true) 130
Add-CaptureLine $frame 'Toph CH01: Anyone know a fix?' (New-Style 'Megaphone' 'Megaphone' $true) 160
Add-CaptureLine $frame 'BeelsFad«HÖl 0 : T> Int Bamboo Hat' (New-Style 'Megaphone' 'SuperMegaphone' $true) 190
Add-CaptureLine $frame 'Alice: hello world' (New-Style 'Party' 'Chat' $false) 220
Add-CaptureLine $frame 'this is a wrapped tail' (New-Style 'Unknown' 'Chat' $false) 250

Assert-True ((Get-FoundCategory $frame 'Alice: selling hat 40k' 0) -eq 'Buddy') 'same body with another speaker does not steal Alice row'
Assert-True ((Get-FoundCategory $frame 'Bob: selling hat 40k' 0) -eq 'Party') 'Bob row keeps its own Party source'
Assert-True ((Get-FoundCategory $frame 'Carol: selling hat 40k' 0) -eq 'Unknown') 'absent speaker is not associated by a shared body'
Assert-True ((Get-FoundCategory $frame 'Alice: completely different sentence' 0) -eq 'Unknown') 'short shared fragments do not associate a row'
Assert-True ((Get-FoundCategory $frame 'Alice: hello' 0) -eq 'Megaphone') 'first duplicate keeps the first physical row source'
Assert-True ((Get-FoundCategory $frame 'Alice: hello' 1) -eq 'Guild') 'second duplicate keeps the second physical row source'
Assert-True ((Get-FoundCategory $frame 'Jae: where do i bring the magic box?' 0) -eq 'Megaphone') 'bullet badge on the physical row still associates'
Assert-True ((Get-FoundCategory $frame 'Toph: Anyone know a fix?' 0) -eq 'Megaphone') 'CHxx badge on the physical row still associates'
Assert-True ((Get-FoundCategory $frame 'BeelsFad: T> Int Bamboo Hat' 0) -eq 'Megaphone') 'OCR badge noise on the physical name still associates'
Assert-True ((Get-FoundCategory $frame 'Alice: hello world this is a wrapped tail' 0) -eq 'Party') 'wrapped tail uses the speaker row source'
$duplicates = [System.Collections.Generic.List[string]]::new()
$duplicates.Add('Alice: hello')
$duplicates.Add('Alice: hello')
$duplicateStyles = $findStyles.Invoke($frame, [object[]]@(,$duplicates))
Assert-True (([string](Get-Field $duplicateStyles[0] 'Category') -eq 'Megaphone') -and
    ([string](Get-Field $duplicateStyles[1] 'Category') -eq 'Guild')) 'FindStyles assigns one physical row per duplicate'

# B. Same text scrolled to another channel is a new source occurrence.
$previous = To-StringList @('Alice: hi')
$previousStyles = To-StyleList @((New-Style 'Megaphone' 'SuperMegaphone' $true))
$current = To-StringList @('Alice: hi')
$currentStyles = To-StyleList @((New-Style 'Megaphone' 'Megaphone' $true))
$changedArgs = [object[]]@($previous, $previousStyles, $current, $currentStyles,
    (To-StringList @()), $false)
$changedResult = $addSourceChanged.Invoke($null, $changedArgs)
Assert-True ([bool]$changedArgs[5]) 'pink-to-blue same text is detected as a source change'
Assert-True ($changedResult.Count -eq 1 -and $changedResult[0] -eq 'Alice: hi') 'source change delivers the same text once'
$noChangeArgs = [object[]]@((To-StringList @('Alice: hi')),
    (To-StyleList @((New-Style 'Megaphone' 'Megaphone' $true))),
    (To-StringList @('Alice: hi')),
    (To-StyleList @((New-Style 'Megaphone' 'Megaphone' $true))),
    (To-StringList @()), $false)
[void]$addSourceChanged.Invoke($null, $noChangeArgs)
Assert-True (-not [bool]$noChangeArgs[5]) 'identical same-source frame is not delivered twice'
$unknownArgs = [object[]]@((To-StringList @('Alice: hi')),
    (To-StyleList @((New-Style 'Normal' 'Chat' $false))),
    (To-StringList @('Alice: hi')),
    (To-StyleList @((New-Style 'Unknown' 'Chat' $false))),
    (To-StringList @()), $false)
[void]$addSourceChanged.Invoke($null, $unknownArgs)
Assert-True (-not [bool]$unknownArgs[5]) 'Normal-to-Unknown jitter is not treated as another channel'

# C. Strong-OCR cache identity: text-only reuse must not reuse a different source.
$cacheLeft = [Activator]::CreateInstance($frameType, $true)
$cacheRight = [Activator]::CreateInstance($frameType, $true)
Add-CaptureLine $cacheLeft 'Alice: hi' (New-Style 'Megaphone' 'SuperMegaphone' $true) 10
Add-CaptureLine $cacheRight 'Alice: hi' (New-Style 'Megaphone' 'Megaphone' $true) 10
Assert-True (-not [bool]$framesShareStyles.Invoke($null, [object[]]@($cacheLeft, $cacheRight))) 'text-only cache detects changed source styles'
$cacheSame = [Activator]::CreateInstance($frameType, $true)
Add-CaptureLine $cacheSame 'Alice: hi' (New-Style 'Megaphone' 'SuperMegaphone' $true) 10
Assert-True ([bool]$framesShareStyles.Invoke($null, [object[]]@($cacheLeft, $cacheSame))) 'identical styles may reuse the reviewed frame'

# D. Publication and history: parse -> queue style -> publish -> main/floating filters.
$chatConstructor = $chatType.GetConstructor($all, $null, [Type[]]@($overlayType, [string]), $null)
$form = $chatConstructor.Invoke([object[]]@($null, [string]$repoRoot))
$floatingConstructor = $floatingType.GetConstructor($all, $null, [Type[]]@($overlayType), $null)
$floating = $floatingConstructor.Invoke([object[]]@($null))
try {
    $pendingType = $chatType.GetNestedType('PendingChatLine',
        [Reflection.BindingFlags]'NonPublic')
    $preparedType = $chatType.GetNestedType('PreparedChatLine',
        [Reflection.BindingFlags]'NonPublic')
    $publish = $chatType.GetMethod('PublishPreparedChatLine', $all)
    $historyField = $chatType.GetField('translationHistory', $all)
    $filterField = $chatType.GetField('categoryFilter', $all)
    $outputField = $chatType.GetField('output', $all)
    $history = $historyField.GetValue($form)
    $historySelect = $history.GetType().GetMethod('Select', $all)
    $filter = $filterField.GetValue($form)
    $selectCategory = $filter.GetType().GetMethod('SelectCategory', $all)
    $output = $outputField.GetValue($form)

    function Publish-Line([string]$source, [string]$message, [string]$translation,
        $style) {
        $pending = [Activator]::CreateInstance($pendingType, $true)
        $pendingType.GetField('Text', $all).SetValue($pending, $source)
        $pendingType.GetField('Style', $all).SetValue($pending, $style)
        $prepared = [Activator]::CreateInstance($preparedType, $true)
        $preparedType.GetField('Pending', $all).SetValue($prepared, $pending)
        $preparedType.GetField('Source', $all).SetValue($prepared, $source)
        $preparedType.GetField('SpeakerPrefix', $all).SetValue($prepared, 'Alice: ')
        $preparedType.GetField('Untranslated', $all).SetValue($prepared, $message)
        $preparedType.GetField('Translated', $all).SetValue($prepared, $translation)
        $preparedType.GetField('NameTokens', $all).SetValue($prepared,
            [System.Collections.Generic.Dictionary[string, string]]::new())
        return [bool]$publish.Invoke($form, [object[]]@($prepared))
    }

    $published = 0
    if (Publish-Line 'Alice: hat' 'hat' '出售帽子。' (New-Style 'Normal' 'Chat' $false)) { $published++ }
    if (Publish-Line 'Alice: mega' 'mega' '喇叭公告。' (New-Style 'Megaphone' 'Megaphone' $true)) { $published++ }
    if (Publish-Line 'Alice: guild' 'guild' '家族消息。' (New-Style 'Guild' 'Chat' $false)) { $published++ }
    if (Publish-Line 'Alice: unknown' 'unknown' '未知来源。' (New-Style 'Unknown' 'Chat' $false)) { $published++ }
    Assert-True ($published -eq 4) 'four styled records publish successfully'

    $allRecords = $historySelect.Invoke($history, [object[]]@($null))
    Assert-True ($allRecords.Count -eq 4) 'All keeps every published record'
    $megaRecords = $historySelect.Invoke($history, [object[]]@([Enum]::Parse($categoryType, 'Megaphone')))
    Assert-True ($megaRecords.Count -eq 1) 'Megaphone filter has exactly one record'
    $guildRecords = $historySelect.Invoke($history, [object[]]@([Enum]::Parse($categoryType, 'Guild')))
    Assert-True ($guildRecords.Count -eq 1) 'Guild filter has exactly one record'
    $unknownRecords = $historySelect.Invoke($history, [object[]]@([Enum]::Parse($categoryType, 'Unknown')))
    Assert-True ($unknownRecords.Count -eq 1) 'Unknown filter has exactly one record (All only)'

    $selectCategory.Invoke($filter, [object[]]@([Enum]::Parse($categoryType, 'Megaphone'))) | Out-Null
    Assert-True ($output.Text.Contains('喇叭公告。') -and -not $output.Text.Contains('出售帽子。') -and
        -not $output.Text.Contains('家族消息。') -and -not $output.Text.Contains('未知来源。')) 'main window Megaphone filter excludes every personal/unknown source'
    $selectCategory.Invoke($filter, [object[]]@($null)) | Out-Null
    Assert-True ($output.Text.Contains('出售帽子。') -and $output.Text.Contains('喇叭公告。') -and
        $output.Text.Contains('家族消息。') -and $output.Text.Contains('未知来源。')) 'main window All filter keeps every record'

    $floatingSelect = $floatingType.GetMethod('SelectCategory', $all)
    $floatingAppend = $floatingType.GetMethod('AppendTranslation', $all, $null,
        [Type[]]@([string], $styleType), $null)
    $floatingContent = $floatingType.GetField('content', $all).GetValue($floating)
    $floatingAppend.Invoke($floating, [object[]]@('出售帽子。', (New-Style 'Normal' 'Chat' $false))) | Out-Null
    $floatingAppend.Invoke($floating, [object[]]@('喇叭公告。', (New-Style 'Megaphone' 'Megaphone' $true))) | Out-Null
    $floatingAppend.Invoke($floating, [object[]]@('家族消息。', (New-Style 'Guild' 'Chat' $false))) | Out-Null
    $floatingAppend.Invoke($floating, [object[]]@('未知来源。', (New-Style 'Unknown' 'Chat' $false))) | Out-Null
    $floatingSelect.Invoke($floating, [object[]]@([Enum]::Parse($categoryType, 'Megaphone'))) | Out-Null
    Assert-True ($floatingContent.Text.Contains('喇叭公告。') -and
        -not $floatingContent.Text.Contains('出售帽子。') -and
        -not $floatingContent.Text.Contains('家族消息。') -and
        -not $floatingContent.Text.Contains('未知来源。')) 'floating window Megaphone filter matches the main window'
    $floatingSelect.Invoke($floating, [object[]]@($null)) | Out-Null
    Assert-True ($floatingContent.Text.Contains('出售帽子。') -and
        $floatingContent.Text.Contains('喇叭公告。') -and
        $floatingContent.Text.Contains('家族消息。') -and
        $floatingContent.Text.Contains('未知来源。')) 'floating window All filter matches the main window'

    # Same text on two channels must not inherit the first record's source.
    if (-not (Publish-Line 'Alice: same' 'same' '相同文本。' (New-Style 'Megaphone' 'Megaphone' $true))) {
        throw 'same-text first publication failed'
    }
    if (-not (Publish-Line 'Alice: same' 'same' '相同文本。' (New-Style 'Guild' 'Chat' $false))) {
        throw 'same-text second publication failed'
    }
    $sameAll = $historySelect.Invoke($history, [object[]]@($null))
    $sameMega = $historySelect.Invoke($history, [object[]]@([Enum]::Parse($categoryType, 'Megaphone')))
    $sameGuild = $historySelect.Invoke($history, [object[]]@([Enum]::Parse($categoryType, 'Guild')))
    Assert-True ($sameAll.Count -eq 6 -and $sameMega.Count -eq 2 -and $sameGuild.Count -eq 2) 'same text carries each channel record independently'
} finally {
    $floating.Dispose()
    $form.Dispose()
}

# E. Evidence cost and OCR call count.
$pink = Join-Path $repoRoot 'tests/fixtures/chat-pink-super-megaphone-video.png'
$bitmap = [System.Drawing.Bitmap]::new($pink)
try {
    $rect = [System.Drawing.Rectangle]::new(0, 0, $bitmap.Width, $bitmap.Height)
    $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $pixels = [int[]]::new($bitmap.Width * $bitmap.Height)
        for ($y = 0; $y -lt $bitmap.Height; $y++) {
            [System.Runtime.InteropServices.Marshal]::Copy(
                [IntPtr]::Add($data.Scan0, $y * $data.Stride), $pixels,
                $y * $bitmap.Width, $bitmap.Width)
        }
    } finally { $bitmap.UnlockBits($data) }
    $target = [System.Drawing.Rectangle]::new(16, 73, 920, 28)
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt 100; $i++) {
        [void]$analyze.Invoke($null, [object[]]@($pixels, $bitmap.Width, $bitmap.Height,
            $target, $null))
    }
    $stopwatch.Stop()
    $average = $stopwatch.Elapsed.TotalMilliseconds / 100.0
    Assert-True ($average -lt 25.0) ("pixel evidence cost {0:N2} ms per 920x28 row" -f $average)
} finally { $bitmap.Dispose() }

$overlaySource = Get-Content (Join-Path $repoRoot 'src/MapleOverlay.cs') -Raw -Encoding UTF8
$start = $overlaySource.IndexOf('private async Task<ChatCaptureFrame> AnalyzeChatBitmapAsync')
$end = $overlaySource.IndexOf('private sealed class StrongChatRow', $start)
$captureBody = $overlaySource.Substring($start, $end - $start)
$recognizeCalls = [regex]::Matches($captureBody, 'RecognizeAsync\(').Count
Assert-True ($recognizeCalls -eq 2) 'capture path keeps exactly one fast + one strong OCR call'
Assert-True ($captureBody.Contains('FramesShareStyles(frame, lastReviewedChatFrame)')) 'reviewed-frame cache checks source styles'
$chatSource = Get-Content (Join-Path $repoRoot 'src/OfflineChat.cs') -Raw -Encoding UTF8
Assert-True ($chatSource.Contains('List<ChatVisualStyle> lineStyles = capture.FindStyles(lines)')) 'queue receives per-row styles after parsing'
Assert-True ($chatSource.Contains('AddSourceChangedChatLines(priorFrame, priorStyles')) 'same-text source changes are fed into the queue'
Assert-True (([regex]::Matches($chatSource, 'OnlineAiClient\.TranslateAsync\(').Count) -eq 2) 'AI translation request sites remain at the v4.0 count (2)'

Write-Host ("Chat megaphone pipeline regression: {0} checks passed; physical-row association, duplicate sources, scroll source change, cache identity, parse→queue→publish→history, main/floating filters, detection cost and OCR call count covered." -f $script:checks)
