$ErrorActionPreference='Stop'
$r=Split-Path -Parent $PSScriptRoot
$work='/workspace/work/release-v4.0'
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
$a=Load-TestAssembly (Join-Path $r 'bin/windows-v4.0/枫语幕.exe')
$f=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance';$t=$a.GetType('MapleOverlay.TranslationStore',$true)
$store=$t.GetConstructors($f)[0].Invoke([object[]]@([string](Join-Path $r '枫语幕词库.tsv')));$t.GetMethod('Load',$f).Invoke($store,@())|Out-Null
$count=0
function Check([bool]$ok,[string]$name){if(-not $ok){throw $name};$script:count++;Write-Output "PASS: $name"}
function Field($object,[string]$name){$object.GetType().GetField($name,$f).GetValue($object)}
$effectMethod=$t.GetMethod('TryTranslateSkillEffect',$f)
$numbers=$a.GetType('MapleOverlay.TranslationValueSafety',$true).GetMethod('PreservesNumbers',$f)
$effects=@($t.GetField('skillTextEntries',$f).GetValue($store))
$levelKeys=[Collections.Generic.HashSet[string]]::new();foreach($raw in Get-Content -LiteralPath (Join-Path $r '枫语幕词库.tsv') -Encoding UTF8){$row=$raw.Split("`t");if($row.Count -ge 4 -and $row[3].StartsWith('Henesys.gg等级')){$levelKeys.Add($row[0]+"`t"+$row[2])|Out-Null}}
$tested=0;$effectIds=[Collections.Generic.HashSet[string]]::new();$untranslated=[Collections.Generic.List[object]]::new();$valueMismatch=[Collections.Generic.List[object]]::new()
foreach($entry in $effects){
 $english=[string](Field $entry 'English');if(-not $levelKeys.Contains($english+"`t"+[string](Field $entry 'Category'))){continue}
 $id=([string](Field $entry 'Category')).Split('#')[-1];$effectIds.Add($id)|Out-Null
 $args=@($english,$id,'');$translated=[bool]$effectMethod.Invoke($store,$args);$tested++
 if(-not $translated){$untranslated.Add(@{id=$id;english=$english})}
 elseif(-not [bool]$numbers.Invoke($null,@(([regex]::Replace($english,'([+-])\s+(?=\d)','$1')),([regex]::Replace([string]$args[2],'([+-])\s+(?=\d)','$1'))))){$valueMismatch.Add(@{id=$id;english=$english;chinese=[string]$args[2]})}
}
$canonical=[ordered]@{tested=$tested;skillIds=@($effectIds|Sort-Object);untranslated=@($untranslated);valueMismatch=@($valueMismatch)}
$canonical|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $work 'skill-corpus-results.json') -Encoding utf8
Check ($tested -ge 3804) 'corpus replay covers all 3804 exported numeric level effects, not selected skills'
Check ($untranslated.Count -eq 0) 'every known canonical numeric skill effect has a complete translation path'
Check ($valueMismatch.Count -eq 0) 'every canonical effect retains all signs, percentages and quantities in source order'
# Overview descriptions may reorder facts or spell a number in words. Exercise
# their complete dictionary path separately; do not apply numeric-slot order
# rules to ordinary prose, or pretend absent level rows were tested.
$overviewIds=[Collections.Generic.HashSet[string]]::new();$overviewFailures=0;$overviewFailedEntries=[Collections.Generic.List[object]]::new()
foreach($entry in $effects){$english=[string](Field $entry 'English');if($levelKeys.Contains($english+"`t"+[string](Field $entry 'Category'))){continue};$id=([string](Field $entry 'Category')).Split('#')[-1];$args=@($english,$id,'');if([bool]$effectMethod.Invoke($store,$args)){$overviewIds.Add($id)|Out-Null}else{$overviewFailures++;$overviewFailedEntries.Add(@{id=$id;english=$english})}}
$overviewFailedEntries|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $work 'overview-failures.json') -Encoding utf8
$metadata=Get-Content -LiteralPath (Join-Path $r 'data/henesys-skills-zh.json') -Raw -Encoding UTF8|ConvertFrom-Json
$jobs=@($metadata.skills|Group-Object jobName|ForEach-Object{
 $numeric=@($_.Group|Where-Object{$effectIds.Contains([string]$_.id)}).Count
 [ordered]@{job=$_.Name;skills=$_.Count;numericSkillsReplayed=$numeric;overviewSkillsReplayed=@($_.Group|Where-Object{$overviewIds.Contains([string]$_.id)}).Count}
})
$jobs|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $work 'job-coverage.json') -Encoding utf8
Check ($jobs.Count -ge 30 -and @($jobs|Where-Object{$_.overviewSkillsReplayed -eq 0}).Count -eq 0 -and $overviewFailures -eq 0) 'every job group has complete overview replay; level effects are checked only where source level rows exist'
$objectiveMethod=$t.GetMethod('ResolveObjectiveName',$f)
$objectives=@($t.GetField('objectiveExact',$f).GetValue($store).GetEnumerator());$ambiguous=$t.GetField('objectiveAmbiguous',$f).GetValue($store)
$objectiveTested=0;$objectiveFailures=0
foreach($pair in $objectives){if($ambiguous.Contains($pair.Key)){continue};$english=[string](Field $pair.Value 'English');$entry=$objectiveMethod.Invoke($store,@($english));$objectiveTested++;if($null -eq $entry -or (Field $entry 'Chinese') -ne (Field $pair.Value 'Chinese')){$objectiveFailures++}}
Check ($objectiveTested -ge 1000 -and $objectiveFailures -eq 0) 'all unambiguous item/equipment/monster names resolve through the same objective-name index'
$proof=Get-Content -LiteralPath (Join-Path $r 'data/journal-state-alignment.json') -Raw -Encoding UTF8|ConvertFrom-Json
Check ($proof.entries.Count -eq 173) 'whole GCW journal batch has state-aligned dictionary data'
$aligned=[Collections.Generic.Dictionary[string,Collections.Generic.List[string]]]::new([StringComparer]::Ordinal)
foreach($raw in Get-Content -LiteralPath (Join-Path $r '枫语幕词库.tsv') -Encoding UTF8){$row=$raw.Split("`t");if($row.Count -lt 3){continue};$key=$row[0]+"`t"+$row[2];if(-not $aligned.ContainsKey($key)){$aligned.Add($key,[Collections.Generic.List[string]]::new())};$aligned[$key].Add($row[1])}
$proofFailures=0
foreach($entry in $proof.entries){$key=$entry.english+"`t"+$entry.category;if(-not $aligned.ContainsKey($key) -or $aligned[$key].Count -ne 1 -or $aligned[$key][0] -ne $entry.after){$proofFailures++}}
Check ($proofFailures -eq 0) 'every aligned journal record is the actual shipped value, without runtime ID exceptions'
$runtime=@('QuestJournalCatalog.cs','QuestJournalOverlay.cs','EquipmentFieldGrammar.cs','NumericTextTemplates.cs','StructuredPanelOverlay.cs','QuestObjectiveSurface.cs','SkillEffectVocabulary.cs','SkillSectionOverlay.cs','CharacterFieldOverlay.cs','ChatColorEvidence.cs')|ForEach-Object{[IO.File]::ReadAllText((Join-Path $r ('src/'+$_)))}
Check (($runtime -join "`n") -notmatch "Maya|Caretaker|Sabitrama|Omok|Icarus|2301002|10116") 'new runtime rules contain no named task/item/skill or reported-ID exception'
Check (-not (Test-Path -LiteralPath (Join-Path $r 'src/GlossaryJournalCorrections.cs'))) 'old per-task runtime correction table was removed after whole-batch data alignment'
$sync=[Management.Automation.Language.Parser]::ParseFile((Join-Path $r 'tools/sync-henesys-skills.ps1'),[ref]$null,[ref]$null)
$expand=$sync.FindAll({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Expand-NumberTemplate'},$true)[0]
Invoke-Expression $expand.Extent.Text
Check ((Expand-NumberTemplate 'Max HP +10%' '最大HP +{n1}%') -eq '最大HP +10%') 'one numeric match is an array: a two-digit value never truncates to its first character'
Check ((Expand-NumberTemplate 'MP -35; Duration 100 sec' 'MP -{n1}；持续{n2}秒') -eq 'MP -35；持续100秒') 'multiple numeric slots are also expanded intact'
$report=[ordered]@{skills=$canonical;jobGroups=$jobs;objectiveNamesTested=$objectiveTested;objectiveFailures=$objectiveFailures;ambiguousObjectiveNames=$ambiguous.Count;alignedJournalRows=$proof.entries.Count;nativeWindows='UNVERIFIED'}
$report|ConvertTo-Json -Depth 9|Set-Content -LiteralPath (Join-Path $work 'corpus-coverage.json') -Encoding utf8
Write-Output "Corpus coverage: $count checks passed; $tested numeric skill records, $objectiveTested unambiguous nouns, $($jobs.Count) job groups, 173 journal rows; dictionary replay is not native OCR validation."
