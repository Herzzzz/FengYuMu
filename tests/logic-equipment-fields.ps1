param([string]$ExePath)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
if([String]::IsNullOrWhiteSpace($ExePath)){$ExePath=Join-Path $repoRoot '枫语幕.exe'}
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
$assembly=Load-TestAssembly $ExePath
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$grammar=$assembly.GetType('MapleOverlay.EquipmentFieldGrammar',$true)
$layout=$assembly.GetType('MapleOverlay.EquipmentPanelLayout',$true)
$parse=$grammar.GetMethod('Parse',$flags)
$looksRow=$grammar.GetMethod('LooksLikeEquipmentRow',$flags)
$looksStructure=$grammar.GetMethod('LooksLikeEquipmentStructure',$flags)
$forRows=$assembly.GetType('MapleOverlay.EquipmentPanelTypography',$true).GetMethod('ForRows',$flags)
$buildField=$layout.GetMethod('BuildField',$flags)
$count=0
function Check([bool]$ok,[string]$name){if(-not $ok){throw $name};$script:count++;Write-Output ('PASS: '+$name)}
function Fields([string]$text,[bool]$equipment=$false){return @($parse.Invoke($null,[object[]]@($text,$equipment)))}
function FieldValue($field){$type=$field.GetType();$start=[int]$type.GetField('ValueStart',$flags).GetValue($field);$length=[int]$type.GetField('ValueLength',$flags).GetValue($field);$text=$script:currentText;if($length -le 0){return ''};return $text.Substring($start,$length)}
function Keys($fields){return @($fields|ForEach-Object{[string]$_.GetType().GetField('Key',$flags).GetValue($_)})}
function Key0($fields){$field=@($fields)[0];return [string]$field.GetType().GetField('Key',$flags).GetValue($field)}
function First($fields){return @($fields)[0]}
function Base($value){if($value -is [System.Management.Automation.PSObject]){return $value.PSObject.BaseObject};return $value}
function Plan([string]$text,[System.Drawing.RectangleF]$key,[System.Drawing.RectangleF]$value,[System.Drawing.RectangleF]$row,[System.Drawing.RectangleF]$panel,$typography){
 $script:currentText=$text;$fields=Fields $text $true
 if($fields.Count -eq 0){throw ('no field parsed: '+$text)}
 $field=Base $fields[0];$type=Base $typography
 return $buildField.Invoke($null,[object[]]@($field,$text,$key,$value,$row,$panel,$type))
}

# Requirements: every column survives even when the tooltip panel was not recognized.
$script:currentText='REQ LEV : 10  REQ STR : 0  REQ INT : 50  REQ DEX : 0  REQ LUK : 20  REQ FAME : 0'
$fields=Fields $script:currentText $false
Check ($fields.Count -eq 6) 'requirements keep all six attributes on a combined OCR row'
Check (((Keys $fields) -join ',') -eq 'level,strength,intelligence,dexterity,luck,fame') 'requirement aliases map to generic attributes'
Check ((First $fields).GetType().GetField('Chinese',$flags).GetValue((First $fields)) -eq '需要等级') 'requirement key carries the required prefix'
Check ((FieldValue $fields[2]) -eq '50') 'requirement INT keeps its exact value'

# Defences and primary attributes were previously reduced to the last field on a row.
$script:currentText='Weapon Def.: +16  Magic Def.: +18'
$fields=Fields $script:currentText $false
Check ($fields.Count -eq 2 -and ((Keys $fields) -join ',') -eq 'weapon-defense,magic-defense') 'Weapon Def and Magic Def both survive on one row'
Check ((FieldValue $fields[0]) -eq '+16' -and (FieldValue $fields[1]) -eq '+18') 'defence values keep sign and digits'
$script:currentText='INT: +1  LUK: +2  Remaining Enhancements: 7'
$fields=Fields $script:currentText $false
Check ($fields.Count -eq 3) 'INT, LUK and Remaining Enhancements all survive together'
Check (((Keys $fields) -join ',') -eq 'intelligence,luck,upgrades') 'primary/upgrade aliases are distinct'
Check ((FieldValue $fields[2]) -eq '7') 'remaining enhancements keeps the observed count'

