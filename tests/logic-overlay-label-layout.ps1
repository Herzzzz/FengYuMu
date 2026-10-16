$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "找不到程序集：$exe" }

# The repository lives under a path with non-ASCII directory names, where the CLR rejects the
# assembly as a "remote" location. The shared loader probes LoadFile first and only then falls
# back to UnsafeLoadFrom; no assertion below is relaxed by it.
if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
}
$assembly = Load-TestAssembly $exe
$all = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

# ---------------------------------------------------------------------------
# v4.0 label geometry. The defects these assertions pin down were all visible on the quest
# panel: a long Chinese body drawn as one clipped line, a rectangle that had already grown
# larger than the panel and could never be capped, one union erase that painted a light slab
# over the gaps between the original rows, and mixed source ink turning the translation purple.
# Everything here is behavioural: measurements, layout decisions and real painted pixels.
# ---------------------------------------------------------------------------
$safetyType = $assembly.GetType('MapleOverlay.OverlayPaintSafety', $true)
if ($null -eq $safetyType) { throw '缺少 OverlayPaintSafety' }
function Get-Safety([string]$name) {
    $method = $safetyType.GetMethod($name, $all)
    if ($null -eq $method) { throw "缺少几何/墨色判据：$name" }
    return $method
}
$isSafeGeometry = Get-Safety 'IsSafeSeamlessGeometry'
$isUsableInk = Get-Safety 'IsUsableInk'
$isInsideCapture = Get-Safety 'IsInsideCapture'
$computeCeiling = Get-Safety 'ComputeLabelCeiling'
$computeLayout = Get-Safety 'TryComputeLabelLayout'
$buildErase = Get-Safety 'BuildEraseRects'

# PowerShell stores every element of an object[] as a PSObject wrapper, and MethodInfo.Invoke
# rejects those wrappers for reference types (brushes, pens, lists). Unwrap before invoking so
# the reflection call sees the real CLR values.
function Invoke-Method($method, $target, [object[]]$arguments) {
    if ($target -is [System.Management.Automation.PSObject]) {
        $target = $target.PSObject.BaseObject
    }
    for ($i = 0; $i -lt $arguments.Length; $i++) {
        if ($arguments[$i] -is [System.Management.Automation.PSObject]) {
            # .PSObject.BaseObject is the correct accessor here: a direct .BaseObject member
            # lookup on a wrapped GDI+ object resolves to $null.
            $arguments[$i] = $arguments[$i].PSObject.BaseObject
        }
    }
    return $method.Invoke($target, $arguments)
}

function Get-Ceiling([System.Drawing.RectangleF]$bounds, [System.Drawing.RectangleF]$block,
    [bool]$hasBlock, [bool]$longProse, [bool]$allowGrowth = $true, $safeArea = $null,
    [single]$maximum = 720.0) {
    if ($null -eq $safeArea) { $safeArea = New-Object System.Drawing.RectangleF(0, 0, 0, 0) }
    return [single](Invoke-Method $computeCeiling $null @($bounds, $block, $hasBlock, $longProse,
        $allowGrowth, $safeArea, $maximum))
}

function Get-Layout([System.Drawing.RectangleF]$bounds, [System.Drawing.RectangleF]$block,
    [bool]$hasBlock, [bool]$longProse, [System.Drawing.SizeF]$measured,
    [System.Drawing.SizeF]$wrapped, [bool]$allowGrowth = $true, $safeArea = $null,
    [single]$maximum = 720.0) {
    if ($null -eq $safeArea) { $safeArea = New-Object System.Drawing.RectangleF(0, 0, 0, 0) }
    $arguments = New-Object 'object[]' 13
    $arguments[0] = $bounds
    $arguments[1] = $block
    $arguments[2] = $hasBlock
    $arguments[3] = $longProse
    $arguments[4] = $allowGrowth
    $arguments[5] = $safeArea
    $arguments[6] = $maximum
    $arguments[7] = $measured
    $arguments[8] = $wrapped
    $ok = [bool](Invoke-Method $computeLayout $null $arguments)
    # Named order: 0 = accepted, 1 = wrapped draw path, 2 = layout rectangle, 3 = force card,
    # 4 = the fit contract says the measured text really fits.
    return @($ok, $arguments[10], $arguments[9], $arguments[11], $arguments[12])
}

# A wrapped body must be laid out inside the block it replaces, through the bounded rectangle
# draw path. Before the repair the ceiling was Math.Max(bounds.Width, 360), so this oversized
# rectangle passed straight through and the text was drawn as a single spilling line.
$block = New-Object System.Drawing.RectangleF(100, 200, 380, 120)
$oversized = New-Object System.Drawing.RectangleF(100, 200, 2400, 30)
$fits = Get-Layout $oversized $block $true $true (New-Object System.Drawing.SizeF(2000, 22)) `
    (New-Object System.Drawing.SizeF(372, 90))
if (-not $fits[0]) { throw '合法的任务正文矩形被几何判据拒绝' }
# Guard the harness itself: a wrong out-parameter index would make the checks below compare
# $null and silently pass, which is exactly how a layout regression could slip through.
if ($fits[1] -isnot [bool]) { throw '布局判据没有返回换行标志（out 参数顺序错误）' }
if ($fits[2] -isnot [System.Drawing.RectangleF]) { throw '布局判据没有返回布局矩形（out 参数顺序错误）' }
if ($fits[3] -isnot [bool]) { throw '布局判据没有返回卡片回退标志（out 参数顺序错误）' }
if (-not $fits[1]) { throw '长正文没有走有界矩形换行绘制路径' }
if ([single]$fits[2].Width -gt 380.5) { throw ("长正文宽度越出测量块：" + $fits[2].Width) }
if ([single]$fits[2].Height -gt 120.5) { throw ("长正文高度越出测量块：" + $fits[2].Height) }
if ($fits[3]) { throw '能装下的长正文不应强制回退卡片' }
# Guard the guard: prove this case actually discriminates the old rule. The previous ceiling was
# Math.Max(bounds.Width, 360), which for this rectangle is 2400 - wider than the whole panel -
# so the assertions above could not have passed before the repair.
$oldCeiling = [Math]::Max([single]$oversized.Width, 360.0)
if ($oldCeiling -le 380.5) { throw '测试用例无法区分修复前后的上限行为' }
if ((Get-Ceiling $oversized $block $true $true) -gt 380.5) { throw '新上限没有被约束到测量块' }

# Boundary rule: prose may never use the whole game client as its expansion area. Given the block
# as the safe area (what the paint pass uses when no panel boundary is known), a taller translation
# does not expand past it and reports that it does not fit.
$blockBoundary = Get-Layout $oversized $block $true $true (New-Object System.Drawing.SizeF(2000, 22)) `
    (New-Object System.Drawing.SizeF(372, 400)) $true $block
