param([string]$ExePath)
$RepoRoot=Split-Path -Parent $PSScriptRoot
if(-not $ExePath){$ExePath=Join-Path $RepoRoot 'bin/option2-fusion-r2/枫语幕.exe'}
$ErrorActionPreference='Stop'
. (Join-Path $RepoRoot 'tests/Load-TestAssembly.ps1')
$assembly=Load-TestAssembly $ExePath
$flags=[Reflection.BindingFlags]'Static,Instance,Public,NonPublic'
$type=$assembly.GetType('MapleOverlay.TranslationStore',$true)
$store=$type.GetConstructors($flags)[0].Invoke([object[]]@([string](Join-Path $RepoRoot '枫语幕词库.tsv')))
$type.GetMethod('Load',$flags).Invoke($store,@())|Out-Null
$names=$type.GetMethod('FindSkillNameMatches',$flags);$identify=$type.GetMethod('DetectSkillId',$flags)
$count=0
function Check([bool]$ok,[string]$name){if(-not $ok){throw $name};$script:count++;Write-Output "PASS: $name"}
function Matches([string]$text,[string]$id=''){return ,@($names.Invoke($store,[object[]]@($text,$id)))}
function Chinese($match){$entry=$match.GetType().GetField('Entry',$flags).GetValue($match);return [string]$entry.GetType().GetField('Chinese',$flags).GetValue($entry)}
foreach($case in @(@('Magic Guard','魔法盾'),@('Maglc Guard','魔法盾'),@('Magic Armor','魔法铠甲'),@('Magic Claw','魔法双击'),@('Recovery','恢复'),@('Magic Guard 3','魔法盾'))){
 $result=Matches $case[0];Check ($result.Count -eq 1 -and (Chinese $result[0]) -eq $case[1]) ('independent observed title: '+$case[0])
}
Check ([string]$identify.Invoke($store,@('Maglc Guard')) -eq '2001000') 'OCR spelling repair resolves unique skill identity'
Check ([string]$identify.Invoke($store,@('Magic Guard Magic Armor')) -eq '') 'multiple visible skill names cannot lend one arbitrary identity'
Check ([string]$identify.Invoke($store,@('Teleport')) -eq '') 'shared skill name does not choose an arbitrary job ID'
Check ((Matches 'Hyper Body').Count -eq 0) 'different Chinese variants of one English title require disambiguation'
$result=Matches 'Hyper Body' '1301005';Check ($result.Count -eq 1 -and (Chinese $result[0]) -eq '神圣之火') 'scoped class identity selects its own canonical title'
Check ((Matches 'Magic Totally Unrelated Sentence').Count -eq 0) 'unrelated prose cannot become a fuzzy skill title'
$overview=$type.GetMethod('GetSkillOverview',$flags).Invoke($store,@('1001'))
$english=[string]$overview.GetType().GetField('English',$flags).GetValue($overview)
$body=@($type.GetMethod('FindSkillTextMatches',$flags).Invoke($store,[object[]]@($english,'1001')))
Check ($body.Count -gt 0) 'known identity retains complete glossary-backed Recovery description'
if($count -ne 13){throw 'Unexpected dictionary count'}
Write-Output "Skill dictionary: 13/13 passed; candidate SHA256=$((Get-FileHash $ExePath -Algorithm SHA256).Hash); real OCR pixels and Windows rendering unverified."
