$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$maple=[IO.File]::ReadAllText((Join-Path $root 'src/MapleOverlay.cs'))
$grammar=[IO.File]::ReadAllText((Join-Path $root 'src/EquipmentFieldGrammar.cs'))
$layout=[IO.File]::ReadAllText((Join-Path $root 'src/EquipmentPanelLayout.cs'))
$structured=[IO.File]::ReadAllText((Join-Path $root 'src/StructuredPanelOverlay.cs'))
$composition=[IO.File]::ReadAllText((Join-Path $root 'src/SimpleFusionComposition.cs'))
$build=[IO.File]::ReadAllText((Join-Path $root 'src/build.ps1'))
$count=0
function Check([bool]$ok,[string]$name){if(-not $ok){throw $name};$script:count++;Write-Output ('PASS: '+$name)}

# The two non-equipment gates deliberately remain on the pre-EQUIP vocabulary.
Check ($maple.Contains('!LooksLikeEquipmentStatText(candidate.Text)) continue;')) 'chat classification gate is unchanged'
Check ($maple.Contains('if (LooksLikeEquipmentStatText(source)) return false;')) 'prose-AI eligibility gate is unchanged'
Check (-not $maple.Contains('LooksLikeEquipmentFieldText(candidate.Text)')) 'equipment grammar is not wired into the chat gate'
Check (-not $maple.Contains('LooksLikeEquipmentFieldText(source)')) 'equipment grammar is not wired into the AI gate'

# Equipment work stays generic: no item name/id, class, player or server branches.
foreach($pattern in @('DetectItemId','GetItemOverview','Black Jester','Crimsonheart','Warrior','Magician','Thief','serverId','ServerName','playerName')){
 Check (-not ($grammar -match [regex]::Escape($pattern))) ('equipment grammar has no '+$pattern+' branch')
}
foreach($pattern in @('DetectItemId','GetItemOverview','Black Jester','Crimsonheart','Warrior','Magician','Thief')){
 Check (-not ($layout -match [regex]::Escape($pattern))) ('equipment layout has no '+$pattern+' branch')
}
Check (-not ($grammar -match 'OcrEngine|PrimaryOcr|RecognizeAsync|OnlineAi|HttpClient|WebRequest')) 'equipment grammar has no OCR or AI dependency'
Check (-not ($layout -match 'OcrEngine|PrimaryOcr|RecognizeAsync|OnlineAi|HttpClient|WebRequest')) 'equipment layout has no OCR or AI dependency'

# The shared plan is compiled into the release and the composition path consumes it.
Check ($build.Contains('EquipmentPanelLayout.cs')) 'release build includes the shared equipment plan'
Check ($composition.Contains('FitEquipmentFieldFont')) 'composition uses the shared equipment typography'
Check ($composition.Contains('equipment-paint')) 'composition records painted/preserved equipment evidence'
Check ($structured.Contains('equipment-coverage')) 'structured planning records recognized/matched/prepared equipment evidence'

Write-Output ('Equipment scope safety: '+$count+' checks passed; source-level isolation guards; runtime chat/OCR/AI paths unchanged.')