if ([single]$blockBoundary[2].Height -gt 120.5) {
    throw ("长正文越出块边界（被当成整块游戏客户区）：" + $blockBoundary[2].Height)
}
if ($blockBoundary[4]) { throw '超出块边界的正文被判定为可完整显示' }
if (-not $blockBoundary[3]) { throw '超出块边界的正文没有进入显式溢出/卡片状态' }

# A detected panel boundary may be larger than the source block, and prose may use it - but still
# never the client, and never beyond the conservative ceiling.
$panel = New-Object System.Drawing.RectangleF(100, 200, 380, 400)
$panelLayout = Get-Layout $oversized $block $true $true (New-Object System.Drawing.SizeF(2000, 22)) `
    (New-Object System.Drawing.SizeF(372, 300)) $true $panel
if (-not $panelLayout[4]) { throw '在面板边界内放得下的长正文被误判为溢出' }
if ([single]$panelLayout[2].Height -gt 400.5) {
    throw ("长正文越出面板边界：" + $panelLayout[2].Height)
}

# Defect B: an already oversized rectangle with short text. The old ceiling could never cap it.
$wideOnly = New-Object System.Drawing.RectangleF(0, 0, 2400, 30)
$capped = Get-Layout $wideOnly $wideOnly $false $false (New-Object System.Drawing.SizeF(60, 20)) `
    (New-Object System.Drawing.SizeF(60, 20))
if ([single]$capped[2].Width -gt 360.5) { throw ("无测量块时未被上限截断：" + $capped[2].Width) }
if ((Get-Ceiling $wideOnly $wideOnly $false $false) -gt 360.5) {
    throw '上限本身没有被限制为最大值'
}

# Short labels keep their established behaviour: they still grow to fit the translation, but the
# growth stays inside the ceiling instead of being unbounded.
$narrow = New-Object System.Drawing.RectangleF(100, 200, 34, 18)
$short = Get-Layout $narrow $narrow $true $false (New-Object System.Drawing.SizeF(70, 20)) `
    (New-Object System.Drawing.SizeF(70, 20))
if ($short[1]) { throw '短标签被强制换行，破坏了原有短标签行为' }
if ([single]$short[2].Width -lt 78.0) { throw ("短标签没有为译文增长：" + $short[2].Width) }
if ([single]$short[2].Width -gt 114.5) { throw ("短标签增长越过了上限：" + $short[2].Width) }

# A seamless short label may not grow at all: allowGrowth is false for it, so it stays inside the
# measured source box instead of reaching over a neighbouring counter or button.
$seamlessShort = Get-Layout $narrow $narrow $true $false (New-Object System.Drawing.SizeF(70, 20)) `
    (New-Object System.Drawing.SizeF(70, 20)) $false
if ([single]$seamlessShort[2].Width -gt 34.5) {
    throw ("无缝短标签越出了源区域：" + $seamlessShort[2].Width)
}

# Client/DPI bound: a bogus 2400px source block may not size a label across the desktop. The
# previous rule returned 1440 for exactly this input.
$bogusBlock = New-Object System.Drawing.RectangleF(0, 0, 2400, 200)
$client = New-Object System.Drawing.RectangleF(0, 0, 1920, 1200)
$bogus = Get-Layout (New-Object System.Drawing.RectangleF(0, 0, 2400, 30)) $bogusBlock $true $true `
    (New-Object System.Drawing.SizeF(2000, 22)) (New-Object System.Drawing.SizeF(2000, 300)) $true $client
if ([single]$bogus[2].Width -gt 720.5) {
    throw ("2400px 源块未被客户区/保守上限截断：" + $bogus[2].Width)
}
if ((Get-Ceiling (New-Object System.Drawing.RectangleF(0, 0, 2400, 30)) $bogusBlock $true $true `
    $true $client) -gt 720.5) {
    throw '保守上限没有被客户区宽度约束'
}

# ---------------------------------------------------------------------------
# F + requirement 5: a high-confidence match is not geometry safety.
# ---------------------------------------------------------------------------
function Check-Safe([System.Drawing.RectangleF]$layout, [System.Drawing.RectangleF]$source,
    [int]$width, [int]$height) {
    return [bool](Invoke-Method $isSafeGeometry $null @($layout, $source, $width, $height))
}
$goodLayout = New-Object System.Drawing.RectangleF(100, 100, 240, 40)
if (-not (Check-Safe $goodLayout (New-Object System.Drawing.RectangleF(102, 101, 236, 36)) 1920 1200)) {
    throw '正常的任务正文矩形被判为不安全几何'
}
if (Check-Safe (New-Object System.Drawing.RectangleF(0, 0, 1800, 60)) `
    (New-Object System.Drawing.RectangleF(0, 0, 1800, 60)) 1920 1200) {
    throw '跨屏大小的矩形未被几何判据拒绝'
}
if (Check-Safe (New-Object System.Drawing.RectangleF([single]::NaN, 0, 200, 40)) `
    (New-Object System.Drawing.RectangleF(100, 100, 200, 40)) 1920 1200) {
    throw '非有限坐标未被拒绝'
}
if (Check-Safe $goodLayout (New-Object System.Drawing.RectangleF(100, 100, 2, 40)) 1920 1200) {
    throw '退化/空源区域未被拒绝'
}
if (Check-Safe $goodLayout (New-Object System.Drawing.RectangleF(900, 900, 60, 20)) 1920 1200) {
    throw '对齐不良的擦除区未被拒绝'
}
if (Check-Safe $goodLayout (New-Object System.Drawing.RectangleF(100, 100, 200, 40)) 8 8) {
    throw '异常小的捕获帧未被拒绝'
}
$offFrame = New-Object System.Drawing.RectangleF(1900, 100, 60, 20)
$inFrame = New-Object System.Drawing.RectangleF(100, 100, 60, 20)
if ([bool](Invoke-Method $isInsideCapture $null @($offFrame, 1920, 1200))) {
    throw '越出捕获帧的区域未被拒绝'
}
if (-not [bool](Invoke-Method $isInsideCapture $null @($inFrame, 1920, 1200))) {
    throw '帧内区域被误判为越界'
}

