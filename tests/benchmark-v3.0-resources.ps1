param(
    [int]$IdleCycles = 6,
    [int]$PanelCycles = 3,
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
$panelImage = Join-Path $PSScriptRoot 'fixtures\public-v3\skill-tab-triple-throw.png'
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot '.bench\v3-resource-benchmark.json'
}

function Read-Metric([string]$text, [string]$name) {
    $match = [regex]::Match($text, [regex]::Escape($name) + '=(\d+)')
    if (-not $match.Success) { throw "资源报告缺少指标：$name" }
    return [int]$match.Groups[1].Value
}

function Invoke-ResourceRun([string]$name, [string[]]$arguments) {
    $process = Start-Process -FilePath $exe -ArgumentList $arguments `
        -WorkingDirectory $repoRoot -PassThru
    $peakWorking = 0L
    $peakPrivate = 0L
    $wall = [Diagnostics.Stopwatch]::StartNew()
    while (-not $process.HasExited) {
        try {
            $process.Refresh()
            $peakWorking = [Math]::Max($peakWorking, $process.WorkingSet64)
            $peakPrivate = [Math]::Max($peakPrivate, $process.PrivateMemorySize64)
        } catch { }
        Start-Sleep -Milliseconds 100
    }
    $process.WaitForExit()
    $wall.Stop()
    $process.Refresh()
    $summary = Get-Content (Join-Path $repoRoot 'continuous_benchmark.txt') -Raw -Encoding UTF8
    return [pscustomobject]@{
        Name = $name
        WallMs = [int]$wall.ElapsedMilliseconds
        CpuMs = [int]$process.TotalProcessorTime.TotalMilliseconds
        PeakWorkingMB = [Math]::Round($peakWorking / 1MB, 1)
        PeakPrivateMB = [Math]::Round($peakPrivate / 1MB, 1)
        AverageResponseMs = Read-Metric $summary '平均响应毫秒'
        MaximumResponseMs = Read-Metric $summary '最大响应毫秒'
        AverageProbeMs = Read-Metric $summary '平均快速探测毫秒'
        MaximumProbeMs = Read-Metric $summary '最大快速探测毫秒'
        FinalPollMs = Read-Metric $summary '最终轮询毫秒'
    }
}

$idle = Invoke-ResourceRun '无面板持续扫描' @('--benchmark','--benchmark-scene-probe',
    "--benchmark-continuous-cycles=$IdleCycles",'--benchmark-range=balanced')
$panel = Invoke-ResourceRun '技能面板持续翻译' @('--benchmark','--benchmark-scene-probe',
    "--benchmark-continuous-cycles=$PanelCycles",'--benchmark-range=balanced',
    "--benchmark-image=$panelImage")

if ($idle.MaximumResponseMs -gt 350) { throw "无面板快速探测过慢：$($idle.MaximumResponseMs)ms" }
if ($panel.MaximumResponseMs -gt 2200) { throw "持续翻译面板响应过慢：$($panel.MaximumResponseMs)ms" }
if ($panel.PeakPrivateMB -gt 512) { throw "持续翻译私有内存过高：$($panel.PeakPrivateMB)MB" }

$processor = Get-CimInstance Win32_Processor | Select-Object -First 1
$computer = Get-CimInstance Win32_ComputerSystem
$os = Get-CimInstance Win32_OperatingSystem
$report = [ordered]@{
    MeasuredAt = (Get-Date).ToString('s')
    Machine = [ordered]@{
        Cpu = $processor.Name.Trim()
        PhysicalCores = [int]$processor.NumberOfCores
        LogicalProcessors = [int]$processor.NumberOfLogicalProcessors
        MemoryGB = [Math]::Round($computer.TotalPhysicalMemory / 1GB, 1)
        OperatingSystem = $os.Caption
        OsVersion = $os.Version
        Architecture = $os.OSArchitecture
    }
    Idle = $idle
    Panel = $panel
}
$directory = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$idle,$panel | Format-Table Name,AverageResponseMs,MaximumResponseMs,AverageProbeMs,
    PeakWorkingMB,PeakPrivateMB,CpuMs,WallMs -AutoSize
Write-Output "持续模式资源压测通过：$OutputPath"
