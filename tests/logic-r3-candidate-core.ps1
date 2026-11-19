param([string]$ExePath)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
if(-not $ExePath){$ExePath=Join-Path $repoRoot 'bin/candidate-r3/枫语幕.exe'}
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
$assembly=Load-TestAssembly $ExePath
$flags=[Reflection.BindingFlags]'Static,Public,NonPublic'
foreach($name in @('OfflineAiClient','AiInstallForm','MapleKnowledgeInitializer')) {
 if($null -ne $assembly.GetType('MapleOverlay.'+$name,$false)){throw "Removed type remains: $name"}
 Write-Output "PASS: candidate omits $name"
}
$responseType=$assembly.GetType('MapleOverlay.OnlineAiResponse',$true)
$value=[Collections.Generic.Dictionary[string,object]]::new();$value.Add('output_text','Hello')
if($responseType.GetMethod('Extract',$flags).Invoke($null,[object[]]@($value)) -ne 'Hello'){throw 'Candidate response parser returned wrong text'}
Write-Output 'PASS: actual candidate response parser returns final text'
$policy=$assembly.GetType('MapleOverlay.SceneTranslationPolicy',$true)
$opacity=[double]$policy.GetMethod('PulseOpacity',$flags).Invoke($null,[object[]]@([double]160))
if([Math]::Abs($opacity-0.055) -gt 0.000001){throw 'Candidate glow intensity differs'}
Write-Output 'PASS: actual candidate has bounded whole-client glow'
Write-Output "R3 candidate core: 5/5 passed; EXE SHA-256=$((Get-FileHash -LiteralPath $ExePath -Algorithm SHA256).Hash); no Windows entry point or HTTP executed."