# ---------------------------------------------------------------------------
# E + requirement 4: mixed source ink must never become magenta/purple prose ink.
# ---------------------------------------------------------------------------
function Check-Ink([System.Drawing.Color]$ink, [System.Drawing.Color]$background) {
    return [bool](Invoke-Method $isUsableInk $null @($ink, $background))
}
$parchment = [System.Drawing.Color]::FromArgb(255, 236, 232, 214)
if (-not (Check-Ink ([System.Drawing.Color]::FromArgb(255, 18, 18, 22)) $parchment)) {
    throw '浅色底上的深色墨被误判为不可用'
}
if (Check-Ink ([System.Drawing.Color]::FromArgb(255, 255, 0, 255)) $parchment) {
    throw '洋红（透明键）被当作可用墨色'
}
if (Check-Ink ([System.Drawing.Color]::FromArgb(255, 170, 60, 190)) $parchment) {
    throw '黑蓝红混合出的紫色被当作可用墨色'
}
if (Check-Ink ([System.Drawing.Color]::FromArgb(255, 150, 150, 150)) `
    ([System.Drawing.Color]::FromArgb(255, 160, 160, 160))) {
    throw '与背景无对比度的墨色被当作可用'
}
if (Check-Ink ([System.Drawing.Color]::Empty) $parchment) { throw '空墨色被当作可用' }

# ---------------------------------------------------------------------------
# D + requirement 3: each original OCR row is erased on its own, not one big union slab.
# ---------------------------------------------------------------------------
$rows = New-Object 'System.Collections.Generic.List[System.Drawing.RectangleF]'
$rows.Add((New-Object System.Drawing.RectangleF(100, 200, 300, 18)))
$rows.Add((New-Object System.Drawing.RectangleF(100, 222, 300, 18)))
$rows.Add((New-Object System.Drawing.RectangleF(100, 244, 120, 18)))
$union = New-Object System.Drawing.RectangleF(100, 200, 300, 62)
# Explicit object[] arguments: @($rows, ...) would enumerate the List and send its elements
# instead of the list itself.
function Invoke-Erase($lines, [System.Drawing.RectangleF]$sourceUnion, [single]$dx, [single]$dy) {
    $arguments = New-Object 'object[]' 4
    # A list that travelled through a PowerShell parameter arrives PSObject-wrapped, which the
    # reflection call rejects; cast it back to the CLR type first.
    $arguments[0] = $null
    if ($null -ne $lines) {
        $arguments[0] = [System.Collections.Generic.List[System.Drawing.RectangleF]]$lines
    }
    $arguments[1] = $sourceUnion
    $arguments[2] = $dx
    $arguments[3] = $dy
    return @(Invoke-Method $buildErase $null $arguments)
}
$eraseRects = Invoke-Erase $rows $union ([single]0) ([single]0)
if ($eraseRects.Count -ne 3) {
    throw ("多行正文没有逐行擦除，返回 " + $eraseRects.Count + " 块")
}
$covered = 0.0
foreach ($rect in $eraseRects) {
    if ([single]$rect.Width -le 0 -or [single]$rect.Height -le 0) { throw '擦除矩形退化' }
    $covered += [single]$rect.Width * [single]$rect.Height
}
if ($covered -ge [single]$union.Width * [single]$union.Height * 0.9) {
    throw '擦除面积接近整块并集，仍会把行间空白刷成色块'
}
$shiftedRects = Invoke-Erase $rows $union ([single]5) ([single](-3))
if ([single]$shiftedRects[0].X -ne 105.0 -or [single]$shiftedRects[0].Y -ne 197.0) {
    throw '擦除矩形没有随标签位移'
}
$unionOnly = Invoke-Erase $null $union ([single]0) ([single]0)
if ($unionOnly.Count -ne 1) { throw '没有逐行矩形时未回退为单块擦除' }

# ---------------------------------------------------------------------------
# C + requirement 3: the short trailing continuation of a wrapped body belongs to the block.
# ---------------------------------------------------------------------------
$overlayType = $assembly.GetType('MapleOverlay.OverlayForm', $true)
$continues = $overlayType.GetMethod('ContinuesDetailProse', $all)
if ($null -eq $continues) { throw '缺少正文续行判据 ContinuesDetailProse' }
$noWords = New-Object 'System.Collections.Generic.HashSet[string]'
# The vocabulary the Winston #10405 entry actually contains. "he looks..." is a real continuation
# because BOTH of its words belong to that body; a single shared word is not evidence.
$winstonWords = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($word in @('he', 'looks', 'winston', 'fossils', 'animal', 'plant', 'stumps', 'mushrooms',
    'careless', 'important', 'things', 'stolen', 'collecting', 'region', 'mountains')) {
    $winstonWords.Add($word) | Out-Null
}
$someWords = $winstonWords
function Check-Tail([string]$raw, [string]$normalized, $words, [int]$maximum, [string]$previousTail) {
    $arguments = New-Object 'object[]' 5
    $arguments[0] = $raw
    $arguments[1] = $normalized
    $arguments[2] = $null
    if ($null -ne $words) { $arguments[2] = [System.Collections.Generic.HashSet[string]]$words }
    $arguments[3] = $maximum
    $arguments[4] = $previousTail
    return [bool](Invoke-Method $continues $null $arguments)
}

# The exact line sequence of the Winston #10405 quest body from the reported screenshot. The body
# tail is the ordered text already selected; only "he looks..." continues it.
$winstonTail = "iA'inston, an archaeologist I ran into in the Rocky Mountains east of Perion, " +
    "has lost the fossils he's meticulously been collecting in the region. He asked me to bring " +
    "back 12 Animal Fossils and 12 Plant Fossils, which have been stolen by Green Mushrooms and " +
    "Axe Stumps. How could he lose such important things? He's more careless"
if (-not (Check-Tail 'he looks...' 'he looks' $winstonWords 24 $winstonTail)) {
    throw '截图中的 "he looks..." 短续行在 Winston 词表下未被纳入任务正文'
}
# Without the entry vocabulary and without a consecutive pair in the tail there is no ordered
# evidence at all, so the row must not be swallowed.
if (Check-Tail 'he looks...' 'he looks' $noWords 24 $winstonTail) {
    throw '无词表且无连续重叠的省略号行被误判为正文续行'
}
# Only one shared word must never be enough - these are the false positives the audit found.
$singleWordNegatives = @(
    @('show quest...', 'show quest'),
    @('view quest...', 'view quest'),
    @('quest info...', 'quest info'),
    @('find fossils...', 'find fossils'),
    @('accept quest...', 'accept quest'),
    @("citizen's r...", "citizen's r"),
    @('next level...', 'next level'),
    @('show quest...', 'show quest'),
    @('VIEW QUEST...', 'view quest'),
    @('Quest Info...', 'quest info')
)
foreach ($negative in $singleWordNegatives) {
    if (Check-Tail $negative[0] $negative[1] $someWords 24 $winstonTail) {
        throw ("单词重叠的面板/目标行被误判为正文续行：" + $negative[0])
    }
    if (Check-Tail $negative[0] $negative[1] $noWords 24 $winstonTail) {
        throw ("小写 OCR 变体被误判为正文续行：" + $negative[0])
    }
}
# The panel rows that sit around the body must never be swallowed by the tail rule.
$notBody = @(
    @('FORFEIT', 'forfeit'),
    @('Quest Helper (0/5)', 'quest helper 0 5'),
    @('Animal Fossil 5 / 12', 'animal fossil 5 12'),
    @("Winston's Fossil Dig-up", "winston's fossil dig-up"),
    @('Level 17+', 'level 17+'),
    @("Citizen's R...", "citizen's r"),
    @('Next Level...', 'next level'),
    @('Accept...', 'accept'),
    @("Winston's F...", "winston's f"),
    @('5 / 12', '5 12'),
    @('12/12', '12 12')
)
foreach ($control in $notBody) {
    if (Check-Tail $control[0] $control[1] $noWords 24 $winstonTail) {
        throw ("面板控件/标题行被误判为正文续行：" + $control[0])
    }
}
# A truncated tail with no ordered overlap against the selected body is not a continuation.
if (Check-Tail 'he looks...' 'he looks' $noWords 24 'FORFEIT Quest Helper') {
    throw '无正文重叠证据的省略号行被纳入'
}
if (Check-Tail 'and he keeps searching the whole mountain range forever...' `
    'and he keeps searching the whole mountain range forever...' $noWords 24 $winstonTail) {
    throw '超长行被当作短续行纳入'
}

