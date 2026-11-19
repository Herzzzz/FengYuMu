$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
$reportPath = Join-Path $repoRoot 'chat_style_benchmark.txt'
if (-not (Test-Path -LiteralPath $exe)) { throw "找不到构建产物：$exe" }
if (-not (Get-Command Start-ProductExe -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Start-ProductExe.ps1')
}

$script:checks = 0
function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++
    Write-Host ("PASS: {0}" -f $message)
}

function Invoke-ChatStyleBenchmark([string]$image, [string]$name) {
    if (Test-Path -LiteralPath $reportPath) { Remove-Item -LiteralPath $reportPath -Force }
    $process = Start-ProductExe -FilePath $exe `
        -ArgumentList ('--chat-style-benchmark=' + $image) `
        -WorkingDirectory $repoRoot -PassThru
    try {
        if (-not $process.WaitForExit(90000)) {
            $process.Kill()
            throw "$name 聊天样式基准超时"
        }
        if ($process.ExitCode -ne 0) { throw "$name 聊天样式基准退出码 $($process.ExitCode)" }
    } finally { $process.Dispose() }
    if (-not (Test-Path -LiteralPath $reportPath)) { throw "$name 未生成 chat_style_benchmark.txt" }
    return (Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8)
}

$pink = Join-Path $repoRoot 'tests/fixtures/chat-pink-super-megaphone-video.png'
$blue = Join-Path $repoRoot 'tests/fixtures/chat-blue-megaphone-video.png'

$reports = @{}
foreach ($case in @(
    @{ name = 'pink super megaphone'; path = $pink },
    @{ name = 'blue megaphone'; path = $blue })) {
    $report = Invoke-ChatStyleBenchmark $case.path $case.name
    $reports[$case.name] = $report
    $elapsed = [regex]::Match($report, 'elapsed_ms=(\d+)')
    Assert-True ($elapsed.Success -and [int]$elapsed.Groups[1].Value -le 2000) ("{0}: elapsed_ms {1} <= 2000" -f $case.name, $elapsed.Groups[1].Value)
    Assert-True ($report -notmatch 'ERROR') ("{0}: report has no capture error" -f $case.name)
    Write-Host ("  report: {0}" -f (($report -split "`r?`n")[0]))
}

$pinkReport = $reports['pink super megaphone']
$pinkBandRows = [regex]::Matches($pinkReport, '(?m)^BAND/SuperMegaphone/Megaphone\b').Count
$pinkBlueRows = [regex]::Matches($pinkReport, '(?m)^BAND/Megaphone/Megaphone\b').Count
Assert-True ($pinkBandRows -ge 3) 'pink fixture: three pink super-megaphone rows carry a band'
Assert-True ($pinkBlueRows -ge 1) 'pink fixture: adjacent blue Kena row carries a band'
$pinkParsedSuper = [regex]::Matches($pinkReport,
    '(?m)^PARSED_STYLE/SuperMegaphone/Megaphone\b').Count
$pinkParsedBlue = [regex]::Matches($pinkReport,
    '(?m)^PARSED_STYLE/Megaphone/Megaphone\b').Count
Assert-True ($pinkParsedSuper -ge 3) 'pink fixture: parsed rows retain three SuperMegaphone sources'
Assert-True ($pinkParsedBlue -ge 1) 'pink fixture: parsed blue row retains Megaphone source'
foreach ($speaker in @('Toph', 'BeelsFad', 'Kena')) {
    $bad = [regex]::Matches($pinkReport,
        '(?m)^PARSED_STYLE/(?!SuperMegaphone/Megaphone|Megaphone/Megaphone)\S+\s+\|\s+' +
        [regex]::Escape($speaker) + ':').Count
    Assert-True ($bad -eq 0) ("pink fixture: {0} is not parsed into a personal channel" -f $speaker)
}

$blueReport = $reports['blue megaphone']
$blueBandRows = [regex]::Matches($blueReport, '(?m)^BAND/Megaphone/Megaphone\b').Count
Assert-True ($blueBandRows -ge 1) 'blue fixture: blue megaphone row carries a band'
$blueParsed = [regex]::Matches($blueReport,
    '(?m)^PARSED_STYLE/Megaphone/Megaphone\b').Count
Assert-True ($blueParsed -ge 1) 'blue fixture: parsed row retains the Megaphone source'
$blueBad = [regex]::Matches($blueReport,
    '(?m)^PARSED_STYLE/(?!Megaphone/Megaphone|SuperMegaphone/Megaphone)\S+\s+\|\s+(?:Jae|Joey):').Count
Assert-True ($blueBad -eq 0) 'blue fixture: megaphone text is not parsed into a personal channel'

Write-Host ("Chat megaphone OCR regression: {0} checks passed; Windows OCR read the existing blue and pink fixtures without new OCR/AI rounds and preserved the sampled source." -f $script:checks)