# Attacks, critical, evasion, speed, attack speed and slots.
$script:currentText='Weapon Attack: +36 (40 -4)'
$fields=Fields $script:currentText $false
Check ($fields.Count -eq 1 -and (FieldValue $fields[0]) -eq '+36 (40 -4)') 'parenthesized attack correction is preserved exactly'
$script:currentText='Magic Attack: -5'
$fields=Fields $script:currentText $false
Check ((Key0 $fields) -eq 'magic-attack' -and (FieldValue $fields[0]) -eq '-5') 'negative magic attack keeps its sign'
$script:currentText='ATT: +12  M. ATT: +13'
$fields=Fields $script:currentText $false
Check ($fields.Count -eq 2 -and ((Keys $fields) -join ',') -eq 'weapon-attack,magic-attack') 'short ATT aliases map to attack fields'
$script:currentText='Attack Power: +9'
$fields=Fields $script:currentText $false
Check ((Key0 $fields) -eq 'weapon-attack' -and (FieldValue $fields[0]) -eq '+9') 'Attack Power is a generic weapon-attack alias'
$script:currentText='Critical Rate: +5%'
$fields=Fields $script:currentText $false
Check ((Key0 $fields) -eq 'critical-rate' -and (FieldValue $fields[0]) -eq '+5%') 'critical rate keeps percentage and sign'
$script:currentText='Crit Damage: +12%'
$fields=Fields $script:currentText $false
Check ((Key0 $fields) -eq 'critical-damage') 'Crit Damage is a critical-damage alias'
$script:currentText='Evasion: +4'
$fields=Fields $script:currentText $false
Check ((Key0 $fields) -eq 'avoidability' -and (FieldValue $fields[0]) -eq '+4') 'Evasion is a generic avoidability alias'
Check ((First $fields).GetType().GetField('Chinese',$flags).GetValue((First $fields)) -eq '回避率') 'Evasion uses the existing avoidability translation'
$script:currentText='Evasion +4'
$fields=Fields $script:currentText $false
Check ((Key0 $fields) -eq 'avoidability' -and (FieldValue $fields[0]) -eq '+4') 'partial OCR without a colon still matches Evasion'
$script:currentText='Avoidability: -3'
$fields=Fields $script:currentText $false
Check ((Key0 $fields) -eq 'avoidability' -and (FieldValue $fields[0]) -eq '-3') 'Avoidability remains a negative-capable alias'
$script:currentText='Speed: +3'
$fields=Fields $script:currentText $false
Check ((Key0 $fields) -eq 'speed' -and (FieldValue $fields[0]) -eq '+3') 'Speed keeps its exact modifier'
$script:currentText='Movement Speed: -2'
$fields=Fields $script:currentText $false
Check ((Key0 $fields) -eq 'speed' -and (FieldValue $fields[0]) -eq '-2') 'Movement Speed is a generic speed alias'
$script:currentText='Attack Speed: Fast'
$fields=Fields $script:currentText $false
Check ((Key0 $fields) -eq 'attack-speed' -and (FieldValue $fields[0]) -eq 'Fast') 'Attack Speed is parsed and its Fast value is preserved'
$script:currentText='Attack Speed: Very Slow'
$fields=Fields $script:currentText $false
Check ((Key0 $fields) -eq 'attack-speed' -and (FieldValue $fields[0]) -eq 'Very Slow') 'multi-word attack speed value is preserved'
$script:currentText='Upgrade Slots: 2'
$fields=Fields $script:currentText $false
Check ((Key0 $fields) -eq 'upgrades' -and (FieldValue $fields[0]) -eq '2') 'Upgrade Slots maps to the upgrade attribute'
$script:currentText='Upgrades Available: 0'
$fields=Fields $script:currentText $false
Check ((Key0 $fields) -eq 'upgrades' -and (FieldValue $fields[0]) -eq '0') 'Upgrades Available accepts a zero value'