# ---------------------------------------------------------------------------
# Real paint assertions. The label is painted onto an in-memory bitmap through the product's own
# PaintOverlayLabel, then the pixels are measured. These fail if the wrapped draw path, the ink
# choice or the block clipping regress - they are not source-string checks.
# ---------------------------------------------------------------------------
$labelType = $assembly.GetType('MapleOverlay.OverlayLabel', $true)
$modeType = $assembly.GetType('MapleOverlay.LabelRenderMode', $true)
$paint = $overlayType.GetMethod('PaintOverlayLabel', $all)
if ($null -eq $paint) { throw '缺少 PaintOverlayLabel' }
$fontField = $overlayType.GetField('overlayFont', $all)
if ($null -eq $fontField) { throw '缺少 overlayFont 字段' }

$overlay = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($overlayType)
# The same Bold production font the overlay uses, so layout assertions measure real glyph metrics.
$testFont = New-Object System.Drawing.Font('Microsoft YaHei UI', 12.0,
    [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$fontField.SetValue($overlay, $testFont)

function New-TestLabel([System.Drawing.RectangleF]$bounds, [System.Drawing.RectangleF]$source,
    [bool]$wrap, [string]$text) {
    $label = [Activator]::CreateInstance($labelType, $true)
    $labelType.GetField('Bounds', $all).SetValue($label, $bounds)
    $labelType.GetField('SourceBounds', $all).SetValue($label, $source)
    $labelType.GetField('HasSourceBounds', $all).SetValue($label, $true)
    $labelType.GetField('Wrap', $all).SetValue($label, $wrap)
    $labelType.GetField('Text', $all).SetValue($label, $text)
    return $label
}

function Invoke-PaintTest($label, $bitmap) {
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $background = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(238, 18, 18, 20))
    $border = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(220, 255, 190, 45), 1.0)
    $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    try {
        $bounds = $labelType.GetField('Bounds', $all).GetValue($label)
        $arguments = New-Object 'object[]' 7
        $arguments[0] = $graphics
        $arguments[1] = $label
        $arguments[2] = $label.Text
        $arguments[3] = $bounds
        $arguments[4] = $background
        $arguments[5] = $border
        $arguments[6] = $textBrush
        Invoke-Method $paint $overlay $arguments | Out-Null
    } finally {
        $graphics.Dispose()
        $background.Dispose()
        $border.Dispose()
        $textBrush.Dispose()
    }
}

