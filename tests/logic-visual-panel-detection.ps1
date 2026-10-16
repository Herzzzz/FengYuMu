$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

# The visual detector is intentionally exercised with a small synthetic scene instead of a
# checked-in screenshot. This keeps the regression deterministic and avoids coupling the test
# to a particular monitor scale or to one user's MapleStory scene.
Add-Type -AssemblyName System.Drawing
if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
}
$assembly = Load-TestAssembly (Join-Path $repoRoot '枫语幕.exe')
$flags = [Reflection.BindingFlags]'Instance,Static,NonPublic,Public'
$visionType = $assembly.GetType('MapleOverlay.ClassicSceneVision', $true)
$findPanels = $visionType.GetMethod('FindForegroundPanels', $flags)
if ($null -eq $findPanels) { throw '缺少 ClassicSceneVision.FindForegroundPanels API' }

$width = 1000
$height = 760
[System.Drawing.Bitmap]$bitmap = New-Object System.Drawing.Bitmap($width, $height,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$lightBrush = $null
$darkBrush = $null
$bubbleBrush = $null
$noiseBrush = $null
try {
    # Neutral background: it must not satisfy either the light-panel or dark-blue mask.
    $graphics.Clear([System.Drawing.Color]::FromArgb(255, 70, 70, 70))

    # Large light foreground window (the typical quest/options surface).
    $lightBrush = New-Object System.Drawing.SolidBrush(
        [System.Drawing.Color]::FromArgb(255, 236, 238, 232))
    $graphics.FillRectangle($lightBrush, 100, 100, 420, 300)

    # Large dark-blue foreground tooltip (the typical item/skill/buff detail surface).
    $darkBrush = New-Object System.Drawing.SolidBrush(
        [System.Drawing.Color]::FromArgb(255, 34, 58, 122))
    $graphics.FillRectangle($darkBrush, 580, 300, 320, 300)

    # A small speech bubble must not become a foreground panel merely because it is white.
    $bubbleBrush = New-Object System.Drawing.SolidBrush(
        [System.Drawing.Color]::FromArgb(255, 245, 245, 238))
    $graphics.FillRectangle($bubbleBrush, 24, 640, 122, 64)

    # Disconnected blue visual noise must not be promoted to a tooltip. Each mark is kept
    # well below the detector's minimum component size and separated from its neighbours.
    $noiseBrush = New-Object System.Drawing.SolidBrush(
        [System.Drawing.Color]::FromArgb(255, 25, 66, 122))
    foreach ($point in @(
        @{ X = 600; Y = 58 }, @{ X = 646; Y = 72 }, @{ X = 692; Y = 54 },
        @{ X = 738; Y = 86 }, @{ X = 790; Y = 62 }, @{ X = 840; Y = 94 },
        @{ X = 614; Y = 166 }, @{ X = 670; Y = 142 }, @{ X = 730; Y = 178 },
        @{ X = 802; Y = 150 })) {
        $graphics.FillEllipse($noiseBrush, $point.X, $point.Y, 22, 22)
    }
    $graphics.Flush()

    $regions = @($findPanels.Invoke($null, [object[]]@($bitmap)))
    $lightRegions = @($regions | Where-Object {
        [string]$_.Kind -eq 'Light' -and $_.Bounds.Width -ge 300 -and $_.Bounds.Height -ge 220
    })
    $darkRegions = @($regions | Where-Object {
        [string]$_.Kind -eq 'DarkBlue' -and $_.Bounds.Width -ge 240 -and $_.Bounds.Height -ge 220
    })
    if ($lightRegions.Count -ne 1) {
        throw "合成浅色面板检测异常：expected=1 actual=$($lightRegions.Count) total=$($regions.Count)"
    }
    if ($darkRegions.Count -ne 1) {
        throw "合成深蓝提示框检测异常：expected=1 actual=$($darkRegions.Count) total=$($regions.Count)"
    }

    $bubble = New-Object System.Drawing.Rectangle(24, 640, 122, 64)
    $noise = New-Object System.Drawing.Rectangle(580, 40, 300, 190)
    foreach ($region in $regions) {
        $bounds = [System.Drawing.Rectangle]$region.Bounds
        if (-not [System.Drawing.Rectangle]::Intersect($bounds, $bubble).IsEmpty) {
            throw "小白色气泡被错误接受为面板：$bounds"
        }
        if (-not [System.Drawing.Rectangle]::Intersect($bounds, $noise).IsEmpty) {
            throw "不规则蓝色噪声被错误接受为面板：$bounds"
        }
    }
}
finally {
    if ($noiseBrush -ne $null) { $noiseBrush.Dispose() }
    if ($bubbleBrush -ne $null) { $bubbleBrush.Dispose() }
    if ($darkBrush -ne $null) { $darkBrush.Dispose() }
    if ($lightBrush -ne $null) { $lightBrush.Dispose() }
    if ($graphics -ne $null) { $graphics.Dispose() }
    if ($bitmap -ne $null) { $bitmap.Dispose() }
}

# Pixel capture and cancellation are tested through the production worker by
# logic-window-capture.ps1; this test retains its actual scene-vision assertions.
Write-Output '视觉面板回归通过：合成场景识别浅色任务面板与深蓝提示框，忽略小气泡和不规则蓝噪声；窗口捕获另由生产子进程行为测试覆盖。'
