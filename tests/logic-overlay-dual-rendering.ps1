$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "找不到程序集：$exe" }

if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
}
$assembly = Load-TestAssembly $exe
$all = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'

# ---------------------------------------------------------------------------
# v4.0 dual rendering. Seamless may only replace the original text in place when the
# surface behind it is a measured flat colour; anything textured or gradient must fall
# back to the card, because a flat patch there would be more visible than the card it
# replaced. The decision function is what this test pins down.
# ---------------------------------------------------------------------------
$overlayType = $assembly.GetType('MapleOverlay.OverlayForm', $true)
$sample = $overlayType.GetMethod('TrySampleUniformBackground', $all)
if ($null -eq $sample) { throw '缺少 TrySampleUniformBackground' }

$modeType = $assembly.GetType('MapleOverlay.LabelRenderMode', $true)
if ($null -eq $modeType) { throw '缺少 LabelRenderMode 枚举' }
$modeNames = [Enum]::GetNames($modeType)
foreach ($expected in @('CardFallback', 'Seamless')) {
    if ($modeNames -notcontains $expected) { throw "LabelRenderMode 缺少 $expected" }
}

Add-Type -AssemblyName System.Drawing
$colorType = [System.Drawing.Color]

function Invoke-Sample([System.Drawing.Bitmap]$bitmap, [System.Drawing.Rectangle]$region) {
    # v4.0 added a third out parameter: the colour the original text was drawn in, which the
    # seamless renderer reuses for the translation. The reflection call has to match exactly.
    $args = New-Object 'object[]' 5
    $args[0] = $bitmap
    $args[1] = $region
    $args[2] = $null
    $args[3] = $null
    $args[4] = $null
    $ok = [bool]$sample.Invoke($null, $args)
    return @($ok, $args[2], $args[3], $args[4])
}

# 1. A flat surface must be accepted, and the average must match the fill.
$flat = New-Object System.Drawing.Bitmap(80, 60, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($flat)
$g.Clear([System.Drawing.Color]::FromArgb(255, 40, 44, 52))
$g.Dispose()
$r = Invoke-Sample $flat (New-Object System.Drawing.Rectangle(4, 4, 60, 40))
if (-not $r[0]) { throw '纯色背景未被接受' }
$avg = $r[1]
if ([Math]::Abs($avg.R - 40) -gt 2 -or [Math]::Abs($avg.G - 44) -gt 2 -or [Math]::Abs($avg.B - 52) -gt 2) {
    throw ("纯色背景平均色错误：" + $avg.R + "," + $avg.G + "," + $avg.B)
}
if ([single]$r[2] -gt 3.0) { throw ("纯色背景离散度异常：" + $r[2]) }

# 2. A gradient must be rejected by the deviation threshold the renderer uses.
$grad = New-Object System.Drawing.Bitmap(80, 60, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
for ($x = 0; $x -lt 80; $x++) {
    $v = [int](20 + $x * 2.6)
    if ($v -gt 255) { $v = 255 }
    for ($y = 0; $y -lt 60; $y++) { $grad.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $v, $v, $v)) }
}
$r2 = Invoke-Sample $grad (New-Object System.Drawing.Rectangle(0, 0, 80, 60))
if ($r2[0] -and [single]$r2[2] -le 26.0) {
    throw ("渐变背景离散度过低，会误用无缝覆盖：" + $r2[2])
}

# 3. Degenerate regions are refused outright.
$r3 = Invoke-Sample $flat (New-Object System.Drawing.Rectangle(0, 0, 1, 1))
if ($r3[0]) { throw '过小区域不应被接受' }

# 4. Out-of-bounds regions are refused instead of throwing.
$r4 = Invoke-Sample $flat (New-Object System.Drawing.Rectangle(70, 50, 40, 40))
if ($r4[0]) { throw '越界区域不应被接受' }