# Signs, percentages, Unicode minus and OCR-confusable digits.
$script:currentText='Weapon Def.: +1O'
$fields=Fields $script:currentText $false
Check ((FieldValue $fields[0]) -eq '+1O') 'OCR-confusable digit is preserved verbatim'
$script:currentText='Magic Attack: ' + [char]0x2212 + '6'
$fields=Fields $script:currentText $false
$minusExpected=[string][char]0x2212 + '6'
Check ((FieldValue $fields[0]) -eq $minusExpected) 'Unicode minus is preserved verbatim'
$script:currentText='Critical Damage: -4.5%'
$fields=Fields $script:currentText $false
Check ((FieldValue $fields[0]) -eq '-4.5%') 'decimal negative percentage is preserved'

# Multiple equipment types use the same generic grammar.
foreach($kind in @('Hat','Overall','Shoes','Gloves','One-Handed Sword')){
 $script:currentText='Type: '+$kind+'  Weapon Def.: +12  Magic Def.: +14'
 $fields=Fields $script:currentText $false
 Check ($fields.Count -eq 2 -and ((Keys $fields) -join ',') -eq 'weapon-defense,magic-defense') ('generic fields work for '+$kind)
}

# Recognition is structural, not item-name based.
Check ([bool]$looksStructure.Invoke($null,[object[]]@('Weapon Def.: +16  Magic Def.: +18'))) 'two defences establish an equipment panel'
Check ([bool]$looksStructure.Invoke($null,[object[]]@('REQ LEV : 10  REQ STR : 0'))) 'a requirement row establishes an equipment panel'
Check ([bool]$looksStructure.Invoke($null,[object[]]@('Remaining Enhancements: 7'))) 'an upgrade row establishes an equipment panel'
Check ([bool]$looksStructure.Invoke($null,[object[]]@('Attack Speed: Fast'))) 'attack speed establishes an equipment field row'
Check ([bool]$looksStructure.Invoke($null,[object[]]@('Evasion +4'))) 'a partial Evasion row establishes equipment structure'
Check ([bool]$looksRow.Invoke($null,[object[]]@('Evasion: +4'))) 'Evasion is recognized as an equipment field row'
Check (-not [bool]$looksStructure.Invoke($null,[object[]]@('Critical Rate: +5% for 10 seconds'))) 'critical prose after a value is not an equipment panel'
Check (-not [bool]$looksRow.Invoke($null,[object[]]@('Intelligence +5 and Strength +3'))) 'ordinary prose with two stat words is not an equipment row'
Check (-not [bool]$looksRow.Invoke($null,[object[]]@('Critical thinking matters'))) 'ordinary critical prose is not an equipment row'
Check (-not [bool]$looksRow.Invoke($null,[object[]]@('Critical Rate: +5% for 10 seconds'))) 'prose after a value is not an equipment field row'
Check (-not [bool]$looksStructure.Invoke($null,[object[]]@('Intelligence is important'))) 'prose is not an equipment panel'