# 1. Seamless long prose on a pale panel: wrapped inside the block, dark ink, no leakage.
$canvas = New-Object System.Drawing.Bitmap(560, 220, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$clear = [System.Drawing.Graphics]::FromImage($canvas)
$clear.Clear([System.Drawing.Color]::White)
$clear.Dispose()

$blockRect = New-Object System.Drawing.RectangleF(60, 40, 380, 120)
$bodyText = '温斯顿是一名在勇士部落东侧岩石山脉中活动的考古学家，他丢失了长期收集的动物化石和植物化石。'
$seamlessLabel = New-TestLabel $blockRect $blockRect $true $bodyText
$labelType.GetField('RenderMode', $all).SetValue($seamlessLabel, [Enum]::Parse($modeType, 'Seamless'))
$labelType.GetField('BackgroundColor', $all).SetValue($seamlessLabel, $parchment)
$sourceRows = New-Object 'System.Collections.Generic.List[System.Drawing.RectangleF]'
$sourceRows.Add((New-Object System.Drawing.RectangleF(60, 40, 380, 20)))
$sourceRows.Add((New-Object System.Drawing.RectangleF(60, 66, 380, 20)))
$sourceRows.Add((New-Object System.Drawing.RectangleF(60, 92, 380, 20)))
$labelType.GetField('SourceLineBounds', $all).SetValue($seamlessLabel, $sourceRows)
Invoke-PaintTest $seamlessLabel $canvas

$inkTop = [single]::MaxValue
$inkBottom = [single]::MinValue
$darkest = 255.0
$outsidePixels = 0
$purplePixels = 0
for ($y = 0; $y -lt $canvas.Height; $y++) {
    for ($x = 0; $x -lt $canvas.Width; $x++) {
        $pixel = $canvas.GetPixel($x, $y)
        $inside = ($x -ge 60 -and $x -lt 440 -and $y -ge 40 -and $y -lt 160)
        if (-not $inside -and ($pixel.R -ne 255 -or $pixel.G -ne 255 -or $pixel.B -ne 255)) {
            $outsidePixels++
        }
        if ($pixel.R -gt 150 -and $pixel.B -gt 150 -and $pixel.G -lt 110) { $purplePixels++ }
        $luminance = 0.299 * $pixel.R + 0.587 * $pixel.G + 0.114 * $pixel.B
        if ($luminance -lt 120) {
            if ($y -lt $inkTop) { $inkTop = $y }
            if ($y -gt $inkBottom) { $inkBottom = $y }
            if ($luminance -lt $darkest) { $darkest = $luminance }
        }
    }
}
if ($outsidePixels -gt 0) { throw ("无缝渲染越出测量块：" + $outsidePixels + " 个像素") }
if ($purplePixels -gt 0) { throw ("浅色面板上出现了洋红/紫色像素：" + $purplePixels) }
if ($darkest -gt 110.0) { throw ("浅色面板上的长正文没有使用深色可读墨：" + $darkest) }
if ($inkTop -eq [single]::MaxValue) { throw '无缝渲染没有画出任何文字' }
$inkHeight = $inkBottom - $inkTop
if ($inkHeight -lt ($testFont.Height * 1.6)) {
    throw ("长中文没有按有界矩形换行绘制：文字仅跨 " + $inkHeight + " 像素")
}

# 2. CardFallback may not grow past the measured block either - an oversized rectangle used to
#    become a panel-wide dark slab.
$canvas2 = New-Object System.Drawing.Bitmap(560, 220, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$clear2 = [System.Drawing.Graphics]::FromImage($canvas2)
$clear2.Clear([System.Drawing.Color]::White)
$clear2.Dispose()
$cardLabel = New-TestLabel (New-Object System.Drawing.RectangleF(60, 40, 2400, 30)) $blockRect $false '动物化石 5/12'
Invoke-PaintTest $cardLabel $canvas2
$cardOutside = 0
for ($y = 0; $y -lt $canvas2.Height; $y++) {
    for ($x = 0; $x -lt $canvas2.Width; $x++) {
        $pixel = $canvas2.GetPixel($x, $y)
        $inside = ($x -ge 60 -and $x -lt 440 -and $y -ge 40 -and $y -lt 160)
        if (-not $inside -and ($pixel.R -ne 255 -or $pixel.G -ne 255 -or $pixel.B -ne 255)) {
            $cardOutside++
        }
    }
}
if ($cardOutside -gt 0) { throw ("卡片越出测量块：" + $cardOutside + " 个像素") }

# 3. Shrink-to-fit. A Chinese body longer than the English block must be scaled down to stay
#    inside that block instead of being clipped at its bottom edge (the "so many lines reduced
#    to so little" defect).
$canvas3 = New-Object System.Drawing.Bitmap(560, 220, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$clear3 = [System.Drawing.Graphics]::FromImage($canvas3)
$clear3.Clear([System.Drawing.Color]::White)
$clear3.Dispose()
$shortBlock = New-Object System.Drawing.RectangleF(60, 40, 380, 100)
$longBody = ('温斯顿是一名在勇士部落东侧岩石山脉中活动的考古学家，他丢失了长期收集的动物化石和植物化石。' +
    '他拜托我找回被绿蘑菇和斧木妖拿走的化石，这么重要的东西怎么会弄丢，他比看起来还粗心。') * 2
$shrinkLabel = New-TestLabel $shortBlock $shortBlock $true $longBody
$labelType.GetField('RenderMode', $all).SetValue($shrinkLabel, [Enum]::Parse($modeType, 'Seamless'))
$labelType.GetField('BackgroundColor', $all).SetValue($shrinkLabel, $parchment)
$shrinkRows = New-Object 'System.Collections.Generic.List[System.Drawing.RectangleF]'
$shrinkRows.Add((New-Object System.Drawing.RectangleF(60, 40, 380, 20)))
$shrinkRows.Add((New-Object System.Drawing.RectangleF(60, 66, 380, 20)))
$labelType.GetField('SourceLineBounds', $all).SetValue($shrinkLabel, $shrinkRows)
Invoke-PaintTest $shrinkLabel $canvas3
$shrinkOutside = 0
$lastInkRow = -1
for ($y = 0; $y -lt $canvas3.Height; $y++) {
    for ($x = 0; $x -lt $canvas3.Width; $x++) {
        $pixel = $canvas3.GetPixel($x, $y)
        $insideX = ($x -ge 60 -and $x -lt 440)
        if (-not $insideX -and ($pixel.R -ne 255 -or $pixel.G -ne 255 -or $pixel.B -ne 255)) {
            $shrinkOutside++
        }
        if ($insideX -and (0.299 * $pixel.R + 0.587 * $pixel.G + 0.114 * $pixel.B) -lt 120) {
            $lastInkRow = $y
        }
    }
}
if ($shrinkOutside -gt 0) { throw ("长正文横向越出测量块：" + $shrinkOutside + " 个像素") }
if ($lastInkRow -lt 0) { throw '长正文场景没有画出任何文字' }
# Every drawn pixel stays inside the English block: prose may not expand into the rest of the
# client. The text shrinks to fit, and only if that is impossible does the overflow state appear.
if ($lastInkRow -ge 140) {
    throw ("长正文越出了源块底边（末行 " + $lastInkRow + "），占用了客户端其余区域")
}
if ($labelType.GetField('Overflow', $all).GetValue($shrinkLabel)) {
    throw '在块内可以缩放下容纳的正文不应进入溢出状态'
}

# ---------------------------------------------------------------------------
# Item 1: the merge path must not lose geometry metadata. The reviewer's repro produced
# Bounds.Height=40 with SourceBounds.Height=14 and a single source row.
# ---------------------------------------------------------------------------
$confidenceType = $assembly.GetType('MapleOverlay.LabelConfidence', $true)
$mergeInto = $overlayType.GetMethod('MergeLabelInto', $all)
$mergeLines = $overlayType.GetMethod('MergeSourceLines', $all)
if ($null -eq $mergeInto -or $null -eq $mergeLines) {
    throw '缺少共享标签合并助手 MergeLabelInto/MergeSourceLines'
}

function New-LabelWithRows([System.Drawing.RectangleF]$bounds, [System.Drawing.RectangleF]$source,
    [System.Drawing.RectangleF[]]$rows, [string]$confidence) {
    $label = [Activator]::CreateInstance($labelType, $true)
    $labelType.GetField('Bounds', $all).SetValue($label, $bounds)
    $labelType.GetField('SourceBounds', $all).SetValue($label, $source)
    $labelType.GetField('HasSourceBounds', $all).SetValue($label, $true)
    $rowList = New-Object 'System.Collections.Generic.List[System.Drawing.RectangleF]'
    foreach ($row in $rows) { $rowList.Add($row) }
    $labelType.GetField('SourceLineBounds', $all).SetValue($label, $rowList)
    $labelType.GetField('Confidence', $all).SetValue($label,
        [Enum]::Parse($confidenceType, $confidence))
    return $label
}

$mergeTarget = New-LabelWithRows (New-Object System.Drawing.RectangleF(100, 200, 300, 20)) `
    (New-Object System.Drawing.RectangleF(103, 202, 294, 16)) @(
        (New-Object System.Drawing.RectangleF(103, 202, 294, 16))) 'High'
$mergeSource = New-LabelWithRows (New-Object System.Drawing.RectangleF(100, 246, 300, 20)) `
    (New-Object System.Drawing.RectangleF(103, 248, 294, 16)) @(
        (New-Object System.Drawing.RectangleF(103, 248, 294, 16)),
        (New-Object System.Drawing.RectangleF(103, 202, 294, 16))) 'Low'
Invoke-Method $mergeInto $null @($mergeTarget, $mergeSource) | Out-Null
$mergedBounds = $labelType.GetField('Bounds', $all).GetValue($mergeTarget)
$mergedSource = $labelType.GetField('SourceBounds', $all).GetValue($mergeTarget)
$mergedRows = $labelType.GetField('SourceLineBounds', $all).GetValue($mergeTarget)
$mergedConfidence = $labelType.GetField('Confidence', $all).GetValue($mergeTarget)
if ([single]$mergedBounds.Height -le [single]$mergedSource.Height) {
    throw ("合并后 SourceBounds 没有随 Bounds 一起并集：Bounds=" + $mergedBounds.Height +
        " Source=" + $mergedSource.Height)
}
if ([single]$mergedSource.Height -le 16.5) {
    throw ("合并后 SourceBounds 仍是单行：" + $mergedSource.Height)
}
if ($mergedRows.Count -ne 2) {
    throw ("合并后逐行源矩形数量错误（应为 2，去重且有序）：" + $mergedRows.Count)
}
if ([single]$mergedRows[0].Top -gt [single]$mergedRows[1].Top) { throw '合并后的源行没有按顺序排列' }
if ($mergedConfidence.ToString() -ne 'High') { throw '合并丢掉了更强的置信度证据' }

# The stronger evidence must win in either direction.
$reverseTarget = New-LabelWithRows (New-Object System.Drawing.RectangleF(100, 300, 300, 20)) `
    (New-Object System.Drawing.RectangleF(103, 302, 294, 16)) @(
        (New-Object System.Drawing.RectangleF(103, 302, 294, 16))) 'Low'
$reverseSource = New-LabelWithRows (New-Object System.Drawing.RectangleF(100, 346, 300, 20)) `
    (New-Object System.Drawing.RectangleF(103, 348, 294, 16)) @(
        (New-Object System.Drawing.RectangleF(103, 348, 294, 16))) 'High'
Invoke-Method $mergeInto $null @($reverseTarget, $reverseSource) | Out-Null
if ($labelType.GetField('Confidence', $all).GetValue($reverseTarget).ToString() -ne 'High') {
    throw '合并没有保留来自被删标签的更强置信度'
}

# ---------------------------------------------------------------------------
# P1-C: an over-long body must never be silently clipped or truncated. It either fits completely or
# the explicit "译文过长，已保留原文" state appears inside the boundary, the original English rows are
# left untouched, and neighbouring UI is unaffected. Input above 4096 characters takes that path.
# ---------------------------------------------------------------------------
$sentence = '温斯顿是一名在勇士部落东侧岩石山脉中活动的考古学家，他丢失了长期收集的动物化石和植物化石。'
$overflowBody = ($sentence * 12) + ('末尾标记结束。' * 8)
$hugeBody = '温斯顿' * 2100
if ($overflowBody.Length -lt 500) { throw ("溢出用例不足 500 字：" + $overflowBody.Length) }
if ($hugeBody.Length -le 4096) { throw ("超限用例不足 4096 字：" + $hugeBody.Length) }
$bigBlock = New-Object System.Drawing.RectangleF(60, 40, 380, 120)
$sourceRows = @(
    (New-Object System.Drawing.RectangleF(60, 44, 380, 16)),
    (New-Object System.Drawing.RectangleF(60, 66, 380, 16)))

function New-OverflowCanvas {
    $bitmap = New-Object System.Drawing.Bitmap(600, 400, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::White)
    $english = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 20, 60, 200))
    foreach ($row in $script:sourceRows) { $graphics.FillRectangle($english, $row) }
    # Neighbouring UI: one sentinel below the block, one to its right.
    $sentinel = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 20, 200, 40))
    $graphics.FillRectangle($sentinel, 100, 300, 200, 40)
    $graphics.FillRectangle($sentinel, 470, 50, 100, 20)
    $graphics.Dispose(); $english.Dispose(); $sentinel.Dispose()
    return $bitmap
}

