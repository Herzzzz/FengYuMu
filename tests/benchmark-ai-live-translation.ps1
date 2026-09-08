param(
    [Parameter(Mandatory=$true)][string]$ModelRoot,
    [int]$MaximumHotMilliseconds = 5000
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $repoRoot '枫语幕.exe'))
$flags = [Reflection.BindingFlags]'Instance,NonPublic,Public'
$clientType = $assembly.GetType('MapleOverlay.OfflineAiClient', $true)
$constructor = $clientType.GetConstructor($flags, $null, [Type[]]@([string]), $null)
$client = $constructor.Invoke([object[]]@($ModelRoot))
$translate = $clientType.GetMethod('TranslateAsync', $flags)
$stop = $clientType.GetMethod('Stop', $flags)
$samples = @(
    'first it was the JR wraiths',
    'u just love beating up kids dont u staryni',
    'now it is the JR pepes'
)
$rows = @()
try {
    for ($index = 0; $index -lt $samples.Count; $index++) {
        $source = $samples[$index]
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $task = $translate.Invoke($client, [object[]]@($source, '英语', '中文', ''))
        $translated = [string]$task.GetAwaiter().GetResult()
        $watch.Stop()
        $rows += [pscustomobject]@{
            Source = $source
            Translation = $translated
            Milliseconds = $watch.ElapsedMilliseconds
        }
        if ([regex]::Matches($translated, '[\u3400-\u9fff]').Count -lt 2) {
            throw "8B模型没有返回有效中文：$source => $translated"
        }
        if ($index -gt 0 -and $watch.ElapsedMilliseconds -gt $MaximumHotMilliseconds) {
            throw "8B模型热态翻译超过${MaximumHotMilliseconds}ms：$($watch.ElapsedMilliseconds)ms"
        }
    }
}
finally {
    $stop.Invoke($client, @()) | Out-Null
}

$rows | Format-Table -AutoSize -Wrap
Write-Output "8B实机翻译通过：冷启动 $($rows[0].Milliseconds)ms；热态最大 $((($rows | Select-Object -Skip 1).Milliseconds | Measure-Object -Maximum).Maximum)ms"