# Shared typography and protected-region planning.
$panel=New-Object System.Drawing.RectangleF(50,50,500,220)
$rows=New-Object 'System.Collections.Generic.List[System.Drawing.RectangleF]'
$rows.Add((New-Object System.Drawing.RectangleF(90,80,400,18)))
$rows.Add((New-Object System.Drawing.RectangleF(90,110,400,18)))
$rows.Add((New-Object System.Drawing.RectangleF(90,140,400,18)))
$rowArguments=New-Object 'object[]' 2
$rowArguments[0]=$rows.PSObject.BaseObject
$rowArguments[1]=$panel
$type=$forRows.Invoke($null,$rowArguments)
Check ($type.GetType().GetField('Family',$flags).GetValue($type) -eq 'Microsoft YaHei') 'equipment typography uses the shared FONT family'
Check ($type.GetType().GetField('Unit',$flags).GetValue($type).ToString() -eq 'Pixel') 'equipment typography uses one pixel unit'
$size=[single]$type.GetType().GetField('FieldSize',$flags).GetValue($type)
Check ($size -ge 11 -and $size -le 22) 'equipment field base size stays readable and bounded'
$font=$type.GetType().GetMethod('CreateFieldFont',$flags).Invoke($type,@())
try{
 Check ($font.Size -eq $size -and $font.Unit.ToString() -eq 'Pixel') 'painted field font keeps the planned size/unit'
}finally{$font.Dispose()}
$script:currentText='Weapon Attack: +36 (40 -4)'
$plan=Plan $script:currentText (New-Object System.Drawing.RectangleF(100,100,120,18)) (New-Object System.Drawing.RectangleF(232,100,90,18)) (New-Object System.Drawing.RectangleF(90,98,400,22)) $panel $type
Check ($plan -ne $null) 'the shared plan prepares an attack field'
Check ($plan.GetType().GetField('OriginalValue',$flags).GetValue($plan) -eq '+36 (40 -4)') 'the plan retains the complete raw attack modifier'
Check ([bool]$plan.GetType().GetProperty('ValuePixelsPreserved',$flags).GetValue($plan,$null)) 'the plan protects value pixels rather than repainting them'
Check ($plan.GetType().GetField('ValueGuardBounds',$flags).GetValue($plan).Equals((New-Object System.Drawing.RectangleF(232,100,90,18)))) 'the measured value rectangle is the protection guard'
$keyRight=[single]$plan.GetType().GetField('KeySourceBounds',$flags).GetValue($plan).Right
$guardLeft=[single]$plan.GetType().GetField('ValueGuardBounds',$flags).GetValue($plan).Left
Check ($keyRight -lt $guardLeft) 'the key layout region stays left of the protected value'
Check ([bool]$plan.GetType().GetField('PanelBounds',$flags).GetValue($plan).Contains($panel)) 'the plan keeps the measured equipment panel boundary'
Check ([object]::ReferenceEquals((Base $plan.GetType().GetField('Typography',$flags).GetValue($plan)),(Base $type))) 'all fields share the one panel typography plan'

# Merged key/value word: split the measured word before the value; the key-only
# translation never covers a digit. If no safe split exists, the exact value text
# is still preserved inside the replacement.
$script:currentText='INT+5'
$plan=Plan $script:currentText (New-Object System.Drawing.RectangleF(100,100,40,18)) (New-Object System.Drawing.RectangleF(122,100,18,18)) (New-Object System.Drawing.RectangleF(90,98,120,22)) $panel $type
Check ([bool]$plan.GetType().GetField('MergedValueSplit',$flags).GetValue($plan)) 'a merged OCR word is split before its value'
$splitRight=[single]$plan.GetType().GetField('KeySourceBounds',$flags).GetValue($plan).Right
Check ($splitRight -le 120.5) 'the split key region ends before the value'
Check ($plan.GetType().GetField('OriginalValue',$flags).GetValue($plan) -eq '+5') 'the merged value is retained in evidence'
$plan=Plan $script:currentText (New-Object System.Drawing.RectangleF(100,100,6,18)) (New-Object System.Drawing.RectangleF(102,100,18,18)) (New-Object System.Drawing.RectangleF(90,98,120,22)) $panel $type
Check ([bool]$plan.GetType().GetField('MergedValueBaked',$flags).GetValue($plan)) 'an unsplittable merged word falls back safely'
Check ($plan.GetType().GetField('Translation',$flags).GetValue($plan).Contains('+5')) 'the unsplittable fallback still retains the exact value text'
Check ($plan.GetType().GetField('Status',$flags).GetValue($plan) -eq 'preserved' -and
  $plan.GetType().GetField('PreservedReason',$flags).GetValue($plan) -eq 'merged-value-region-unsafe') 'an unsafe merged value preserves the original instead of recolouring it'
$script:currentText='Weapon Def.: +16'
$plan=Plan $script:currentText (New-Object System.Drawing.RectangleF(100,100,90,18)) ([System.Drawing.RectangleF]::Empty) (New-Object System.Drawing.RectangleF(90,98,300,22)) $panel $type
Check ([bool]$plan.GetType().GetField('PartialValue',$flags).GetValue($plan)) 'partial OCR without a value rectangle is marked partial'
$partialGuard=$plan.GetType().GetField('ValueGuardBounds',$flags).GetValue($plan)
Check (-not $partialGuard.IsEmpty) 'partial value still protects the rest of the row'

if($count -lt 45){throw "unexpected check count: $count"}
Write-Output ('Equipment fields: '+$count+' checks passed; actual built EXE grammar/plan API; synthetic rectangles; real CW pixels unverified.')