foreach ($case in @(@('584 字正文', $overflowBody), @('>4096 字正文', $hugeBody))) {
    $canvas6 = New-OverflowCanvas
    $overflowLabel = New-TestLabel $bigBlock $bigBlock $true $case[1]
    $labelType.GetField('RenderMode', $all).SetValue($overflowLabel, [Enum]::Parse($modeType, 'Seamless'))
    $labelType.GetField('BackgroundColor', $all).SetValue($overflowLabel, $parchment)
    $overflowRows = New-Object 'System.Collections.Generic.List[System.Drawing.RectangleF]'
    foreach ($row in $sourceRows) { $overflowRows.Add($row) }
    $labelType.GetField('SourceLineBounds', $all).SetValue($overflowLabel, $overflowRows)
    Invoke-PaintTest $overflowLabel $canvas6
    if (-not $labelType.GetField('Overflow', $all).GetValue($overflowLabel)) {
        throw ($case[0] + ' 没有进入显式溢出状态（可能被静默裁剪或截断）')
    }
    # The original English must still be there: the overflow state never erases the source.
    $englishLeft = 0
    for ($x = 60; $x -lt 440; $x += 7) {
        $pixel = $canvas6.GetPixel($x, 72)
        if ($pixel.B -gt 150 -and $pixel.R -lt 90) { $englishLeft++ }
    }
    if ($englishLeft -lt 10) { throw ($case[0] + ' 把原文擦掉了，溢出时必须保留英文') }
    # The explicit notice is drawn as a bounded card inside the source block.
    $noticePixels = 0
    for ($y = 40; $y -lt 160; $y += 2) {
        for ($x = 60; $x -lt 440; $x += 4) {
            $pixel = $canvas6.GetPixel($x, $y)
            if ($pixel.R -lt 60 -and $pixel.G -lt 60 -and $pixel.B -lt 60) { $noticePixels++ }
        }
    }
    if ($noticePixels -lt 20) { throw ($case[0] + ' 没有画出显式的溢出提示') }
    $below = $canvas6.GetPixel(200, 320)
    $right = $canvas6.GetPixel(500, 60)
    if ($below.R -ne 20 -or $below.G -ne 200 -or $right.G -ne 200) {
        throw ($case[0] + ' 影响了相邻 UI（哨兵像素被改动）')
    }
    $canvas6.Dispose()
}

# ---------------------------------------------------------------------------
# Item 7: end-to-end. Labels merged by the production helper, then the production sampling pass,
# then the real OnPaint entry point - the source rows are not hand-injected as perfect geometry.
# A red stripe sits in the gap between the two original OCR rows: erasing one union rectangle
# would wipe it, erasing each row separately must leave it untouched.
# ---------------------------------------------------------------------------
$sample = $overlayType.GetMethod('SampleSeamlessBackgrounds', $all)
if ($null -eq $sample) { throw '缺少 SampleSeamlessBackgrounds' }
$pipelineCanvas = New-Object System.Drawing.Bitmap(1200, 800, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$pipelineGraphics = [System.Drawing.Graphics]::FromImage($pipelineCanvas)
$pipelineGraphics.Clear($parchment)
$stripeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 200, 30, 30))
$pipelineGraphics.FillRectangle($stripeBrush, 60, 60, 380, 6)
$pipelineGraphics.Dispose()
$stripeBrush.Dispose()
$stripeBefore = $pipelineCanvas.GetPixel(250, 62)

