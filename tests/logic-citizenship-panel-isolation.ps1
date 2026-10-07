$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "找不到程序集：$exe" }
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot '枫语幕词库.tsv'))) { throw '找不到词库' }

# ---------------------------------------------------------------------------
# CITIZENSHIP is simultaneously the Character Info tab and a quest title in the
# citizenship chain. The Character Info template used to trigger on any OCR line
# containing "citizenship", so a quest list whose first row is the Citizenship
# quest projected the whole Character Info layout (role / level / popularity /
# guild / party / trade / pet rows) onto the Quest window at unrelated offsets:
# labels whose source text does not exist anywhere in the frame.
#
# The scene below is drawn in code on purpose so this regression needs no
# external screenshot and cannot silently drift with an asset on disk.
# ---------------------------------------------------------------------------
Add-Type -AssemblyName System.Drawing

$workRoot = Join-Path $env:TEMP 'fengyumu-citizenship-isolation'
if (Test-Path -LiteralPath $workRoot) { Remove-Item -LiteralPath $workRoot -Recurse -Force }
New-Item -ItemType Directory -Path $workRoot -Force | Out-Null
$scenePath = Join-Path $workRoot 'quest-window-with-citizenship-row.png'

$width = 760
$height = 540
$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$graphics.Clear([System.Drawing.Color]::FromArgb(255, 236, 233, 226))
$graphics.FillRectangle([System.Drawing.Brushes]::White, 0, 0, $width, $height)
$graphics.DrawRectangle((New-Object System.Drawing.Pen(
    [System.Drawing.Color]::FromArgb(255, 120, 120, 140), 2)), 1, 1, $width - 3, $height - 3)
$graphics.FillRectangle((New-Object System.Drawing.SolidBrush(
    [System.Drawing.Color]::FromArgb(255, 72, 86, 140))), 4, 4, $width - 8, 26)

$titleFont = New-Object System.Drawing.Font('Arial', 13, [System.Drawing.FontStyle]::Bold)
$tabFont = New-Object System.Drawing.Font('Arial', 12, [System.Drawing.FontStyle]::Regular)
$itemFont = New-Object System.Drawing.Font('Arial', 14, [System.Drawing.FontStyle]::Regular)
$graphics.DrawString('QUEST', $titleFont, [System.Drawing.Brushes]::White, 12, 7)

# Tabs keep their in-game low-contrast look; OCR frequently returns only the
# "QUEST" caption, which is exactly the case the isolation guard must survive.
$graphics.FillRectangle((New-Object System.Drawing.SolidBrush(
    [System.Drawing.Color]::FromArgb(255, 206, 203, 196))), 10, 36, 118, 26)
$graphics.DrawString('Available', $tabFont, [System.Drawing.Brushes]::White, 22, 40)
$graphics.FillRectangle((New-Object System.Drawing.SolidBrush(
    [System.Drawing.Color]::FromArgb(255, 233, 120, 150))), 132, 36, 128, 26)
$graphics.DrawString('In Progress', $tabFont, [System.Drawing.Brushes]::White, 142, 40)
$graphics.FillRectangle((New-Object System.Drawing.SolidBrush(
    [System.Drawing.Color]::FromArgb(255, 206, 203, 196))), 264, 36, 118, 26)
$graphics.DrawString('Completed', $tabFont, [System.Drawing.Brushes]::White, 274, 40)

# First row is the Citizenship quest itself - the trigger of the historical bug.
$graphics.FillRectangle((New-Object System.Drawing.SolidBrush(
    [System.Drawing.Color]::FromArgb(255, 214, 206, 186))), 14, 76, 480, 30)
$graphics.DrawString('Citizenship', $itemFont, [System.Drawing.Brushes]::Black, 60, 80)
$graphics.FillRectangle((New-Object System.Drawing.SolidBrush(
    [System.Drawing.Color]::FromArgb(255, 70, 110, 170))), 14, 110, 480, 30)
$graphics.DrawString('Donating to Kerning City', $itemFont, [System.Drawing.Brushes]::White, 60, 114)

