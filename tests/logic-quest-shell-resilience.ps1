$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "找不到程序集：$exe" }

# ---------------------------------------------------------------------------
# The quest pipeline used to key off the QUEST caption and the Available /
# In Progress / Completed tabs. Windows OCR misses those low-contrast tabs often,
# and an overlay painted on an earlier pass hides the caption too - so the next
# pass fell back to "plain screen" and silently stopped translating the quest
# list, the detail title and the whole body text. That is the "works once, then
# stops" symptom.
#
# "Quest Summary" belongs to the quest pane only, so it must keep the quest
# surface recognised even when everything else is covered.
# ---------------------------------------------------------------------------
Add-Type -AssemblyName System.Drawing

$workRoot = Join-Path $env:TEMP 'fengyumu-quest-shell-resilience'
if (Test-Path -LiteralPath $workRoot) { Remove-Item -LiteralPath $workRoot -Recurse -Force }
New-Item -ItemType Directory -Path $workRoot -Force | Out-Null

function New-QuestScene([string]$path) {
    $w = 760; $h = 560
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::FromArgb(255, 236, 233, 226))
    $g.FillRectangle([System.Drawing.Brushes]::White, 0, 0, $w, $h)
    $g.DrawRectangle((New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 120, 120, 140), 2)), 1, 1, $w - 3, $h - 3)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 72, 86, 140))), 4, 4, $w - 8, 26)
    $fTitle = New-Object System.Drawing.Font('Arial', 13, [System.Drawing.FontStyle]::Bold)
    $fTab = New-Object System.Drawing.Font('Arial', 12)
    $g.DrawString('QUEST', $fTitle, [System.Drawing.Brushes]::White, 12, 7)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 206, 203, 196))), 10, 36, 118, 26)
    $g.DrawString('Available', $fTab, [System.Drawing.Brushes]::White, 22, 40)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 233, 120, 150))), 132, 36, 128, 26)
    $g.DrawString('In Progress', $fTab, [System.Drawing.Brushes]::White, 142, 40)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 206, 203, 196))), 264, 36, 118, 26)
    $g.DrawString('Completed', $fTab, [System.Drawing.Brushes]::White, 274, 40)

    $fItem = New-Object System.Drawing.Font('Arial', 13)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 70, 110, 170))), 14, 76, 340, 26)
    $g.DrawString('Vicious in Need of an Apprentice', $fItem, [System.Drawing.Brushes]::White, 58, 79)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 226, 222, 210))), 14, 106, 340, 26)
    $g.DrawString("Nella & Kerning City Citizen's R...", $fItem, [System.Drawing.Brushes]::Black, 58, 109)

    $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 70, 110, 170))), 372, 76, 372, 34)
    $fDetail = New-Object System.Drawing.Font('Arial', 13, [System.Drawing.FontStyle]::Bold)
    $g.DrawString('Vicious in Need of an Apprentice', $fDetail, [System.Drawing.Brushes]::White, 400, 84)
    $fSummary = New-Object System.Drawing.Font('Arial', 14, [System.Drawing.FontStyle]::Bold)
    $g.DrawString('Quest Summary', $fSummary, [System.Drawing.Brushes]::Black, 380, 152)
    $g.DrawString('Stump 30 / 30', $fTab, [System.Drawing.Brushes]::Black, 400, 182)

    $fBody = New-Object System.Drawing.Font('Arial', 11)
    $body = @(
        'After I told Vicious, the carpenter of Henesys,',
        'that I wanted to become his apprentice, he gave',
        'me a test. He said that woodcrafting requires',
        'one to have the proper strength and knowledge',
        'and asked me to defeat monsters near Henesys',
        'and collect Tree Branches.'
    )
    $y = 214
    foreach ($t in $body) { $g.DrawString($t, $fBody, [System.Drawing.Brushes]::Black, 380, $y); $y += 22 }
    $g.DrawString('DETAILS', $fTab, [System.Drawing.Brushes]::Black, 250, 520)
    $g.DrawString('QUEST HELPER', $fTab, [System.Drawing.Brushes]::Black, 450, 520)
    $g.DrawString('FORFEIT', $fTab, [System.Drawing.Brushes]::Black, 640, 520)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    $fTitle.Dispose(); $fTab.Dispose(); $fItem.Dispose(); $fDetail.Dispose(); $fSummary.Dispose(); $fBody.Dispose()
}

function Invoke-Benchmark([string]$image) {
    Start-Process -FilePath $exe -ArgumentList @('--benchmark',
        "--benchmark-image=$image", '--benchmark-range=balanced') `
        -WorkingDirectory $repoRoot -Wait
    $result = Get-Content (Join-Path $repoRoot 'last_run.txt') -Raw -Encoding UTF8
    if ($result.StartsWith('错误=')) { throw "基准执行失败：$result" }
    return $result
}

$cleanPath = Join-Path $workRoot 'quest-clean.png'
New-QuestScene $cleanPath
$clean = Invoke-Benchmark $cleanPath
if ($clean -notmatch '识别场景=任务') { throw '干净任务窗口未被识别为任务场景' }
if ($clean -notmatch '木匠') { throw '干净任务窗口的任务正文没有翻译' }

# Now hide everything the old logic depended on, keeping only Quest Summary.
$coveredPath = Join-Path $workRoot 'quest-shell-hidden.png'
$img = [System.Drawing.Image]::FromFile($cleanPath)
$bmp = New-Object System.Drawing.Bitmap($img.Width, $img.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.DrawImage($img, 0, 0, $img.Width, $img.Height)
$cover = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 40, 30, 50))
$g.FillRectangle($cover, 4, 4, $img.Width - 8, 26)   # QUEST caption
$g.FillRectangle($cover, 10, 36, 372, 26)            # the three tabs
$g.FillRectangle($cover, 14, 76, 340, 56)            # the left quest list
$bmp.Save($coveredPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $img.Dispose()
$cover.Dispose()

$covered = Invoke-Benchmark $coveredPath
if ($covered -match '识别场景=普通界面') {
    throw '任务标题与页签被遮挡后仍退回普通界面：Quest Summary 兜底失效'
}
if ($covered -notmatch '识别场景=任务') {
    throw "被遮挡的任务窗口场景识别异常：$([regex]::Match($covered, '识别场景=([^\r\n]+)').Groups[1].Value)"
}
if ($covered -notmatch '木匠') {
    throw '任务标题与页签被遮挡后任务正文不再翻译'
}
if ($covered -notmatch '任务概要') {
    throw '任务标题与页签被遮挡后 Quest Summary 不再翻译'
}

$source = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
foreach ($required in @(
    'private static bool HasQuestSummaryMarker(List<OcrLine> lines)',
    'bool questSurfacePresent = questShellPresent || anyShellAnchor != null ||',
    'HasQuestSummaryMarker(lines);',
    'else if (mainQuestShell || FindQuestShellAnchor(allLines) != null ||')) {
    if (-not $source.Contains($required)) { throw "任务面板兜底判据缺失：$required" }
}

Write-Output ('任务面板兜底：QUEST 标题与页签被覆盖层遮挡时，仍凭 Quest Summary 识别任务面板，' +
    '任务正文与任务概要继续翻译')