$rowOne = New-Object System.Drawing.RectangleF(60, 40, 380, 20)
$rowTwo = New-Object System.Drawing.RectangleF(60, 66, 380, 20)
$pipeTarget = New-LabelWithRows $rowOne $rowOne @($rowOne) 'High'
$pipeSource = New-LabelWithRows $rowTwo $rowTwo @($rowTwo) 'High'
Invoke-Method $mergeInto $null @($pipeTarget, $pipeSource) | Out-Null
$labelType.GetField('Text', $all).SetValue($pipeTarget, $bodyText)
$labelType.GetField('Wrap', $all).SetValue($pipeTarget, $true)

# The label list type is internal and the field initializer never runs on an uninitialized form,
# so the list is created from the field's own declared type.
$labelsField = $overlayType.GetField('labels', $all)
$pipelineLabels = [Activator]::CreateInstance($labelsField.FieldType)
$pipelineLabels.Add($pipeTarget)
$screenRect = New-Object System.Drawing.Rectangle(0, 0, 1200, 800)
Invoke-Method $sample $overlay @($pipelineCanvas, $screenRect, $pipelineLabels) | Out-Null
$pipeMode = $labelType.GetField('RenderMode', $all).GetValue($pipeTarget).ToString()
$pipeBackground = $labelType.GetField('BackgroundColor', $all).GetValue($pipeTarget)
$pipeInk = $labelType.GetField('TextColor', $all).GetValue($pipeTarget)
if ($pipeMode -ne 'Seamless') { throw ("端到端采样没有启用无缝：" + $pipeMode) }
if ($pipeBackground.R -ne $parchment.R -or $pipeBackground.G -ne $parchment.G) {
    throw '端到端采样没有取到面板底色'
}
if ($pipeInk -ne [System.Drawing.Color]::Empty) {
    throw '换行正文仍然沿用采样的混合墨色，可能在浅色面板上发紫'
}

$onPaint = $overlayType.GetMethod('OnPaint', $all)
if ($null -eq $onPaint) { throw '缺少 OnPaint' }
# An uninitialized Form cannot have its Bounds property set (the setter needs window state), but
# reading it returns 0x0, so the client rectangle alone defines the safe area here.
$overlayType.GetField('captureBounds', $all).SetValue($overlay,
    (New-Object System.Drawing.Rectangle(0, 0, 1920, 1200)))
$overlayType.GetField('visibleTranslation', $all).SetValue($overlay, $true)
$overlayType.GetField('lockNoticeText', $all).SetValue($overlay, '')
$labelsField.SetValue($overlay, $pipelineLabels)
$paintGraphics = [System.Drawing.Graphics]::FromImage($pipelineCanvas)
$paintArgs = New-Object System.Windows.Forms.PaintEventArgs($paintGraphics,
    (New-Object System.Drawing.Rectangle(0, 0, 1200, 800)))
try {
    Invoke-Method $onPaint $overlay @($paintArgs) | Out-Null
} finally {
    $paintGraphics.Dispose()
    $paintArgs.Dispose()
}
$stripeAfter = $pipelineCanvas.GetPixel(250, 62)
if ($stripeAfter.R -ne $stripeBefore.R -or $stripeAfter.G -ne $stripeBefore.G -or
    $stripeAfter.B -ne $stripeBefore.B) {
    throw ('端到端擦除把两行 OCR 之间的空隙也刷掉了：' + $stripeAfter.R + ',' + $stripeAfter.G +
        ',' + $stripeAfter.B)
}
$pipelineCanvas.Dispose()

# ---------------------------------------------------------------------------
# P1-A: erase rectangles are clipped to the measured span. A skill row that shares one OCR line
# with a "Master Level : 3" prefix must never erase that prefix.
# ---------------------------------------------------------------------------
$clampRows = $safetyType.GetMethod('ClampRowsToSpan', $all)
if ($null -eq $clampRows) { throw '缺少 ClampRowsToSpan' }
$rowFull = New-Object System.Drawing.RectangleF(60, 44, 380, 16)
$bodySpan = New-Object System.Drawing.RectangleF(150, 44, 290, 60)
$rowList = New-Object 'System.Collections.Generic.List[System.Drawing.RectangleF]'
$rowList.Add($rowFull)
$clamped = @(Invoke-Method $clampRows $null @($rowList, $bodySpan))
if ($clamped.Count -ne 1) { throw '行矩形裁剪后数量异常' }
if ([single]$clamped[0].Left -lt 149.5) { throw ("行矩形没有被裁剪到正文跨度：" + $clamped[0].Left) }
if ([single]$clamped[0].Right -gt 440.5) { throw '行矩形越出正文跨度右边界' }

$canvas7 = New-Object System.Drawing.Bitmap(600, 200, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g7 = [System.Drawing.Graphics]::FromImage($canvas7)
$g7.Clear([System.Drawing.Color]::White)
$prefixBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 200, 30, 30))
$g7.FillRectangle($prefixBrush, 60, 44, 90, 16)    # the structured prefix sentinel: x 60..150
$g7.FillRectangle($prefixBrush, 150, 44, 290, 16)  # the body area on the same OCR row
$g7.Dispose(); $prefixBrush.Dispose()
$prefixLabel = New-TestLabel $bodySpan $bodySpan $false '温斯顿化石'
$labelType.GetField('RenderMode', $all).SetValue($prefixLabel, [Enum]::Parse($modeType, 'Seamless'))
$labelType.GetField('BackgroundColor', $all).SetValue($prefixLabel, $parchment)
$clampedList = New-Object 'System.Collections.Generic.List[System.Drawing.RectangleF]'
foreach ($row in $clamped) { $clampedList.Add($row) }
$labelType.GetField('SourceLineBounds', $all).SetValue($prefixLabel, $clampedList)
Invoke-PaintTest $prefixLabel $canvas7
$prefixAfter = $canvas7.GetPixel(100, 50)
if ($prefixAfter.R -ne 200 -or $prefixAfter.G -ne 30) {
    throw ('前缀哨兵被擦除，说明 SourceLineBounds 宽于 SourceBounds：' + $prefixAfter.R + ',' + $prefixAfter.G)
}
$bodyAfter = $canvas7.GetPixel(300, 50)
if ($bodyAfter.R -eq 200 -and $bodyAfter.G -eq 30) { throw '正文区域没有被擦除' }
$canvas7.Dispose()

