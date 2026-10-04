$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
$errorPath = Join-Path $repoRoot 'main_ui_test_error.txt'
Add-Type -AssemblyName System.Drawing

foreach ($scale in @(100, 125, 150, 175, 200)) {
    Remove-Item -LiteralPath $errorPath -Force -ErrorAction SilentlyContinue
    $arguments = @('--main-ui-test')
    if ($scale -ne 100) { $arguments += "--main-ui-test-scale=$scale" }
    Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $repoRoot `
        -WindowStyle Hidden -Wait
    if (Test-Path -LiteralPath $errorPath) {
        throw "$scale% UI 自适应失败：$(Get-Content $errorPath -Raw -Encoding UTF8)"
    }
    $name = if ($scale -eq 100) { 'main_ui_test.png' } else { "main_ui_test_$scale.png" }
    $path = Join-Path $repoRoot $name
    if (-not (Test-Path -LiteralPath $path)) { throw "$scale% UI 截图没有生成" }
    $image = [Drawing.Image]::FromFile($path)
    try {
        $minimumWidth = [int](540 * $scale / 100)
        $minimumHeight = [int](540 * $scale / 100)
        if ($image.Width -lt $minimumWidth -or $image.Height -lt $minimumHeight) {
            throw "$scale% UI 没有同步缩放窗口和控件：$($image.Width)x$($image.Height)"
        }
    } finally {
        $image.Dispose()
    }
    if ($scale -ne 100) {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    }
}

# Repeatedly re-apply layouts from the immutable 96-DPI baseline.  This catches the
# compounding-size bug that only appears when a window moves between mixed-DPI monitors.
Remove-Item -LiteralPath $errorPath -Force -ErrorAction SilentlyContinue
Start-Process -FilePath $exe -ArgumentList @('--main-ui-test',
    '--main-ui-test-transition=150,125,200,100,150') -WorkingDirectory $repoRoot `
    -WindowStyle Hidden -Wait
if (Test-Path -LiteralPath $errorPath) {
    throw "跨屏 DPI 重排失败：$(Get-Content $errorPath -Raw -Encoding UTF8)"
}
$transitionImage = Join-Path $repoRoot 'main_ui_test_transition.png'
if (-not (Test-Path -LiteralPath $transitionImage)) { throw '跨屏 DPI 重排截图没有生成' }
Remove-Item -LiteralPath $transitionImage -Force -ErrorAction SilentlyContinue

Write-Output 'UI自适应：100% / 125% / 150% / 175% / 200% 及混合DPI跨屏重排通过，无越界或文字截断'