# 5. Ink recovery. A flat panel that carries dark glyph pixels must still be accepted, and the
#    reported ink must be the colour of those glyphs. This is what keeps the translation legible
#    on a pale panel instead of drawing invisible white text on parchment.
$inkboard = New-Object System.Drawing.Bitmap(80, 60, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g2 = [System.Drawing.Graphics]::FromImage($inkboard)
$g2.Clear([System.Drawing.Color]::FromArgb(255, 236, 232, 214))
$glyphs = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 32, 30, 28))
$g2.FillRectangle($glyphs, 10, 22, 60, 16)   # the glyph band covers about a quarter of the region
$g2.Dispose(); $glyphs.Dispose()
$r5 = Invoke-Sample $inkboard (New-Object System.Drawing.Rectangle(4, 4, 72, 52))
if (-not $r5[0]) { throw '带文字笔画的纯色底板被误拒（Seamless 会退化为卡片）' }
if ([single]$r5[2] -gt 26.0) { throw ("含文字笔画的纯色底板离散度过高：" + $r5[2]) }
$inkColor = $r5[3]
if ($inkColor -eq [System.Drawing.Color]::Empty) { throw '未回传原文笔画颜色' }
if ([Math]::Abs($inkColor.R - 32) -gt 6 -or [Math]::Abs($inkColor.G - 30) -gt 6 -or
    [Math]::Abs($inkColor.B - 28) -gt 6) {
    throw ("原文笔画颜色估计错误：" + $inkColor.R + "," + $inkColor.G + "," + $inkColor.B)
}
$inkboard.Dispose()

$flat.Dispose(); $grad.Dispose()

# ---------------------------------------------------------------------------
# Guard the guard: the renderer must keep both paths, and sampling must stay inside the
# capture pipeline (it needs the clean screenshot, never the painted overlay).
# ---------------------------------------------------------------------------
$source = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
foreach ($required in @(
    'internal enum LabelRenderMode',
    'CardFallback = 0',
    'Seamless = 1',
    'label.RenderMode == LabelRenderMode.Seamless',
    'private void SampleSeamlessBackgrounds(Bitmap bitmap, Rectangle screen, List<OverlayLabel> target)',
    'label.Confidence != LabelConfidence.High',
    'if (deviation > maximumDeviation)',
    'public Color BackgroundColor = Color.Empty;',
    'SampleSeamlessBackgrounds(bitmap, screen, next);')) {
    if (-not $source.Contains($required)) { throw "双渲染缺少守卫：$required" }
}
# The erase region must be derived from the measured source rectangles, never from the (possibly
# wider) layout box: that is what keeps live counters and levels uncovered. Since the repair the
# erase pass consumes the per-row source rectangles and the source union, so this guard pins the
# new call shape and additionally fails if the layout rectangle is ever used to erase.
if ($source -notmatch 'BuildEraseRects\(\s*label\.SourceLineBounds,\s*label\.SourceBounds') {
    throw '无缝覆盖未使用 SourceBounds/逐行源矩形作为擦除区域'
}
if ($source -match 'FillRectangle\(seamlessBrush,\s*layout') {
    throw '无缝覆盖用布局矩形擦除，会盖住动态数值'
}

# Sampling must happen exactly once, at the last moment where the clean pre-overlay screenshot is
# still alive: after the merges and before the labels are published. The earlier guard checked an
# unrelated Program.Benchmark pattern while production really samples at that single call site.
$sampleCalls = ([regex]::Matches($source, 'SampleSeamlessBackgrounds\(bitmap, screen, next\);')).Count
if ($sampleCalls -ne 1) { throw ("无缝采样调用点数量不为 1：" + $sampleCalls) }
if ($source -notmatch '(?s)MergeSameTextLabels\(next\);.*?SampleSeamlessBackgrounds\(bitmap, screen, next\);.*?labels\.Clear\(\); labels\.AddRange\(next\);') {
    throw '无缝采样不在合并之后、标签发布之前的干净帧位置'
}

Write-Output ('双渲染：纯色背景启用无缝原位替换、渐变/纹理回退卡片、越界与退化区域拒绝；' +
    '擦除区域严格取自 SourceBounds，不随译文变宽；基准路径保持卡片渲染不变')
