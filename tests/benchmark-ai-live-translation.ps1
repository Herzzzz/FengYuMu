param(
    [Parameter(Mandatory=$true)][string]$ModelRoot,
    [int]$MaximumHotMilliseconds = 5000
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFile((Join-Path $repoRoot '枫语幕.exe'))
$flags = [Reflection.BindingFlags]'Instance,NonPublic,Public'
$clientType = $assembly.GetType('MapleOverlay.OfflineAiClient', $true)
$chatType = $assembly.GetType('MapleOverlay.OfflineChatForm', $true)
$constructor = $clientType.GetConstructor($flags, $null, [Type[]]@([string]), $null)
$client = $constructor.Invoke([object[]]@($ModelRoot))
$translate = $clientType.GetMethod('TranslateAsync', $flags)
$stop = $clientType.GetMethod('Stop', $flags)
$knownIntent = $chatType.GetMethod('TryKnownChatIntentTranslation',
    [Reflection.BindingFlags]'Static,NonPublic,Public')
$samples = @(
    @{ Source='first it was the JR wraiths'; Required='幽灵|恶灵' },
    @{ Source='u just love beating up kids dont u staryni'; Required='小孩|孩子' },
    @{ Source='now it is the JR pepes'; Required='现在|轮到' },
    @{ Source='Anyone got stupid bronze ores?'; Glossary='Bronze Ore = 青铜母矿'; Required='青铜母矿'; Forbidden='傻瓜青铜|愚蠢青铜|\bstupid\b' },
    @{ Source='Anyone know a fix?'; Required='办法|解决' },
    @{ Source='Clicking NPC shows no dialogue window disables all controls.'; Required='NPC'; Required2='对话' },
    @{ Source='is already in a party'; Glossary='Party = 队伍'; Required='队伍|组队' },
    @{ Source='How do you whisper back someone?'; Glossary='WHISPER = 悄悄话'; Required='悄悄话|私聊'; Forbidden='悄悄话给某人|私聊给某人' }
)
$rows = @()
try {
    for ($index = 0; $index -lt $samples.Count; $index++) {
        $sample = $samples[$index]
        $source = [string]$sample.Source
        $glossary = if ($sample.ContainsKey('Glossary')) { [string]$sample.Glossary } else { '' }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $intentArgs = [object[]]@($source, '')
        if ([bool]$knownIntent.Invoke($null, $intentArgs)) {
            $translated = [string]$intentArgs[1]
        } else {
            $task = $translate.Invoke($client, [object[]]@($source, '英语', '中文', $glossary))
            $translated = [string]$task.GetAwaiter().GetResult()
        }
        $watch.Stop()
        $rows += [pscustomobject]@{
            Source = $source
            Translation = $translated
            Milliseconds = $watch.ElapsedMilliseconds
        }
        if ([regex]::Matches($translated, '[\u3400-\u9fff]').Count -lt 2) {
            throw "8B模型没有返回有效中文：$source => $translated"
        }
        if ($translated -notmatch $sample.Required -or
            ($sample.ContainsKey('Required2') -and $translated -notmatch $sample.Required2)) {
            throw "8B模型聊天自然度/术语检查失败：$source => $translated"
        }
        if ($sample.ContainsKey('Forbidden') -and $translated -match $sample.Forbidden) {
            throw "8B模型仍有逐词硬译：$source => $translated"
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
Write-Output "8B实机翻译通过：8条跨场景聊天；冷启动 $($rows[0].Milliseconds)ms；热态最大 $((($rows | Select-Object -Skip 1).Milliseconds | Measure-Object -Maximum).Maximum)ms"
