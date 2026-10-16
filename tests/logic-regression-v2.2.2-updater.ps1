param([string]$ExePath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ExePath) { $ExePath = Join-Path $repoRoot 'bin/candidate-r3/枫语幕.exe' }
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
$assembly = Load-TestAssembly $ExePath
# The former model installer was removed by explicit user instruction. Its old
# llama download URL contract is superseded by absence plus actual app transactions.
foreach($name in @('AiInstallForm','OfflineAiClient','MapleKnowledgeInitializer')) {
    if($null -ne $assembly.GetType('MapleOverlay.'+$name,$false)) {
        throw "Removed local-model component remains in candidate: $name"
    }
}
& (Join-Path $PSScriptRoot 'logic-candidate-transactions.ps1') -ExePath $ExePath
Write-Output 'Installer retirement regression passed: no local-model installer/inference/indexer; current application transaction replacement and backup exercised.'
