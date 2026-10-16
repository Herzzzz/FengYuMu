param([string]$ExePath,[switch]$Before,[string]$BeforeRepository='/workspace/FengYuMu-option2-chat-r4')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$testedRoot=if($Before){$BeforeRepository}else{$root}
if(-not $ExePath){$ExePath=Join-Path $testedRoot $(if($Before){'bin/option2-chat-r4/枫语幕.exe'}else{'bin/option2-completeness-r5/枫语幕.exe'})}
. (Join-Path $testedRoot 'tests/Load-TestAssembly.ps1')
$assembly=Load-TestAssembly $ExePath
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$type=$assembly.GetType('MapleOverlay.TranslationStore',$true)
$dict=Join-Path $testedRoot '枫语幕词库.tsv'
$dictionaryHashBefore=(Get-FileHash -LiteralPath $dict -Algorithm SHA256).Hash
function Store([string]$path){$s=$type.GetConstructors($flags)[0].Invoke([object[]]@($path));$type.GetMethod('Load',$flags).Invoke($s,@())|Out-Null;return $s}
$store=Store $dict
$count=0
function Check([bool]$ok,[string]$name){if(-not $ok){throw $name};$script:count++;Write-Output ('PASS: '+$name)}
function Field($match,[string]$name){$entry=$match.GetType().GetField('Entry',$flags).GetValue($match);return $entry.GetType().GetField($name,$flags).GetValue($entry)}
function Hits([string]$method,[string]$text,[string]$id=''){
 $args=if($method -eq 'FindTaskMatches' -or $method -eq 'FindSkillTextMatches'){[object[]]@($text,$id)}else{[object[]]@($text)}
 return ,@($type.GetMethod($method,$flags).Invoke($store,$args))
}
$caretaker=Hits 'FindTaskNameMatches' "Caretaker's Ledger"
Check ($caretaker.Count -eq 1 -and (Field $caretaker[0] 'Chinese') -eq '管理员的账簿') 'observed server ledger title has an independent translation'
$omok=Hits 'FindTaskNameMatches' "Maple World's Unique Fun: Omok"
Check ($omok.Count -eq 1 -and (Field $omok[0] 'Chinese') -eq '冒险岛世界的独特乐趣：五子棋') 'existing Omok group wording is routed as an independent title'
Check ([string]$type.GetMethod('ResolveUniqueTaskTitleId',$flags).Invoke($store,@("Caretaker's Ledger")) -eq '' -and [string]$type.GetMethod('ResolveUniqueTaskTitleId',$flags).Invoke($store,@("Maple World's Unique Fun: Omok")) -eq '') 'translated server aliases do not invent canonical quest IDs'
$body='Lyn the Librarian asked me to recover 50 Torn Ledger Pages the fairies took, then have Chief Stan in Henesys stamp the cover.'
$hit=Hits 'FindSharedTaskBodyMatches' $body
Check ($hit.Count -eq 1 -and (Field $hit[0] 'Chinese').Contains('50页') -and (Field $hit[0] 'TaskId') -eq '') 'observed ledger body preserves its actual text and quantity without borrowing another task'
Check ((Hits 'FindSharedTaskBodyMatches' ($body.Replace('50','40'))).Count -eq 0) 'unknown 40-page variant cannot borrow the 50-page wording'
Check ((Hits 'FindSharedTaskBodyMatches' ($body.Replace('50','500'))).Count -eq 0) '500 cannot match the substring 50'
$canonical='I brought the requested items to Casey in Henesys Park and received a Pig & Mushroom Omok Set! With this, I can play the Omok mini-game with other adventurers anywhere! I should remember to ask Casey if I want to know more about the mini-game.'
$hit=Hits 'FindTaskMatches' $canonical '80005'
Check ($hit.Count -gt 0 -and (Field $hit[0] 'TaskId') -eq '80005') 'existing Omok body keeps its canonical task ownership'
$heal='Recovers the HP of all party members around the area. The amount of healing depends on the number of people being healed, and undead monsters around the area will receive damage.'
Check ((Hits 'FindSkillTextMatches' $heal '2301001').Count -gt 0) 'Heal full description remains present in the actual candidate'
$mp="Absorbs the enemy's MP when attacking with Magic skills, but only once every 5 seconds. Cannot absorb if the enemy's MP is 0."
Check ((Hits 'FindSkillTextMatches' $mp '2300000').Count -gt 0) 'MP Eater full description remains present in the actual candidate'
$hit=Hits 'FindMatches' 'Torn Ledger Pages 7/50'
$items=@($hit|Where-Object{(Field $_ 'English') -eq 'Torn Ledger Pages'})
Check ($items.Count -eq 1 -and (Field $items[0] 'Chinese') -notmatch '\d') 'ledger item translation excludes the live progress counter'
Check ((Get-FileHash -LiteralPath $dict -Algorithm SHA256).Hash -eq $dictionaryHashBefore) 'loading observed wording never rewrites the user dictionary'
$temp=Join-Path $root ('.bench/observed-wording-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $temp|Out-Null
try{
 $custom=Join-Path $temp 'custom.tsv'
 [IO.File]::WriteAllText($custom,"Caretaker's Ledger`t用户自定义译名`t怀旧服-任务#custom-ledger`n",[Text.Encoding]::UTF8)
 $originalStore=$store;$store=Store $custom;$hit=Hits 'FindTaskNameMatches' "Caretaker's Ledger"
 Check ($hit.Count -eq 1 -and (Field $hit[0] 'Chinese') -eq '用户自定义译名' -and (Field $hit[0] 'TaskId') -eq 'custom-ledger') 'explicit user wording and identity override the built-in supplement'
 $store=$originalStore
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
if($count -ne 12){throw "Unexpected check count: $count"}
Write-Output "Observed quest wording: 12/12 passed; candidate SHA256=$((Get-FileHash -LiteralPath $ExePath -Algorithm SHA256).Hash); manually transcribed text is not actual OCR validation."