# ---------------------------------------------------------------------------
# P1-B: the early first-paint snapshot is request-local. Two frames, the second empty.
# ---------------------------------------------------------------------------
$overlaySource = Get-Content -LiteralPath (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw
if ($overlaySource -match 'if\s*\(!restoreExistingOverlay\)\s*earlyPaintedLabels\.Clear\(\)') {
    throw '自动刷新仍会保留上一请求的首显快照'
}
if ($overlaySource -notmatch 'visibleTranslation\s*=\s*false\s*;[\s\S]{0,900}?earlyPaintedLabels\.Clear\(\)\s*;[\s\S]{0,300}?if\s*\(!restoreExistingOverlay\)\s*labels\.Clear\(\)') {
    throw 'ShowTranslationAsync 没有在每次请求开始时无条件清空首显快照'
}
$preserve = $overlayType.GetMethod('PreserveEarlyLabels', $all)
if ($null -eq $preserve) { throw '缺少 PreserveEarlyLabels' }
$earlyLabel = New-TestLabel (New-Object System.Drawing.RectangleF(60, 40, 380, 20)) `
    (New-Object System.Drawing.RectangleF(60, 40, 380, 20)) $false '温斯顿'
$earlyList = [Activator]::CreateInstance($labelsField.FieldType)
$earlyList.Add($earlyLabel)
$publishedEmpty = [Activator]::CreateInstance($labelsField.FieldType)
Invoke-Method $preserve $null @($publishedEmpty, $earlyList, 0) | Out-Null
if ($publishedEmpty.Count -ne 0) { throw '第二帧为空时旧的首显标签被重新加回' }
$publishedFrame = [Activator]::CreateInstance($labelsField.FieldType)
Invoke-Method $preserve $null @($publishedFrame, $earlyList, 3) | Out-Null
if ($publishedFrame.Count -ne 1) { throw '当前帧的首显快照没有被保留' }

# ---------------------------------------------------------------------------
# P2-A: the cross-pass MergeLabels must keep both passes' row rectangles and the full source union.
# ---------------------------------------------------------------------------
$mergeLabelsMethod = $overlayType.GetMethod('MergeLabels', $all)
if ($null -eq $mergeLabelsMethod) { throw '缺少 MergeLabels' }
$rowA = New-Object System.Drawing.RectangleF(100, 200, 300, 18)
$rowB = New-Object System.Drawing.RectangleF(100, 222, 300, 18)
$pass1 = New-LabelWithRows (New-Object System.Drawing.RectangleF(100, 200, 300, 20)) $rowA @($rowA) 'High'
$pass2 = New-LabelWithRows (New-Object System.Drawing.RectangleF(100, 200, 300, 40)) `
    (New-Object System.Drawing.RectangleF(100, 200, 300, 40)) @($rowA, $rowB) 'High'
$labelType.GetField('Text', $all).SetValue($pass1, '同一段正文')
$labelType.GetField('Text', $all).SetValue($pass2, '同一段正文的完整译文')
$primary = [Activator]::CreateInstance($labelsField.FieldType)
$primary.Add($pass1)
$secondary = [Activator]::CreateInstance($labelsField.FieldType)
$secondary.Add($pass2)
Invoke-Method $mergeLabelsMethod $null @($primary, $secondary) | Out-Null
if ($primary.Count -ne 1) { throw ("跨通道合并后标签数量异常：" + $primary.Count) }
$mergedLabel = $primary[0]
$mergedRows = $labelType.GetField('SourceLineBounds', $all).GetValue($mergedLabel)
$mergedSource = $labelType.GetField('SourceBounds', $all).GetValue($mergedLabel)
if ($mergedRows.Count -ne 2) { throw ("跨通道合并丢掉了行矩形（应保留 2 行）：" + $mergedRows.Count) }
if ([single]$mergedSource.Height -lt 39.5) {
    throw ("跨通道合并没有保留完整源并集：" + $mergedSource.Height)
}

# Equal-confidence passes with identical text used to take neither replacement branch, silently
# dropping the second pass's rows even though it was classified as the same overlapping label.
$same1 = New-LabelWithRows (New-Object System.Drawing.RectangleF(100, 300, 300, 20)) `
    (New-Object System.Drawing.RectangleF(100, 300, 300, 18)) @(
        (New-Object System.Drawing.RectangleF(100, 300, 300, 18))) 'High'
$same2 = New-LabelWithRows (New-Object System.Drawing.RectangleF(100, 300, 300, 40)) `
    (New-Object System.Drawing.RectangleF(100, 300, 300, 40)) @(
        (New-Object System.Drawing.RectangleF(100, 300, 300, 18)),
        (New-Object System.Drawing.RectangleF(100, 322, 300, 18))) 'High'
$labelType.GetField('Text', $all).SetValue($same1, '相同译文')
$labelType.GetField('Text', $all).SetValue($same2, '相同译文')
$samePrimary = [Activator]::CreateInstance($labelsField.FieldType)
$samePrimary.Add($same1)
$sameSecondary = [Activator]::CreateInstance($labelsField.FieldType)
$sameSecondary.Add($same2)
Invoke-Method $mergeLabelsMethod $null @($samePrimary, $sameSecondary) | Out-Null
$sameRows = $labelType.GetField('SourceLineBounds', $all).GetValue($samePrimary[0])
$sameSource = $labelType.GetField('SourceBounds', $all).GetValue($samePrimary[0])
if ($sameRows.Count -ne 2 -or [single]$sameSource.Height -lt 39.5) {
    throw '同文同置信度跨通道合并仍丢失第二通道的源几何'
}

# The GDI+ objects are released last: every paint assertion above uses the same font.
$canvas.Dispose()
$canvas2.Dispose()
$canvas3.Dispose()
$testFont.Dispose()
# The production form also caches scaled label fonts. The test disposes the ones it caused to be
# created, so the test process does not leak GDI+ handles.
$scaledField = $overlayType.GetField('scaledLabelFonts', $all)
$scaledFonts = $scaledField.GetValue($overlay)
if ($null -ne $scaledFonts) {
    foreach ($font in $scaledFonts) { if ($null -ne $font) { $font.Dispose() } }
    $scaledField.SetValue($overlay, $null)
}
$overlayType.GetField('overlayFont', $all).SetValue($overlay, $null)

Write-Output ('标签几何：长正文被约束在测量块/面板边界内并按有界矩形换行、装不下时进入显式"译文过长"溢出状态且不擦原文、' +
    '短标签仍按原行为增长但有硬上限、超大/非有限/越界/对齐不良几何一律拒绝无缝、' +
    '多行正文逐行擦除而非整块并集、混合来源墨色不会被用作紫色译文、' +
    '洋红透明键与低对比墨色被拒、正文短续行（he looks...）纳入匹配')
