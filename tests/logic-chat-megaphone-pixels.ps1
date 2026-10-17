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
$evidenceType = $assembly.GetType('MapleOverlay.ChatColorEvidence', $true)
$sampleType = $assembly.GetType('MapleOverlay.ChatColorSample', $true)
$policyType = $assembly.GetType('MapleOverlay.ChatVisualStylePolicy', $true)
$styleType = $assembly.GetType('MapleOverlay.ChatVisualStyle', $true)
$categoryType = $assembly.GetType('MapleOverlay.ChatSourceCategory', $true)
$kindType = $assembly.GetType('MapleOverlay.ChatVisualKind', $true)
$analyze = $evidenceType.GetMethod('Analyze', $all)
$fromSample = $policyType.GetMethod('FromSample', $all)

$script:checks = 0
function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++
    Write-Host ("PASS: {0}" -f $message)
}
function Get-Field($value, [string]$name) {
    if ($null -eq $value) { throw "Get-Field($name): 值为空" }
    $field = $value.GetType().GetField($name, $script:bindingFlags)
    if ($null -ne $field) { return $field.GetValue($value) }
    $property = $value.GetType().GetProperty($name, $script:bindingFlags)
    if ($null -ne $property) { return $property.GetValue($value, $null) }
    throw "Get-Field($name): 类型 $($value.GetType().FullName) 没有该字段或属性"
}
function Convert-StyleInfo($style) {
    return @{
        Category = [string](Get-Field $style 'Category')
        Kind = [string](Get-Field $style 'Kind')
        HasBackground = [bool](Get-Field $style 'HasBackground')
    }
}
function Get-StyleCategory($style) {
    if ($style -is [hashtable]) { return [string]$style.Category }
    return [string](Get-Field $style 'Category')
}
function Get-StyleKind($style) {
    if ($style -is [hashtable]) { return [string]$style.Kind }
    return [string](Get-Field $style 'Kind')
}
function Get-StyleHasBackground($style) {
    if ($style -is [hashtable]) { return [bool]$style.HasBackground }
    return [bool](Get-Field $style 'HasBackground')
}
function Get-BandName($sample) { return [string]$sample.Band }

function Read-Pixels([System.Drawing.Bitmap]$bitmap) {
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
        return ,$pixels
    } finally { $bitmap.UnlockBits($data) }
}

function Get-SampleInfo($sample) {
    return @{
        Band = [string](Get-Field $sample 'Band')
        Coverage = [double](Get-Field $sample 'Coverage')
        RowCoverage = [double](Get-Field $sample 'RowCoverage')
        InkPixels = [int](Get-Field $sample 'InkPixels')
        InkMatchesBand = [bool](Get-Field $sample 'InkMatchesBand')
        IsBroadcastBand = [bool](Get-Field $sample 'IsBroadcastBand')
        Foreground = (Get-Field $sample 'Foreground')
        Background = (Get-Field $sample 'Background')
    }
}

function Get-Sample([string]$path, [int]$x, [int]$y, [int]$w, [int]$h) {
    $bitmap = [System.Drawing.Bitmap]::new($path)
    try {
        $pixels = Read-Pixels $bitmap
        $rect = [System.Drawing.Rectangle]::new($x, $y, $w, $h)
        $sample = $analyze.Invoke($null, [object[]]@($pixels, $bitmap.Width, $bitmap.Height,
            $rect, $null))
        return (Get-SampleInfo $sample)
    } finally { $bitmap.Dispose() }
}

function Get-Style($sample, [string]$text) {
    $style = $fromSample.Invoke($null, [object[]]@($text, $sample.Foreground,
        $sample.Background, $sample.IsBroadcastBand))
    return (Convert-StyleInfo $style)
}

