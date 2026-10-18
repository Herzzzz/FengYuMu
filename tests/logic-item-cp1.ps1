$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'tests/Load-TestAssembly.ps1')
$assembly=Load-TestAssembly (Join-Path $root '枫语幕.exe')
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$storeType=$assembly.GetType('MapleOverlay.TranslationStore',$true)
$formType=$assembly.GetType('MapleOverlay.OverlayForm',$true)
$lineType=$assembly.GetType('MapleOverlay.OcrLine',$true)
$wordType=$assembly.GetType('MapleOverlay.OcrWord',$true)
$resultType=$assembly.GetType('MapleOverlay.OcrResult',$true)
$scopeType=$assembly.GetType('MapleOverlay.LabelBuildScope',$true)
$store=$storeType.GetConstructors($flags)[0].Invoke([object[]]@([string](Join-Path $root '枫语幕词库.tsv')))
$storeType.GetMethod('Load',$flags).Invoke($store,@())|Out-Null
$formCtor=@($formType.GetConstructors($flags)|Where-Object{$_.GetParameters().Count -eq 3})[0]
$form=$formCtor.Invoke([object[]]@($true,$null,$store))
$screen=New-Object Drawing.Rectangle 0,0,1920,1080
$form.Bounds=$screen
$formType.GetField('captureBounds',$flags).SetValue($form,$screen)
$formType.GetField('gameBounds',$flags).SetValue($form,$screen)
$formType.GetField('imageTestSize',$flags).SetValue($form,(New-Object Drawing.Size 1920,1080))
$count=0
function Check([bool]$ok,[string]$name){if(-not $ok){throw ('FAIL: '+$name)};$script:count++;Write-Output ('PASS: '+$name)}
function MakeLine([string]$text,[float]$x,[float]$y){
 $line=[Activator]::CreateInstance($lineType,$true)
 $lineType.GetField('Text',$flags).SetValue($line,$text)
 $words=$lineType.GetField('Words',$flags).GetValue($line);$cx=$x
 foreach($token in ($text -split ' ')){if(!$token){continue};$word=[Activator]::CreateInstance($wordType,$true)
  $wordType.GetField('Text',$flags).SetValue($word,$token);$w=[Math]::Max(5.0,[float]($token.Length*7))
  $wordType.GetField('BoundingRect',$flags).SetValue($word,(New-Object Drawing.RectangleF $cx,$y,$w,16));$words.Add($word);$cx+=$w+4}
 return $line
}
function MakeResult([string[]]$texts){
 $result=[Activator]::CreateInstance($resultType,$true);$lines=$resultType.GetField('Lines',$flags).GetValue($result);$all=@();$row=0
 foreach($text in $texts){$lines.Add((MakeLine $text 700 (260+22*$row)));$all+=$text;$row++}
 $resultType.GetField('Text',$flags).SetValue($result,($all -join "`n"));return $result
}
function Build([string[]]$texts){
 $prepared=New-Object Drawing.Bitmap 1920,1080
 try{$invokeArgs=[object[]]::new(4);$invokeArgs[0]=MakeResult $texts;$invokeArgs[1]=[float]1;$invokeArgs[2]=[Drawing.Bitmap]$prepared;$invokeArgs[3]=[Enum]::Parse($scopeType,'Discovery')
  return $formType.GetMethod('BuildLabels',$flags).Invoke($form,$invokeArgs)}finally{$prepared.Dispose()}
}
function ItemLabels($labels){return @($labels|Where-Object{$null-ne $_ -and $null-ne $_.GetType().GetField('ItemPanel',$flags).GetValue($_)})}
function Texts($labels){return @($labels|Where-Object{$null-ne $_}|ForEach-Object{$_.GetType().GetField('Text',$flags).GetValue($_)})}

$valid=Build @('Returns you to Perion.')
Check ((Texts $valid)-contains '返回勇士部落。') 'trusted complete body-only item is planned'
Check ((ItemLabels $valid).Count -eq 1) 'trusted body-only item has one measured owner'
$quest=Build @('Quest Summary','Returns you to Perion.')
Check (-not ((Texts $quest)-contains '返回勇士部落。')) 'quest summary cannot borrow item body'
$skill=Build @('Current Level : 1','Next Level : 2','Returns you to Perion.')
Check (-not ((Texts $skill)-contains '返回勇士部落。')) 'skill panel cannot borrow item body'
$chat=Build @('Player: Returns you to Perion.')
Check ((ItemLabels $chat).Count -eq 0) 'player chat cannot become item body'
$truncated=Build @('Restores 100 H')
Check ((ItemLabels $truncated).Count -eq 0) 'truncated visible tail remains unowned and unpainted'
$bare=Build @('Red Potion')
Check ((ItemLabels $bare).Count -eq 0) 'bare inventory name is not claimed as tooltip'
$adjacent=Build @('Red Potion','Blue Potion','Restores 100 HP.')
Check ((ItemLabels $adjacent).Count -eq 0) 'adjacent item names preserve ambiguous local block'
if($count-ne 8){throw "Unexpected check count: $count"}
Write-Output "ITEM-001 CP1 smoke: $count/$count passed; real game/native OCR UNVERIFIED."
[Environment]::Exit(0)