$graphics.DrawString('DETAILS', $tabFont, [System.Drawing.Brushes]::Black, 200, 500)
$graphics.DrawString('QUEST HELPER', $tabFont, [System.Drawing.Brushes]::Black, 430, 500)
$graphics.DrawString('FORFEIT', $tabFont, [System.Drawing.Brushes]::Black, 620, 500)

$bitmap.Save($scenePath, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()
$titleFont.Dispose(); $tabFont.Dispose(); $itemFont.Dispose()

function Invoke-Benchmark([string]$image) {
    Start-Process -FilePath $exe -ArgumentList @('--benchmark',
        "--benchmark-image=$image", '--benchmark-range=balanced') `
        -WorkingDirectory $repoRoot -Wait
    $result = Get-Content (Join-Path $repoRoot 'last_run.txt') -Raw -Encoding UTF8
    if ($result.StartsWith('错误=')) { throw "基准执行失败：$result" }
    return $result
}

function Get-OverlayLabels([string]$result) {
    $line = ($result -split "`r?`n" | Where-Object { $_.StartsWith('覆盖标签=') })
    if (-not $line) { return @() }
    return @(($line.Substring('覆盖标签='.Length) -split '\s*\|\s*') | ForEach-Object {
        ($_ -split '@\{')[0]
    })
}

$result = Invoke-Benchmark $scenePath
$labels = Get-OverlayLabels $result

# Case validity: the scene must actually present the trigger, otherwise the
# assertions below would pass vacuously.
if ($result -notmatch '(?i)citizenship') {
    throw '用例失效：OCR 未读到 Citizenship 任务行，无法验证锚点隔离'
}
foreach ($anchor in @('quest', 'available', 'in progress', 'completed')) {
    if ($result -notmatch ('(?i)' + [regex]::Escape($anchor))) {
        throw "用例失效：OCR 未读到任务窗口外壳标记 $anchor"
    }
}

# The Quest window itself must still be translated.
foreach ($expected in @('任务', '可接取', '进行中', '已完成', '向废弃都市捐赠')) {
    if ($labels -notcontains $expected) {
        throw "任务窗口自身译文缺失：$expected（实际：$($labels -join ', ')）"
    }
}

# The historical defect: Character Info rows painted onto the Quest window.
$forbidden = @('角色信息', '公民身份', '职业', '人气', '家族', '邀请组队', '申请交易', '查看宠物信息')
foreach ($text in $forbidden) {
    if ($labels -contains $text) {
        throw "任务窗口仍被人物信息布局污染：$text（实际：$($labels -join ', ')）"
    }
}
if ($labels -contains '等级') {
    throw "任务窗口仍被人物信息布局污染：等级"
}

# Guard the guard: the Character Info anchor must stay conditioned on the absence
# of a Quest surface, and must keep using the loose shell probe so that a Quest
# window whose tabs OCR failed is still recognised.
$source = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
foreach ($required in @(
    'bool questSurfacePresent = questShellPresent || anyShellAnchor != null ||',
    'HasQuestSummaryMarker(lines);',
    'else if (!characterInfo && !questSurfacePresent &&',
    'normalized.Contains("citizenship"))')) {
    if (-not $source.Contains($required)) {
        throw "人物信息锚点隔离守卫缺失：$required"
    }
}

# Character Info must still work when its own window is the surface being read.
$characterFixture = Join-Path $repoRoot 'tests\fixtures\character-info-pet-item-list-20260909.png'
if (Test-Path -LiteralPath $characterFixture) {
    $characterResult = Invoke-Benchmark $characterFixture
    $characterLabels = Get-OverlayLabels $characterResult
    foreach ($expected in @('角色信息', '公民身份', '职业', '人气', '家族',
        '邀请组队', '申请交易', '查看宠物信息')) {
        if ($characterLabels -notcontains $expected) {
            throw "隔离守卫误伤人物信息窗口，标签缺失：$expected"
        }
    }
    Write-Output '人物信息窗口回归：8 个人物标签全部保留'
}

Write-Output ('公民系统面板隔离：任务窗口 5 个结构标签正常，Citizenship 任务行不再触发人物信息布局，' +
    '任务窗口未出现任何人物信息标签')