function Test-FixtureLine([string]$file, [int]$x, [int]$y, [int]$w, [int]$h,
    [string]$text, [string]$family, [string]$kind, [string]$caseLabel) {
    $sample = Get-Sample $file $x $y $w $h
    $style = Get-Style $sample $text
    $band = Get-BandName $sample
    Assert-True ($band -eq $family) ("{0}: band {1} (expected {2})" -f $caseLabel, $band, $family)
    Assert-True ([bool]$sample.InkMatchesBand) ("{0}: dark stroke matches band" -f $caseLabel)
    Assert-True ((Get-StyleCategory $style) -eq 'Megaphone') ("{0}: category Megaphone" -f $caseLabel)
    Assert-True ((Get-StyleKind $style) -eq $kind) ("{0}: kind {1} (expected {2})" -f $caseLabel, (Get-StyleKind $style), $kind)
    Assert-True (Get-StyleHasBackground $style) ("{0}: coloured background retained" -f $caseLabel)
    Write-Host ("  evidence: band={0} cov={1:N3} row={2:N3} inkPixels={3}" -f
        $sample.Band, $sample.Coverage, $sample.RowCoverage, $sample.InkPixels)
}

function New-SyntheticPixels([System.Drawing.Color]$background, [System.Drawing.Color]$ink,
    [bool]$band) {
    $pixels = [int[]]::new(340 * 70)
    for ($i = 0; $i -lt $pixels.Length; $i++) { $pixels[$i] = $background.ToArgb() }
    if ($band) {
        for ($y = 18; $y -lt 40; $y++) {
            for ($x = 0; $x -lt 340; $x++) {
                $pixels[$y * 340 + $x] = [System.Drawing.Color]::FromArgb(205, 120, 175).ToArgb()
            }
        }
    }
    for ($left = 28; $left -lt 310; $left += 14) {
        for ($yy = 23; $yy -lt 32; $yy++) {
            for ($xx = $left; $xx -lt $left + 7; $xx++) {
                $pixels[$yy * 340 + $xx] = [System.Drawing.Color]::Black.ToArgb()
            }
        }
        for ($yy = 25; $yy -lt 30; $yy++) {
            for ($xx = $left + 2; $xx -lt $left + 5; $xx++) {
                $pixels[$yy * 340 + $xx] = $ink.ToArgb()
            }
        }
    }
    return ,$pixels
}

function Get-SyntheticSample([System.Drawing.Color]$background, [System.Drawing.Color]$ink,
    [bool]$band) {
    $pixels = New-SyntheticPixels $background $ink $band
    $sample = $analyze.Invoke($null, [object[]]@($pixels, 340, 70,
        [System.Drawing.Rectangle]::new(20, 20, 300, 16), $null))
    return (Get-SampleInfo $sample)
}

