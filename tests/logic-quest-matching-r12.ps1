param([string]$ExePath)
$ErrorActionPreference='Stop'
$r=Split-Path -Parent $PSScriptRoot
if(-not $ExePath){$ExePath=Join-Path $r 'bin/windows-v4.0/枫语幕.exe'}
. (Join-Path $r 'tests/Load-TestAssembly.ps1')
$a=Load-TestAssembly $ExePath
$f=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$t=$a.GetType('MapleOverlay.TranslationStore',$true)
$s=$t.GetConstructors($f)[0].Invoke([object[]]@([string](Join-Path $r '枫语幕词库.tsv')))
$t.GetMethod('Load',$f).Invoke($s,@())|Out-Null
$count=0
function Check([bool]$ok,[string]$name){if(-not $ok){throw $name};$script:count++;Write-Output "PASS: $name"}
function Hits([string]$method,[string]$text){return ,@($t.GetMethod($method,$f).Invoke($s,[object[]]@($text)))}
$plural=Hits 'FindTaskNameMatches' "Caretaker's Ledgers"
Check ($plural.Count -eq 1) 'user-confirmed plural ledger title has an exact independent translation'
$entry=$plural[0].GetType().GetField('Entry',$f).GetValue($plural[0])
Check ($entry.GetType().GetField('English',$f).GetValue($entry) -eq "Caretaker's Ledgers") 'plural is not a partial singular substring'
Check ((Hits 'FindTaskNameMatches' "Caretaker's Ledger").Count -eq 1) 'prior singular wording remains available'
Check ([string]$t.GetMethod('ResolveUniqueTaskTitleId',$f).Invoke($s,[object[]]@("Caretaker's Ledgers")) -eq '') 'server wording cannot invent a canonical task ID'
$body='Lyn the Librarian asked me to recover 50 Torn Ledger Pages the fairies took, then have Chief Stan in Henesys stamp the cover.'
Check ((Hits 'FindSharedTaskBodyMatches' $body).Count -eq 1) 'complete observed source retains its full body translation'
Check ((Hits 'FindSharedTaskBodyMatches' ($body.Replace('recover','rec over'))).Count -eq 1) 'OCR split inside a word cannot suppress the complete observed paragraph'
Check ((Hits 'FindSharedTaskBodyMatches' ($body.Replace('Torn Ledger','TornLedger'))).Count -eq 1) 'OCR joined words retain the same observed paragraph'
Check ((Hits 'FindSharedTaskBodyMatches' ($body.Replace('recover','rec0ver'))).Count -eq 1) 'one aligned zero-for-letter OCR error can recover full prose'
Check ((Hits 'FindSharedTaskBodyMatches' ($body.Replace('Librarian','L1brarian'))).Count -eq 1) 'one aligned one-for-letter OCR error can recover full prose'
foreach($bad in @($body.Replace('50','40'),$body.Replace('50','500'),$body.Replace('50','-50'),$body.Replace('50','+50'),$body.Replace('recover','destroy'),$body.Replace('fairies','monsters'),$body.Substring(0,$body.Length-20))){
 Check ((Hits 'FindSharedTaskBodyMatches' $bad).Count -eq 0) 'changed quantity, different wording or missing continuation cannot borrow a full paragraph'
}
$equiv=$a.GetType('MapleOverlay.ObservedProseMatch',$true).GetMethod('Equivalent',$f)
function EqualProse([string]$x,[string]$y){return [bool]$equiv.Invoke($null,[object[]]@($x,$y))}
Check (-not (EqualProse 'Collect 1 234 items from monsters in the forest.' 'Collect 12 34 items from monsters in the forest.')) 'space repair cannot merge different numeric token sequences'
Check (-not (EqualProse 'Collect 12.5 items from monsters in the forest.' 'Collect 12 5 items from monsters in the forest.')) 'decimal format cannot become two quantities'
Check (-not (EqualProse ($body.Replace('50','5O')) $body)) 'letter O cannot manufacture an unobserved numeric zero'
Check (-not (EqualProse ($body.Replace('50',' 5 0 ')) $body)) 'split quantity cannot be silently reconstructed'
Check (-not (EqualProse ('Unknown words '+$body) $body)) 'unrelated prefix is not swallowed by complete-prose comparison'
Check (-not (EqualProse ($body+' Unknown words') $body)) 'unrelated suffix is not swallowed by complete-prose comparison'
$policy=$a.GetType('MapleOverlay.PanelContextPolicy',$true).GetMethod('ResolveTaskId',$f)
$lines=[Collections.Generic.List[string]]::new();$lines.Add('Make a Pig & Mushroom Omok Set')
$lines.Add('I brought the requested items to Casey in Henesys Park and received a Pig & Mushroom Omok Set! With this, I can play the Omok mini-game with other adventurers anywhere! I should remember to ask Casey if I want to know more about the mini-game.')
Check ([string]$policy.Invoke($null,[object[]]@('Make a Pig & Mushroom Omok Set',$lines,$s,$false,$true)) -eq '') 'unscoped full-frame task names cannot authorize a paragraph'
Check ([string]$policy.Invoke($null,[object[]]@('Make a Pig & Mushroom Omok Set',$lines,$s,$true,$true)) -eq '80005') 'measured selected-detail scope resolves its own canonical task'
$ledgerLines=[Collections.Generic.List[string]]::new();$ledgerLines.Add("Caretaker's Ledgers");$ledgerLines.Add($body)
Check ([string]$policy.Invoke($null,[object[]]@("Caretaker's Ledgers",$ledgerLines,$s,$true,$true)) -eq '') 'shared ledger prose cannot be assigned an invented identity'
$canonical='Icarus failed to create a working hang glider, and is now attempting to create a flying balloon on his never-ending quest for flight. This looks just as preposterous as his last attempt, but I don''t think I can stop him. To make the flying balloon, he needs 50 Tablecloths and 10 Alligator Skin Pouchs. Hope this works.'
$method=$t.GetMethod('FindTaskMatches',$f)
Check (@($method.Invoke($s,[object[]]@($canonical,'10318'))).Count -gt 0) 'canonical paragraph with its observed quantities remains available'
Check (@($method.Invoke($s,[object[]]@($canonical.Replace('50','40'),'10318'))).Count -eq 0) 'canonical fuzzy matcher cannot change an observed task quantity'
Check (@($method.Invoke($s,[object[]]@($canonical.Replace('failed','falled'),'10318'))).Count -gt 0) 'quantity guard preserves existing nonnumeric OCR tolerance'
Write-Output "Quest matching r12: $count checks passed; actual candidate dictionary/policy, controlled English variants rather than user OCR text."
