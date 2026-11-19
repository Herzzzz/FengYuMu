$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) { . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1') }
$assembly = Load-TestAssembly $exe

$labelType = $assembly.GetType('MapleOverlay.OverlayLabel', $true)
$safetyType = $assembly.GetType('MapleOverlay.OverlayPaintSafety', $true)
$prepare = $safetyType.GetMethod('TryPrepare',
    [Reflection.BindingFlags]'Static,NonPublic,Public')

function Test-PaintLabel {
    param(
        [string]$Name,
        [AllowNull()][string]$Text,
        [single]$X,
        [single]$Y,
        [single]$Width,
        [single]$Height,
        [bool]$Expected
    )
    $label = [Activator]::CreateInstance($labelType, $true)
    $labelType.GetField('Text').SetValue($label, $Text)
    $labelType.GetField('Bounds').SetValue($label,
        (New-Object System.Drawing.RectangleF -ArgumentList $X, $Y, $Width, $Height))
    $arguments = New-Object 'object[]' 3
    $arguments[0] = $label
    $arguments[1] = $null
    $arguments[2] = [System.Drawing.RectangleF]::Empty
    $actual = [bool]$prepare.Invoke($null, $arguments)
    if ($actual -ne $Expected) {
        throw "$Name 绘制校验结果错误：actual=$actual expected=$Expected"
    }
    return $arguments
}

$valid = Test-PaintLabel '正常标签' '需要等级：50' 20 30 100 22 $true
$validBounds = [System.Drawing.RectangleF]$valid[2]
if ($valid[1] -ne '需要等级：50' -or $validBounds.Width -ne 100 -or
    $validBounds.Height -ne 22) {
    throw '正常标签在绘制校验中被意外修改'
}

$zero = Test-PaintLabel '零尺寸标签' '等级' 20 30 0 0 $true
$zeroBounds = [System.Drawing.RectangleF]$zero[2]
if ($zeroBounds.Width -ne 1 -or $zeroBounds.Height -ne 1) {
    throw '零尺寸标签没有被收敛为安全尺寸'
}

Test-PaintLabel '空文本' $null 20 30 100 22 $false | Out-Null
Test-PaintLabel '空白文本' '   ' 20 30 100 22 $false | Out-Null
Test-PaintLabel 'NaN 坐标' '等级' ([single]::NaN) 30 100 22 $false | Out-Null
Test-PaintLabel '无穷尺寸' '等级' 20 30 ([single]::PositiveInfinity) 22 $false | Out-Null
Test-PaintLabel '负尺寸' '等级' 20 30 -5 22 $false | Out-Null
Test-PaintLabel '异常大坐标' '等级' 100001 30 100 22 $false | Out-Null

$long = '长' * 5000
$kept = Test-PaintLabel '超长文本' $long 20 30 100 22 $true
# The text is no longer silently truncated at the paint boundary: an over-long translation is
# routed to the explicit overflow state by the renderer, so TryPrepare has to hand the full string
# through (and the renderer must know the limit that triggers the overflow path).
if (([string]$kept[1]).Length -ne 5000) { throw '超长文本被静默截断，应改为走显式溢出状态' }

$source = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
foreach ($required in @(
    'OverlayLabel[] snapshot = labels.ToArray()',
    'OverlayPaintSafety.TryPrepare',
    'internal const int MaximumDrawableTextLength = 4096;',
    'PaintOverflowNotice(graphics, label, r, safeArea, background, border, textBrush)',
    'catch (ArgumentException)',
    'catch (System.Runtime.InteropServices.ExternalException)',
    'shuttingDown = true;',
    'overlayFont.Dispose();')) {
    if (-not $source.Contains($required)) { throw "悬浮层绘制保护缺少：$required" }
}

Write-Output '悬浮层绘制安全：空文本、非法坐标/尺寸、超长文本、关闭竞态与单标签 GDI+ 故障隔离通过'
