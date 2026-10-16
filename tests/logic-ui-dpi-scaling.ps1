$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
$errorPath = Join-Path $repoRoot 'main_ui_test_error.txt'
Add-Type -AssemblyName System.Drawing

foreach ($scale in @(100, 125, 150, 175, 200)) {
    Remove-Item -LiteralPath $errorPath -Force -ErrorAction SilentlyContinue
    $arguments = @('--main-ui-test')
    if ($scale -ne 100) { $arguments += "--main-ui-test-scale=$scale" }
if (-not (Get-Command Start-ProductExe -ErrorAction SilentlyContinue)) { . (Join-Path $PSScriptRoot 'Start-ProductExe.ps1') }
    Start-ProductExe -FilePath $exe -ArgumentList $arguments -WorkingDirectory $repoRoot `
        -WindowStyle Hidden -Wait
    if (Test-Path -LiteralPath $errorPath) {
        throw "$scale% UI 自适应失败：$(Get-Content $errorPath -Raw -Encoding UTF8)"
    }
    $name = if ($scale -eq 100) { 'main_ui_test.png' } else { "main_ui_test_$scale.png" }
    $path = Join-Path $repoRoot $name
    if (-not (Test-Path -LiteralPath $path)) { throw "$scale% UI 截图没有生成" }
    $image = [Drawing.Image]::FromFile($path)
    try {
        $minimumWidth = [int](430 * $scale / 100)
        $minimumHeight = [int](680 * $scale / 100)
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
Start-ProductExe -FilePath $exe -ArgumentList @('--main-ui-test',
    '--main-ui-test-transition=150,125,200,100,150') -WorkingDirectory $repoRoot `
    -WindowStyle Hidden -Wait
if (Test-Path -LiteralPath $errorPath) {
    throw "跨屏 DPI 重排失败：$(Get-Content $errorPath -Raw -Encoding UTF8)"
}
$transitionImage = Join-Path $repoRoot 'main_ui_test_transition.png'
if (-not (Test-Path -LiteralPath $transitionImage)) { throw '跨屏 DPI 重排截图没有生成' }
Remove-Item -LiteralPath $transitionImage -Force -ErrorAction SilentlyContinue

# Page 2 and page 3 are states inside the same vertical form, not separate windows.
foreach ($pageArgument in @('--main-ui-test-dashboard', '--main-ui-test-ai')) {
    foreach ($scale in @(100, 125, 150, 175, 200)) {
        Remove-Item -LiteralPath $errorPath -Force -ErrorAction SilentlyContinue
        $pageArguments = @('--main-ui-test', $pageArgument)
        if ($scale -ne 100) { $pageArguments += "--main-ui-test-scale=$scale" }
        Start-ProductExe -FilePath $exe -ArgumentList $pageArguments `
            -WorkingDirectory $repoRoot -WindowStyle Hidden -Wait
        if (Test-Path -LiteralPath $errorPath) {
            throw "$pageArgument/$scale% 同窗页面自适应失败：$(Get-Content $errorPath -Raw -Encoding UTF8)"
        }
        $pageImageName = if ($scale -eq 100) { 'main_ui_test.png' } `
            else { "main_ui_test_$scale.png" }
        $pageImagePath = Join-Path $repoRoot $pageImageName
        $pageImage = [Drawing.Image]::FromFile($pageImagePath)
        try {
            if ($pageImage.Width -lt [int](430 * $scale / 100) -or
                $pageImage.Height -lt [int](680 * $scale / 100)) {
                throw "$pageArgument/$scale% 没有保持竖向主框体：$($pageImage.Width)x$($pageImage.Height)"
            }
        } finally { $pageImage.Dispose() }
        if ($scale -ne 100) {
            Remove-Item -LiteralPath $pageImagePath -Force -ErrorAction SilentlyContinue
        }
    }
}

# Maximizing is temporary: the saved default snap preference must not be switched off,
# and the centered card column must remain in bounds at desktop width.
Remove-Item -LiteralPath $errorPath -Force -ErrorAction SilentlyContinue
$maxImagePath = Join-Path $repoRoot 'main_ui_test_maximized.png'
Remove-Item -LiteralPath $maxImagePath -Force -ErrorAction SilentlyContinue
Start-ProductExe -FilePath $exe -ArgumentList @('--main-ui-test',
    '--main-ui-test-dashboard', '--main-ui-test-maximized') -WorkingDirectory $repoRoot `
    -WindowStyle Hidden -Wait
if (Test-Path -LiteralPath $errorPath) {
    throw "最大化主界面失败：$(Get-Content $errorPath -Raw -Encoding UTF8)"
}
if (-not (Test-Path -LiteralPath $maxImagePath)) { throw '最大化主界面截图没有生成' }
$maxImage = [Drawing.Image]::FromFile($maxImagePath)
try {
    if ($maxImage.Width -lt 700 -or $maxImage.Height -lt 500) {
        throw "最大化主界面尺寸异常：$($maxImage.Width)x$($maxImage.Height)"
    }
} finally { $maxImage.Dispose() }
Remove-Item -LiteralPath $maxImagePath -Force -ErrorAction SilentlyContinue

Write-Output 'UI自适应：三页同窗、最大化、100% / 125% / 150% / 175% / 200% 及混合DPI跨屏重排通过'