function Save-Scaled([string]$source, [string]$destination, [double]$scale) {
    $image = [System.Drawing.Image]::FromFile($source)
    try {
        $width = [int][Math]::Round($image.Width * $scale)
        $height = [int][Math]::Round($image.Height * $scale)
        $bitmap = [System.Drawing.Bitmap]::new($width, $height,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.InterpolationMode =
                    [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.CompositingMode =
                    [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.DrawImage($image, 0, 0, $width, $height)
            } finally { $graphics.Dispose() }
            $bitmap.Save($destination, [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
    } finally { $image.Dispose() }
}

function Save-Shifted([string]$source, [string]$destination, [int]$red, [int]$green, [int]$blue) {
    $bitmap = [System.Drawing.Bitmap]::new($source)
    try {
        for ($y = 0; $y -lt $bitmap.Height; $y++) {
            for ($x = 0; $x -lt $bitmap.Width; $x++) {
                $color = $bitmap.GetPixel($x, $y)
                $r = [Math]::Max(0, [Math]::Min(255, $color.R + $red))
                $g = [Math]::Max(0, [Math]::Min(255, $color.G + $green))
                $b = [Math]::Max(0, [Math]::Min(255, $color.B + $blue))
                $bitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($color.A, $r, $g, $b))
            }
        }
        $bitmap.Save($destination, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
}

function Save-Crop([string]$source, [string]$destination, [int]$x, [int]$y, [int]$w, [int]$h) {
    $image = [System.Drawing.Image]::FromFile($source)
    try {
        $bitmap = [System.Drawing.Bitmap]::new($w, $h,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.DrawImage($image, [System.Drawing.Rectangle]::new(0, 0, $w, $h),
                    [System.Drawing.Rectangle]::new($x, $y, $w, $h),
                    [System.Drawing.GraphicsUnit]::Pixel)
            } finally { $graphics.Dispose() }
            $bitmap.Save($destination, [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
    } finally { $image.Dispose() }
}

$pink = Join-Path $repoRoot 'tests/fixtures/chat-pink-super-megaphone-video.png'
$blue = Join-Path $repoRoot 'tests/fixtures/chat-blue-megaphone-video.png'
$pinkRects = @(
    @{ x = 16; y = 13; w = 820; h = 28; text = 'Toph: Clicking NPC shows no dialogue window disables all controls.'; label = 'pink row 1 (top)' },
    @{ x = 16; y = 43; w = 550; h = 28; text = 'Toph: Anyone know a fix?'; label = 'pink row 2 (middle)' },
    @{ x = 16; y = 73; w = 920; h = 28; text = 'BeelsFad: T> Int Bamboo Hat for Dex Bamboo Hat'; label = 'pink row 3 (last)' },
    @{ x = 16; y = 103; w = 900; h = 29; text = 'Kena: Trading Dex Bamboo Hat for Int Bamboo Hat'; label = 'pink row 4 (adjacent blue)' }
)
$blueRects = @(
    @{ x = 16; y = 103; w = 650; h = 29; text = 'Joey: where do I bring magic box from event?'; label = 'blue row' }
)

# A. Original fixture pixels, including first/middle/last pink rows and the
# adjacent blue row in the same frame.
foreach ($case in $pinkRects) {
    $family = if ($case.label -like '*blue*') { 'BlueBroadcast' } else { 'PinkBroadcast' }
    $kind = if ($family -eq 'BlueBroadcast') { 'Megaphone' } else { 'SuperMegaphone' }
    Test-FixtureLine $pink $case.x $case.y $case.w $case.h $case.text $family $kind $case.label
}
foreach ($case in $blueRects) {
    Test-FixtureLine $blue $case.x $case.y $case.w $case.h $case.text 'BlueBroadcast' 'Megaphone' $case.label
}

$temp = Join-Path $repoRoot ('.bench/chat-megaphone-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $temp | Out-Null
try {
    # B. Reasonable scaling and slight colour offsets.
    foreach ($scale in @(0.75, 1.25)) {
        $tag = '{0:000}' -f [int]($scale * 100)
        $pinkScaled = Join-Path $temp "pink-$tag.png"
        $blueScaled = Join-Path $temp "blue-$tag.png"
        Save-Scaled $pink $pinkScaled $scale
        Save-Scaled $blue $blueScaled $scale
        foreach ($case in $pinkRects) {
            $family = if ($case.label -like '*blue*') { 'BlueBroadcast' } else { 'PinkBroadcast' }
            $kind = if ($family -eq 'BlueBroadcast') { 'Megaphone' } else { 'SuperMegaphone' }
            $sx = [int][Math]::Floor($case.x * $scale)
            $sy = [int][Math]::Floor($case.y * $scale)
            $sw = [int][Math]::Ceiling($case.w * $scale)
            $sh = [int][Math]::Ceiling($case.h * $scale)
            $label = "{0} at {1:P0}" -f $case.label, $scale
            Test-FixtureLine $pinkScaled $sx $sy $sw $sh $case.text $family $kind $label | Out-Null
        }
        foreach ($case in $blueRects) {
            $sx = [int][Math]::Floor($case.x * $scale)
            $sy = [int][Math]::Floor($case.y * $scale)
            $sw = [int][Math]::Ceiling($case.w * $scale)
            $sh = [int][Math]::Ceiling($case.h * $scale)
            $label = "{0} at {1:P0}" -f $case.label, $scale
            Test-FixtureLine $blueScaled $sx $sy $sw $sh $case.text 'BlueBroadcast' 'Megaphone' $label | Out-Null
        }
    }
    $pinkWarm = Join-Path $temp 'pink-warm.png'
    $blueTint = Join-Path $temp 'blue-tint.png'
    Save-Shifted $pink $pinkWarm 8 2 -3
    Save-Shifted $blue $blueTint -6 2 8
    foreach ($case in $pinkRects) {
        $family = if ($case.label -like '*blue*') { 'BlueBroadcast' } else { 'PinkBroadcast' }
        $kind = if ($family -eq 'BlueBroadcast') { 'Megaphone' } else { 'SuperMegaphone' }
        Test-FixtureLine $pinkWarm $case.x $case.y $case.w $case.h $case.text $family $kind
            ("{0} with warm colour shift" -f $case.label) | Out-Null
    }
    foreach ($case in $blueRects) {
        Test-FixtureLine $blueTint $case.x $case.y $case.w $case.h $case.text 'BlueBroadcast' 'Megaphone'
            ("{0} with blue colour shift" -f $case.label) | Out-Null
    }

    # C. Partial cropping (top/bottom half of a row, and a row at the image edge).
    $cropCases = @(
        @{ file = $pink; x = 16; y = 43; w = 550; h = 12; text = 'Toph: Anyone know a fix?'; label = 'pink middle top half'; family = 'PinkBroadcast'; kind = 'SuperMegaphone' },
        @{ file = $pink; x = 16; y = 59; w = 550; h = 12; text = 'Toph: Anyone know a fix?'; label = 'pink middle bottom half'; family = 'PinkBroadcast'; kind = 'SuperMegaphone' },
        @{ file = $pink; x = 16; y = 73; w = 920; h = 10; text = 'BeelsFad: T> Int Bamboo Hat for Dex Bamboo Hat'; label = 'pink last top crop'; family = 'PinkBroadcast'; kind = 'SuperMegaphone' },
        @{ file = $blue; x = 16; y = 103; w = 650; h = 10; text = 'Joey: where do I bring magic box from event?'; label = 'blue top crop'; family = 'BlueBroadcast'; kind = 'Megaphone' },
        @{ file = $blue; x = 16; y = 122; w = 650; h = 10; text = 'Joey: where do I bring magic box from event?'; label = 'blue bottom crop'; family = 'BlueBroadcast'; kind = 'Megaphone' }
    )
    foreach ($case in $cropCases) {
        Test-FixtureLine $case.file $case.x $case.y $case.w $case.h $case.text $case.family $case.kind $case.label | Out-Null
    }
    $pinkTop = Join-Path $temp 'pink-top-crop.png'
    Save-Crop $pink $pinkTop 0 13 1080 119
    Test-FixtureLine $pinkTop 16 0 820 28 'Toph: Clicking NPC shows no dialogue window disables all controls.' 'PinkBroadcast' 'SuperMegaphone' 'whole image cropped at first band row'
    $blueEdge = Join-Path $temp 'blue-edge-crop.png'
    Save-Crop $blue $blueEdge 0 103 1080 29
    Test-FixtureLine $blueEdge 16 0 650 29 'Joey: where do I bring magic box from event?' 'BlueBroadcast' 'Megaphone' 'blue row cropped at both image edges'
} finally {
    Remove-Item -LiteralPath $temp -Recurse -Force
}

# D. Ordinary chat over a coloured scene must not be promoted merely because
# the scene is pink or blue.
foreach ($background in @([System.Drawing.Color]::FromArgb(215, 165, 182),
        [System.Drawing.Color]::FromArgb(140, 168, 180))) {
    $sample = Get-SyntheticSample $background ([System.Drawing.Color]::White) $false
    $style = Get-Style $sample 'Alice: hello'
    Assert-True (-not [bool]$sample.IsBroadcastBand) ("normal chat over {0},{1},{2}: no broadcast band" -f $background.R, $background.G, $background.B)
    Assert-True ((Get-StyleCategory $style) -eq 'Normal') ("normal chat over {0},{1},{2}: stays Normal" -f $background.R, $background.G, $background.B)
}

# E. Six personal channels over a uniformly coloured game scene keep their glyph source.
$channelCases = @(
    @{ category = 'Normal'; color = [System.Drawing.Color]::FromArgb(255, 255, 255) },
    @{ category = 'Buddy'; color = [System.Drawing.Color]::FromArgb(255, 204, 102) },
    @{ category = 'Party'; color = [System.Drawing.Color]::FromArgb(255, 153, 255) },
    @{ category = 'Guild'; color = [System.Drawing.Color]::FromArgb(153, 153, 255) },
    @{ category = 'Alliance'; color = [System.Drawing.Color]::FromArgb(153, 255, 153) },
    @{ category = 'Whisper'; color = [System.Drawing.Color]::FromArgb(0, 255, 0) }
)
foreach ($case in $channelCases) {
    $sample = Get-SyntheticSample ([System.Drawing.Color]::FromArgb(85, 145, 200)) $case.color $false
    $style = Get-Style $sample 'Alice: hello'
    Assert-True ((Get-StyleCategory $style) -eq $case.category) ("{0} channel glyph remains isolated over a coloured scene" -f $case.category)
}

# F. System and notice remain text/colour isolated and never become megaphones.
$notice = $fromSample.Invoke($null, [object[]]@('[Notice] Channel 3 is now open.',
    [System.Drawing.Color]::Empty, [System.Drawing.Color]::Empty, $false))
Assert-True ((Get-StyleCategory $notice) -eq 'Notice') 'notice prefix remains Notice'
$system = $fromSample.Invoke($null, [object[]]@('System: warning',
    [System.Drawing.Color]::FromArgb(225, 115, 145), [System.Drawing.Color]::Empty, $false))
Assert-True ((Get-StyleCategory $system) -eq 'System') 'system prefix/colour remains System'
Assert-True (-not (Get-StyleHasBackground $system)) 'system colour is not converted into a broadcast background'
$ordinary = $fromSample.Invoke($null, [object[]]@('Alice: this system uses a megaphone',
    [System.Drawing.Color]::White, [System.Drawing.Color]::Empty, $false))
Assert-True ((Get-StyleCategory $ordinary) -eq 'Normal') 'ordinary words do not manufacture System/Megaphone'

# G. Unknown / no-evidence rows stay out of personal channels; real guild and
# explicit personal prefixes still identify their source.
$unknownStroke = $fromSample.Invoke($null, [object[]]@('Alice: hello',
    [System.Drawing.Color]::FromArgb(86, 121, 151), [System.Drawing.Color]::FromArgb(140, 168, 180), $false))
Assert-True ((Get-StyleCategory $unknownStroke) -eq 'Unknown') 'dark blue stroke on a blue scene without band evidence is Unknown'
$unmatched = $fromSample.Invoke($null, [object[]]@('Alice: hello',
    [System.Drawing.Color]::FromArgb(120, 120, 120), [System.Drawing.Color]::Empty, $false))
Assert-True ((Get-StyleCategory $unmatched) -eq 'Unknown') 'unmatched glyph colour is Unknown rather than Normal/System/Guild'
$unknownText = $fromSample.Invoke($null, [object[]]@('loose OCR fragment',
    [System.Drawing.Color]::Empty, [System.Drawing.Color]::Empty, $false))
Assert-True ((Get-StyleCategory $unknownText) -eq 'Unknown') 'unassociated fragment without visual evidence is Unknown'
$guild = $fromSample.Invoke($null, [object[]]@('Alice: hello',
    [System.Drawing.Color]::FromArgb(153, 153, 255), [System.Drawing.Color]::Empty, $false))
Assert-True ((Get-StyleCategory $guild) -eq 'Guild') 'valid guild glyph colour is still Guild'
$explicitGuild = $fromSample.Invoke($null, [object[]]@('[Guild] Alice: hello',
    [System.Drawing.Color]::Empty, [System.Drawing.Color]::Empty, $false))
Assert-True ((Get-StyleCategory $explicitGuild) -eq 'Guild') 'explicit guild prefix is still Guild'

# H. A synthetic measured pink band with a boundary still classifies as broadcast;
# an explicit personal prefix on the same band is never promoted.
$bandSample = Get-SyntheticSample ([System.Drawing.Color]::FromArgb(25, 28, 35)) ([System.Drawing.Color]::White) $true
$bandStyle = Get-Style $bandSample 'Alice: selling'
Assert-True ([bool]$bandSample.IsBroadcastBand) 'synthetic measured pink band is broadcast evidence'
Assert-True ((Get-StyleCategory $bandStyle) -eq 'Megaphone') 'synthetic measured pink band classifies as Megaphone'
$partyBand = $fromSample.Invoke($null, [object[]]@('[Party] Alice: hello',
    [System.Drawing.Color]::White, [System.Drawing.Color]::FromArgb(205, 120, 175), $true))
Assert-True ((Get-StyleCategory $partyBand) -eq 'Party') 'explicit personal prefix is not promoted by a pink band'

# I. Translucent texture through the band: a 25% scene blend with real dark
# strokes still counts, while the same scene without the band does not.
function New-TexturedPixels([bool]$withBand, [bool]$whiteInk) {
    $pixels = [int[]]::new(340 * 70)
    $bandColor = [System.Drawing.Color]::FromArgb(217, 165, 182)
    $inkColor = if ($whiteInk) { [System.Drawing.Color]::White } else { [System.Drawing.Color]::FromArgb(150, 72, 106) }
    for ($y = 0; $y -lt 70; $y++) {
        for ($x = 0; $x -lt 340; $x++) {
            $sceneR = 60 + [int](40.0 * $x / 340) + [int](15.0 * $y / 70)
            $sceneG = 90 + [int](30.0 * $y / 70)
            $sceneB = 55 + [int](35.0 * $x / 340)
            $r = $sceneR; $g = $sceneG; $b = $sceneB
            if ($withBand -and $y -ge 18 -and $y -lt 40) {
                $r = [int](($bandColor.R * 0.72) + ($sceneR * 0.28))
                $g = [int](($bandColor.G * 0.72) + ($sceneG * 0.28))
                $b = [int](($bandColor.B * 0.72) + ($sceneB * 0.28))
            }
            $pixels[$y * 340 + $x] = [System.Drawing.Color]::FromArgb($r, $g, $b).ToArgb()
        }
    }
    for ($left = 28; $left -lt 310; $left += 14) {
        for ($yy = 23; $yy -lt 32; $yy++) {
            for ($xx = $left; $xx -lt $left + 7; $xx++) {
                $pixels[$yy * 340 + $xx] = [System.Drawing.Color]::Black.ToArgb()
            }
        }
        for ($yy = 25; $yy -lt 30; $yy++) {
            for ($xx = $left + 2; $xx -lt $left + 5; $xx++) {
                $pixels[$yy * 340 + $xx] = $inkColor.ToArgb()
            }
        }
    }
    return ,$pixels
}
$textureBand = $analyze.Invoke($null, [object[]]@((New-TexturedPixels $true $false), 340, 70,
    [System.Drawing.Rectangle]::new(20, 20, 300, 16), $null))
$textureInfo = Get-SampleInfo $textureBand
$textureStyle = Get-Style $textureInfo 'Alice: selling'
Write-Host ("  texture evidence: band={0} cov={1:N3} row={2:N3} inkMatch={3} inkPixels={4}" -f
    $textureInfo.Band, $textureInfo.Coverage, $textureInfo.RowCoverage,
    $textureInfo.InkMatchesBand, $textureInfo.InkPixels)
Assert-True ($textureInfo.Band -eq 'PinkBroadcast') 'translucent textured pink band is still detected'
Assert-True ((Get-StyleCategory $textureStyle) -eq 'Megaphone') 'translucent textured pink band classifies as Megaphone'
$textureScene = $analyze.Invoke($null, [object[]]@((New-TexturedPixels $false $true), 340, 70,
    [System.Drawing.Rectangle]::new(20, 20, 300, 16), $null))
$textureSceneInfo = Get-SampleInfo $textureScene
$textureSceneStyle = Get-Style $textureSceneInfo 'Alice: hello'
Assert-True (-not $textureSceneInfo.IsBroadcastBand) 'the same textured scene without a band is not broadcast'
Assert-True ((Get-StyleCategory $textureSceneStyle) -ne 'Megaphone') 'textured scene alone does not promote ordinary chat'

Write-Output ("Chat megaphone pixel regression: {0} checks passed; fixtures, scale/tint, partial crops, adjacent pink/blue rows, six personal channels, system/notice, ordinary text over coloured/textured scenes and Unknown isolation covered." -f $script:checks)
