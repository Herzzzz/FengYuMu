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
$findStyleSingle = $frameType.GetMethod('FindStyle', $all, $null, [Type[]]@([string]), $null)
$findStyles = $frameType.GetMethod('FindStyles', $all)
$styleForRow = $frameType.GetMethod('StyleForRow', $all)
$addSourceChangedIndices = $chatType.GetMethod('AddSourceChangedChatLineIndices', $all)
$getNewIndices = $chatType.GetMethod('GetNewChatLineIndices', $all)
$suppressIndices = $chatType.GetMethod('SuppressRecentOcrReappearanceIndices', $all)
$parseWithRows = $chatType.GetMethod('ParseChatLines', $all, $null,
    [Type[]]@([string], [System.Collections.Generic.List[int]]), $null)
$continuityType = $assembly.GetType('MapleOverlay.ChatFrameContinuity', $true)
$resolveIndices = $continuityType.GetMethod('ResolveIndices', $all)
$framesShareStyles = $overlayType.GetMethod('FramesShareStyles',
    [Reflection.BindingFlags]'Static,NonPublic,Public')
$styleListType = [System.Collections.Generic.List``1].MakeGenericType($styleType)
$intListType = [System.Collections.Generic.List[int]]

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
function Get-FoundCategorySingle($frame, [string]$text) {
    $style = $findStyleSingle.Invoke($frame, [object[]]@($text))
    return [string](Get-Field $style 'Category')
}
function Get-RowCategory($frame, [int]$row) {
    $style = $styleForRow.Invoke($frame, [object[]]@($row))
    return [string](Get-Field $style 'Category')
}
function New-IntList([int[]]$values) {
    $list = [System.Collections.Generic.List[int]]::new()
    foreach ($value in $values) { $list.Add($value) }
    return ,$list
}
function Invoke-TwoListMethod($method, $first, $second) {
    $arguments = [object[]]::new(2)
    $arguments[0] = $first
    $arguments[1] = $second
    Write-Output -NoEnumerate $method.Invoke($null, $arguments)
}
function Get-SourceChangeResult($previousTexts, $previousStyles, $currentTexts,
    $currentStyles, $candidates) {
    $arguments = [object[]]::new(6)
    $arguments[0] = $previousTexts
    $arguments[1] = $previousStyles
    $arguments[2] = $currentTexts
    $arguments[3] = $currentStyles
    $arguments[4] = $candidates
    $arguments[5] = $false
    $indices = $addSourceChangedIndices.Invoke($null, $arguments)
    return [pscustomobject]@{ Indices = $indices; Changed = [bool]$arguments[5] }
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
Add-CaptureLine $frame 'Alicia: hello' (New-Style 'Party' 'Chat' $false) 280
Add-CaptureLine $frame 'Alice: hello there' (New-Style 'Guild' 'Chat' $false) 310

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
Assert-True ((Get-FoundCategorySingle $frame 'Alicia: hello') -eq 'Party') 'exact similar nickname keeps its row source'
Assert-True ((Get-FoundCategorySingle $frame 'Alic: hello') -eq 'Unknown') 'ambiguous similar nickname remains Unknown'
Assert-True ((Get-FoundCategorySingle $frame 'hello') -eq 'Unknown') 'missing nickname does not bind by sentence body'
Assert-True ((Get-FoundCategorySingle $frame 'Alice: hello there') -eq 'Guild') 'exact identity outranks a contained fuzzy body'
Assert-True ((Get-FoundCategorySingle $frame 'Alice: hello') -eq 'Unknown') 'text-only duplicate exact identities with different sources are ambiguous'
Assert-True ((Get-FoundCategorySingle $frame 'Alice: hexlo') -eq 'Unknown') 'fuzzy body with two possible rows remains Unknown'

# R1 exact nickname: Alice party vs Alicf guild. Only Alicf is an exact identity.
$nameFrame = [Activator]::CreateInstance($frameType, $true)
Add-CaptureLine $nameFrame 'Alice: hello' (New-Style 'Party' 'Chat' $false) 10
Add-CaptureLine $nameFrame 'Alicf: hello' (New-Style 'Guild' 'Chat' $false) 40
Assert-True ((Get-FoundCategorySingle $nameFrame 'Alicf: hello') -eq 'Guild') 'exact Alicf nickname+body selects Guild without a fuzzy Alice upgrade'
Assert-True ((Get-FoundCategory $nameFrame 'Alicf: hello' 0) -eq 'Guild') 'occurrence API also selects Guild for exact Alicf'
Assert-True ((Get-FoundCategorySingle $nameFrame 'Alice: hello') -eq 'Party') 'exact Alice nickname+body selects Party'
Assert-True ((Get-FoundCategorySingle $nameFrame 'Alic: hello') -eq 'Unknown') 'ambiguous fuzzy nickname is Unknown in the single API'
Assert-True ((Get-FoundCategory $nameFrame 'Alic: hello' 0) -eq 'Unknown') 'ambiguous fuzzy nickname is Unknown in the occurrence API'
Assert-True ((Get-FoundCategorySingle $nameFrame 'hello') -eq 'Unknown') 'missing nickname is Unknown in the exact-nickname fixture'
$duplicates = [System.Collections.Generic.List[string]]::new()
$duplicates.Add('Alice: hello')
$duplicates.Add('Alice: hello')
$duplicateStyles = $findStyles.Invoke($frame, [object[]]@(,$duplicates))
Assert-True (([string](Get-Field $duplicateStyles[0] 'Category') -eq 'Megaphone') -and
    ([string](Get-Field $duplicateStyles[1] 'Category') -eq 'Guild')) 'FindStyles assigns one physical row per duplicate'

# B. Same text scrolled to another channel is a new source occurrence.
$pinkBlue = Get-SourceChangeResult `
    (To-StringList @('Alice: hi')) `
    (To-StyleList @((New-Style 'Megaphone' 'SuperMegaphone' $true))) `
    (To-StringList @('Alice: hi')) `
    (To-StyleList @((New-Style 'Megaphone' 'Megaphone' $true))) `
    (New-IntList @())
Assert-True ($pinkBlue.Changed -and $pinkBlue.Indices.Count -eq 1 -and
    $pinkBlue.Indices[0] -eq 0) 'pink-to-blue same text is detected as a source change'
$sameSource = Get-SourceChangeResult `
    (To-StringList @('Alice: hi')) `
    (To-StyleList @((New-Style 'Megaphone' 'Megaphone' $true))) `
    (To-StringList @('Alice: hi')) `
    (To-StyleList @((New-Style 'Megaphone' 'Megaphone' $true))) `
    (New-IntList @())
Assert-True (-not $sameSource.Changed -and $sameSource.Indices.Count -eq 0) 'identical same-source frame is not delivered twice'
$unknownJitter = Get-SourceChangeResult `
    (To-StringList @('Alice: hi')) `
    (To-StyleList @((New-Style 'Normal' 'Chat' $false))) `
    (To-StringList @('Alice: hi')) `
    (To-StyleList @((New-Style 'Unknown' 'Chat' $false))) `
    (New-IntList @())
Assert-True (-not $unknownJitter.Changed -and $unknownJitter.Indices.Count -eq 0) 'Normal-to-Unknown jitter is not treated as another channel'

# B2. Current-row identity: parse rows, diff by index, resolve style by row and
# deliver a changed source without re-finding duplicate text by prior count.
$rowList = [System.Collections.Generic.List[int]]::new()
$parseArgs = [object[]]::new(2)
$parseArgs[0] = "Alice: hello`nAlice: hello`nBob: hi"
$parseArgs[1] = $rowList
$parsedRows = $parseWithRows.Invoke($null, $parseArgs)
Assert-True ($parsedRows.Count -eq 3 -and $rowList.Count -eq 3) 'parser returns one current row index per parsed message'
Assert-True ($rowList[0] -eq 0 -and $rowList[1] -eq 1 -and $rowList[2] -eq 2) 'duplicate text keeps distinct first/middle/last row indices'
$rowList.Clear()
$wrappedArgs = [object[]]::new(2)
$wrappedArgs[0] = 'Alice: this is a long message that keeps going and will wrap onto the next row' + "`nand this is the wrapped tail"
$wrappedArgs[1] = $rowList
$wrappedParsed = $parseWithRows.Invoke($null, $wrappedArgs)
Assert-True ($wrappedParsed.Count -eq 1 -and $rowList.Count -eq 1 -and $rowList[0] -eq 0) 'wrapped tail keeps the speaker row index'

$diffArgs = [object[]]::new(2)
$diffArgs[0] = To-StringList @('Alice: hi')
$diffArgs[1] = To-StringList @('Alice: hi', 'Alice: hi', 'Bob: ho')
$newIndexList = $getNewIndices.Invoke($null, $diffArgs)
Assert-True ($newIndexList.Count -eq 2 -and $newIndexList[0] -eq 1 -and $newIndexList[1] -eq 2) 'new duplicate occurrence is identified by current row index'

$rowFrame = [Activator]::CreateInstance($frameType, $true)
Add-CaptureLine $rowFrame 'Alice: hi' (New-Style 'Megaphone' 'Megaphone' $true) 10
Add-CaptureLine $rowFrame 'Alice: hi' (New-Style 'Guild' 'Chat' $false) 40
Add-CaptureLine $rowFrame 'Alice: hi' (New-Style 'Megaphone' 'SuperMegaphone' $true) 70
Assert-True ((Get-RowCategory $rowFrame 0) -eq 'Megaphone') 'row 0 resolves its own style'
Assert-True ((Get-RowCategory $rowFrame 1) -eq 'Guild') 'row 1 resolves its own style'
Assert-True ((Get-RowCategory $rowFrame 2) -eq 'Megaphone') 'row 2 resolves its own style'
Assert-True ((Get-RowCategory $rowFrame 3) -eq 'Unknown') 'missing row resolves Unknown, not another duplicate'

$changedIndexArgs = [object[]]::new(6)
$changedIndexArgs[0] = To-StringList @('Alice: hi')
$changedIndexArgs[1] = To-StyleList @((New-Style 'Megaphone' 'SuperMegaphone' $true))
$changedIndexArgs[2] = To-StringList @('Alice: hi')
$changedIndexArgs[3] = To-StyleList @((New-Style 'Megaphone' 'Megaphone' $true))
$changedIndexArgs[4] = New-IntList @()
$changedIndexArgs[5] = $false
$changedIndices = $addSourceChangedIndices.Invoke($null, $changedIndexArgs)
Assert-True ([bool]$changedIndexArgs[5]) 'index flow detects pink-to-blue same-text source change'
Assert-True ($changedIndices.Count -eq 1 -and $changedIndices[0] -eq 0) 'index flow delivers the changed current row, not a prior occurrence'

# R2 general previous/current alignment: the megaphone duplicate is retained and
# moved up after scroll; only the new other row may be enqueued.
$retainedArgs = Get-SourceChangeResult `
    (To-StringList @('Alice: hi', 'Alice: hi')) `
    (To-StyleList @((New-Style 'Normal' 'Chat' $false),
        (New-Style 'Megaphone' 'Megaphone' $true))) `
    (To-StringList @('Alice: hi', 'Bob: new')) `
    (To-StyleList @((New-Style 'Megaphone' 'Megaphone' $true),
        (New-Style 'Party' 'Chat' $false))) `
    (New-IntList @(0, 1))
Assert-True ($retainedArgs.Indices.Count -eq 1 -and $retainedArgs.Indices[0] -eq 1 -and
    -not $retainedArgs.Changed) 'scroll alignment filters the retained duplicate and keeps only the new row'

$retainedSingleArgs = Get-SourceChangeResult `
    (To-StringList @('Alice: hi', 'Alice: hi')) `
    (To-StyleList @((New-Style 'Normal' 'Chat' $false),
        (New-Style 'Megaphone' 'Megaphone' $true))) `
    (To-StringList @('Alice: hi')) `
    (To-StyleList @((New-Style 'Megaphone' 'Megaphone' $true))) `
    (New-IntList @(0))
Assert-True ($retainedSingleArgs.Indices.Count -eq 0 -and -not $retainedSingleArgs.Changed) 'a retained old duplicate alone is not re-enqueued after scroll'

$newSameTextArgs = Get-SourceChangeResult `
    (To-StringList @('Alice: hi')) `
    (To-StyleList @((New-Style 'Megaphone' 'Megaphone' $true))) `
    (To-StringList @('Alice: hi', 'Alice: hi')) `
    (To-StyleList @((New-Style 'Megaphone' 'Megaphone' $true),
        (New-Style 'Guild' 'Chat' $false))) `
    (New-IntList @())
Assert-True ($newSameTextArgs.Indices.Count -eq 1 -and $newSameTextArgs.Indices[0] -eq 1 -and
    $newSameTextArgs.Changed) 'new same-text cross-channel message is not swallowed when diff misses it'

$newSameTextCandidateArgs = Get-SourceChangeResult `
    (To-StringList @('Alice: hi')) `
    (To-StyleList @((New-Style 'Megaphone' 'Megaphone' $true))) `
    (To-StringList @('Alice: hi', 'Alice: hi')) `
    (To-StyleList @((New-Style 'Megaphone' 'Megaphone' $true),
        (New-Style 'Guild' 'Chat' $false))) `
    (New-IntList @(1))
Assert-True ($newSameTextCandidateArgs.Indices.Count -eq 1 -and
    $newSameTextCandidateArgs.Indices[0] -eq 1) 'new same-text cross-channel candidate survives alignment'

$trueSourceChangeArgs = Get-SourceChangeResult `
    (To-StringList @('Alice: hi')) `
    (To-StyleList @((New-Style 'Normal' 'Chat' $false))) `
    (To-StringList @('Alice: hi')) `
    (To-StyleList @((New-Style 'Guild' 'Chat' $false))) `
    (New-IntList @())
Assert-True ($trueSourceChangeArgs.Indices.Count -eq 1 -and
    $trueSourceChangeArgs.Indices[0] -eq 0 -and $trueSourceChangeArgs.Changed) 'same row with a true source change is still delivered'

$firstMiddleLast = To-StringList @('Alice: hi', 'Alice: hi', 'Alice: hi')
$firstMiddleStyles = To-StyleList @(
    (New-Style 'Megaphone' 'SuperMegaphone' $true),
    (New-Style 'Guild' 'Chat' $false),
    (New-Style 'Megaphone' 'Megaphone' $true))
$firstMiddleArgs = [object[]]::new(2)
$firstMiddleArgs[0] = To-StringList @()
$firstMiddleArgs[1] = $firstMiddleLast
$firstMiddleIndices = $getNewIndices.Invoke($null, $firstMiddleArgs)
Assert-True ($firstMiddleIndices.Count -eq 3 -and
    $firstMiddleIndices[0] -eq 0 -and $firstMiddleIndices[1] -eq 1 -and
    $firstMiddleIndices[2] -eq 2) 'first/middle/last duplicate rows are all delivered in row order'
Assert-True (([string](Get-Field $firstMiddleStyles[0] 'Category') -eq 'Megaphone') -and
    ([string](Get-Field $firstMiddleStyles[1] 'Category') -eq 'Guild') -and
    ([string](Get-Field $firstMiddleStyles[2] 'Category') -eq 'Megaphone')) 'first/middle/last duplicate rows keep distinct styles'

$continuity = [Activator]::CreateInstance($continuityType, $true)
$sameDelegate = [Func[string, string, bool]]{ param($left, $right) return $left -eq $right }
$continuityArgs = [object[]]::new(5)
$continuityArgs[0] = To-StringList @()
$continuityArgs[1] = To-StringList @('Alice: hi')
$continuityArgs[2] = New-IntList @(0)
$continuityArgs[3] = $sameDelegate
$continuityArgs[4] = $false
$continuityResult = $resolveIndices.Invoke($continuity, $continuityArgs)
Assert-True ([bool]$continuityArgs[4] -and $continuityResult.Count -eq 1 -and
    $continuityResult[0] -eq 0) 'index continuity advances a first nonempty frame with its current row'
$continuityArgs[0] = To-StringList @('Alice: hi')
$continuityArgs[1] = To-StringList @('Bob: ho')
$continuityArgs[2] = New-IntList @()
$continuityArgs[4] = $false
$continuityResult = $resolveIndices.Invoke($continuity, $continuityArgs)
Assert-True (-not [bool]$continuityArgs[4] -and $continuityResult.Count -eq 0) 'index continuity holds an unanchored replacement'
$continuityResult = $resolveIndices.Invoke($continuity, $continuityArgs)
Assert-True ([bool]$continuityArgs[4] -and $continuityResult.Count -eq 1 -and
    $continuityResult[0] -eq 0) 'index continuity delivers the confirmed replacement row'

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
        $style, [int]$rowIndex = -1) {
        $pending = [Activator]::CreateInstance($pendingType, $true)
        $pendingType.GetField('Text', $all).SetValue($pending, $source)
        $pendingType.GetField('Style', $all).SetValue($pending, $style)
        $pendingType.GetField('RowIndex', $all).SetValue($pending, $rowIndex)
        $prepared = [Activator]::CreateInstance($preparedType, $true)
        $preparedType.GetField('Pending', $all).SetValue($prepared, $pending)
        $preparedType.GetField('Source', $all).SetValue($prepared, $source)
        $preparedType.GetField('SpeakerPrefix', $all).SetValue($prepared, 'Alice: ')
        $preparedType.GetField('Untranslated', $all).SetValue($prepared, $message)
        $preparedType.GetField('Translated', $all).SetValue($prepared, $translation)
        $preparedType.GetField('Style', $all).SetValue($prepared, $style)
        $preparedType.GetField('RowIndex', $all).SetValue($prepared, $rowIndex)
        $preparedType.GetField('NameTokens', $all).SetValue($prepared,
            [System.Collections.Generic.Dictionary[string, string]]::new())
        $preparedRow = [int]$preparedType.GetField('RowIndex', $all).GetValue($prepared)
        $preparedStyle = $preparedType.GetField('Style', $all).GetValue($prepared)
        $preparedCategory = [string]$styleType.GetField('Category', $all).GetValue($preparedStyle)
        if ($preparedRow -ne $rowIndex -or
            $preparedCategory -ne [string](Get-Field $style 'Category')) {
            throw 'prepared publication did not retain the resolved style/row'
        }
        return [bool]$publish.Invoke($form, [object[]]@($prepared))
    }

    $published = 0
    if (Publish-Line 'Alice: hat' 'hat' '出售帽子。' (New-Style 'Normal' 'Chat' $false) 0) { $published++ }
    if (Publish-Line 'Alice: mega' 'mega' '喇叭公告。' (New-Style 'Megaphone' 'Megaphone' $true) 1) { $published++ }
    if (Publish-Line 'Alice: guild' 'guild' '家族消息。' (New-Style 'Guild' 'Chat' $false) 2) { $published++ }
    if (Publish-Line 'Alice: unknown' 'unknown' '未知来源。' (New-Style 'Unknown' 'Chat' $false) 3) { $published++ }
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
    # Guild finishes first, then the megaphone copy from its own current row.
    if (-not (Publish-Line 'Alice: same' 'same' '相同文本。' (New-Style 'Guild' 'Chat' $false) 2)) {
        throw 'same-text guild publication failed'
    }
    if (-not (Publish-Line 'Alice: same' 'same' '相同文本。' (New-Style 'Megaphone' 'Megaphone' $true) 1)) {
        throw 'same-text megaphone publication failed'
    }
    $sameAll = $historySelect.Invoke($history, [object[]]@($null))
    $sameMega = $historySelect.Invoke($history, [object[]]@([Enum]::Parse($categoryType, 'Megaphone')))
    $sameGuild = $historySelect.Invoke($history, [object[]]@([Enum]::Parse($categoryType, 'Guild')))
    Assert-True ($sameAll.Count -eq 6 -and $sameMega.Count -eq 2 -and $sameGuild.Count -eq 2) 'same text carries each channel record independently after delayed publication'

    # First/middle/last duplicate rows complete asynchronously out of order.
    if (-not (Publish-Line 'Alice: triple' 'triple' '最后行。' (New-Style 'Megaphone' 'Megaphone' $true) 2)) {
        throw 'triple last publication failed'
    }
    if (-not (Publish-Line 'Alice: triple' 'triple' '第一行。' (New-Style 'Megaphone' 'SuperMegaphone' $true) 0)) {
        throw 'triple first publication failed'
    }
    if (-not (Publish-Line 'Alice: triple' 'triple' '中间行。' (New-Style 'Guild' 'Chat' $false) 1)) {
        throw 'triple middle publication failed'
    }
    $tripleAll = $historySelect.Invoke($history, [object[]]@($null))
    $tripleCategories = @{}
    foreach ($record in $tripleAll) {
        $recordType = $record.GetType()
        if ([string]$recordType.GetField('Original', $all).GetValue($record) -ne 'Alice: triple') { continue }
        $recordStyle = $recordType.GetField('Style', $all).GetValue($record)
        $tripleCategories[[string]$recordType.GetField('Translation', $all).GetValue($record)] =
            [string]$styleType.GetField('Category', $all).GetValue($recordStyle)
    }
    foreach ($key in $tripleCategories.Keys) {
        Write-Host ("  triple evidence: {0} => {1}" -f $key, $tripleCategories[$key])
    }
    Assert-True ($tripleCategories.Count -eq 3 -and
        $tripleCategories['Alice: 第一行。'] -eq 'Megaphone' -and
        $tripleCategories['Alice: 中间行。'] -eq 'Guild' -and
        $tripleCategories['Alice: 最后行。'] -eq 'Megaphone') 'first/middle/last duplicate rows retain their own source through out-of-order async publication'
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
Assert-True ($chatSource.Contains('List<ChatVisualStyle> lineStyles = new List<ChatVisualStyle>();')) 'queue resolves styles once from parsed rows'
Assert-True ($chatSource.Contains('capture.StyleForRow(row)')) 'queue takes the resolved style by current row index'
Assert-True ($chatSource.Contains('RowIndex = rowIndex')) 'enqueued pending line carries its current row index'
Assert-True ($chatSource.Contains('chatContinuity.ResolveIndices(priorFrame, lines')) 'continuity is applied to current row indices'
Assert-True ($chatSource.Contains('AddSourceChangedChatLineIndices(priorFrame, priorStyles')) 'same-text source changes are fed by current row index'
Assert-True ($chatSource.Contains('PublishPreparedChatLine') -and
    $chatSource.Contains('ChatVisualStyle style = prepared.Style')) 'publication uses the carried prepared style'
Assert-True ($chatSource.Contains('AlignChatRows') -and
    $chatSource.Contains('ChatRowPairScore')) 'previous/current chat rows use ordered alignment'
Assert-True (-not $chatSource.Contains('previousOccurrences')) 'no occurrence-from-start source-change map remains'
Assert-True (-not $chatSource.Contains('priorOccurrence')) 'queue no longer re-finds duplicate text by prior-frame occurrence count'
Assert-True (([regex]::Matches($chatSource, 'OnlineAiClient\.TranslateAsync\(').Count) -eq 2) 'AI translation request sites remain at the v4.0 count (2)'

Write-Host ("Chat megaphone pipeline regression: {0} checks passed; exact-vs-fuzzy identity, exact Alicf nickname regression, ambiguous/missing nickname Unknown, physical-row association, first/middle/last duplicate sources, current-row carry, retained-row scroll alignment, false retained-duplicate suppression, new same-text cross-channel delivery, cache identity, parse→queue→publish→history, out-of-order async publication, main/floating filters, detection cost and OCR call count covered." -f $script:checks)
