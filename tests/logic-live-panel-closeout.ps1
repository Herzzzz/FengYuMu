param([string]$ExePath='', [switch]$ProbeOnly)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$root=Split-Path -Parent $PSScriptRoot
if(!$ExePath){$ExePath=@(Get-ChildItem -LiteralPath $root -Filter *.exe -File)[0].FullName}
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
$a=Load-TestAssembly $ExePath
$f=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
function T($name){$a.GetType('MapleOverlay.'+$name,$true)}
function N($name){[Activator]::CreateInstance((T $name),$true)}
function S($o,$name,$value){$o.GetType().GetField($name,$f).SetValue($o,$value)}
function G($o,$name){$o.GetType().GetField($name,$f).GetValue($o)}
function Call($o,$name,[object[]]$args){
 try {$o.GetType().GetMethod($name,$f).Invoke($o,$args)}
 catch {if($_.Exception.InnerException){throw $_.Exception.InnerException};throw}
}
$out=Join-Path $root 'panel-test-output'
New-Item -ItemType Directory -Force $out | Out-Null
$storeType=T 'TranslationStore'
$dict=@(Get-ChildItem -LiteralPath $root -Filter *.tsv | Where-Object {$_.Name -notmatch '^(基础|任务)'})[0].FullName
$store=$storeType.GetConstructors($f)[0].Invoke([object[]]@($dict))
[void](Call $store 'Load' @())
$ctor=@((T 'OverlayForm').GetConstructors($f) | Where-Object {$_.GetParameters().Count -eq 3})[0]
$form=$ctor.Invoke([object[]]@($true,$null,$store))
$form.Bounds=[Drawing.Rectangle]::new(0,0,1200,850)
S $form 'imageTestSize' ([Drawing.Size]::new(1200,850))
S $form 'gameBounds' ([Drawing.Rectangle]::new(0,0,1200,850))
S $form 'captureBounds' ([Drawing.Rectangle]::new(0,0,1200,850))
S $form 'recognitionGeneration' 1
$script:report=@()
$script:checks=0
function Check($ok,$name){
 if(!$ok){if($ProbeOnly){Write-Output "OBSERVED FAILURE: $name"}else{throw "FAIL: $name"}}
 else {$script:checks++;Write-Output "PASS: $name"}
}
function Frame {
 $script:b=[Drawing.Bitmap]::new(1200,850)
 $script:gr=[Drawing.Graphics]::FromImage($b)
 $gr.Clear([Drawing.Color]::FromArgb(46,62,38))
 $script:ocr=N 'OcrResult'
}
function Fill($x,$y,$w,$h,$color){$brush=[Drawing.SolidBrush]::new($color);$gr.FillRectangle($brush,$x,$y,$w,$h);$brush.Dispose()}
function Row([string]$text,[single]$x,[single]$y,[single]$size=14,$ink=[Drawing.Color]::White){
 $line=N 'OcrLine';S $line 'Text' $text
 $font=[Drawing.Font]::new('Arial',$size,[Drawing.FontStyle]::Regular,[Drawing.GraphicsUnit]::Pixel)
 $format=[Drawing.StringFormat]::GenericTypographic
 $xx=$x
 foreach($part in $text.Split([char]' ')){
  if(!$part){continue}
  $width=[Math]::Max(2,$gr.MeasureString($part,$font,1000,$format).Width)
  $height=[single][Math]::Ceiling($font.GetHeight($gr))
  $word=N 'OcrWord';S $word 'Text' $part
  S $word 'BoundingRect' ([Drawing.RectangleF]::new($xx,$y,$width,$height))
  [void]$line.Words.Add($word)
  $brush=[Drawing.SolidBrush]::new($ink);$gr.DrawString($part,$font,$brush,$xx,$y,$format);$brush.Dispose()
  $xx+=$width+$size*.28
 }
 $font.Dispose();[void]$ocr.Lines.Add($line)
 return $line
}
function Paint($name){
 $gr.Dispose();S $ocr 'Text' (($ocr.Lines | ForEach-Object {$_.Text}) -join "`n")
 $b.Save((Join-Path $out ($name+'-before.png')))
 (G $form 'captureTextRows').Clear()
 $scope=[Enum]::Parse((T 'LabelBuildScope'),'Discovery')
 $labels=Call $form 'BuildLabels' ([object[]]@($ocr,[single]1,$b,$scope))
 # Reflection returns the List as one object; retain it for the production compositor.
 $listType=[Collections.Generic.List``1].MakeGenericType((T 'OverlayLabel'))
 $list=[Activator]::CreateInstance($listType)
 foreach($label in @($labels)){[void]$list.Add($label)}
 $task=Call $form 'PrepareSimpleFusionAsync' ([object[]]@($b,[Drawing.Rectangle]::new(0,0,1200,850),$list))
 while(!$task.IsCompleted){[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 5}
 [void]$task.GetAwaiter().GetResult()
 $layer=G $form 'simpleLayer';if($layer){$layer.Save((Join-Path $out ($name+'-after.png')))}
 $ready=G $form 'simpleReady'
 $rows=@()
 foreach($label in $list){
  $row=[ordered]@{text=(G $label 'Text');role=(G $label 'SemanticRole');source=(G $label 'MatchSource');ready=$ready.Contains($label);reason=(G $label 'FusionFailureReason');bounds=(G $label 'SourceBounds').ToString()}
  $rows+=$row
 }
 $rows | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $out ($name+'.json'))
 $script:report+=@{case=$name;planned=$list.Count;ready=$ready.Count;labels=$rows}
 Write-Host ($name+' planned='+$list.Count+' ready='+$ready.Count)
 $b.Dispose()
 return $rows
}
try {
 Frame
 Fill 30 25 280 590 ([Drawing.Color]::Ivory)
 [void](Row 'SKILL INVENTORY' 45 32 14 ([Drawing.Color]::Black))
 [void](Row 'Holy Magic Basics' 115 105 16 ([Drawing.Color]::Black))
 $names=@('MP Eater','Teleport','Heal','Invincible','Bless','Holy Arrow')
 for($i=0;$i -lt $names.Count;$i++){
  [void](Row $names[$i] 125 (160+$i*65) 15 ([Drawing.Color]::Black))
  [void](Row '3' 125 (182+$i*65) 13 ([Drawing.Color]::Black))
 }
 Fill 235 265 660 340 ([Drawing.Color]::FromArgb(42,44,101))
 [void](Row 'Teleport' 251 279 19)
 [void](Row '[Master Level: 20]' 355 323)
 [void](Row 'Use arrow keys to teleport a certain distance in the same map.' 355 350)
 [void](Row '[Current Level: 1]' 252 450)
 [void](Row 'MP -55; Teleport, with 110 in distance' 252 474)
 [void](Row '[Next Level: 2]' 252 526)
 [void](Row 'MP -53; Teleport, with 112 in distance' 252 550)
 $r=Paint 'skill-adjacent'
 foreach($text in @('魔力吸收','群体治愈','神之保护','圣箭术')){Check (@($r | Where-Object {$_.ready -and $_.text -eq $text}).Count -gt 0) "list name drawn: $text"}
 Check (@($r | Where-Object {$_.ready -and $_.role -eq 'skill-current-body' -and $_.text -match '110'}).Count -eq 1) 'current level effect drawn with its own distance'
 Check (@($r | Where-Object {$_.ready -and $_.role -eq 'skill-next-body' -and $_.text -match '112'}).Count -eq 1) 'next level effect drawn with its own distance'
 Frame
 [void](Row 'CHARACTER STAT' 40 22 14)
 $y=62;$labels=@('NAME','JOB','LEVEL','HP','MP','EXP.','FAME')
 for($i=0;$i -lt 7;$i++){
  $h=25;if($i -eq 1){$h=44}
  Fill 40 $y 78 $h ([Drawing.Color]::FromArgb(202,77,131))
  [void](Row $labels[$i] 45 ($y+3) 12)
  [void](Row '123' 130 ($y+3) 12)
  $y+=$h+4
 }
 for($i=0;$i -lt 4;$i++){
  Fill 40 ($y+$i*29) 78 25 ([Drawing.Color]::FromArgb(178,193,70))
  [void](Row (@('STR','DEX','INT','LUK')[$i]) 45 ($y+$i*29+3) 12)
 }
 for($i=0;$i -lt 10;$i++){
  Fill 325 (115+$i*29) 112 25 ([Drawing.Color]::FromArgb(88,138,181))
  [void](Row (@('ATTACK','WEAPON DEF.','MAGIC','MAGIC DEF.','ACCURACY','EVASION','CRIT. RATE','CRIT. DAMAGE','SPEED','JUMP')[$i]) 330 (118+$i*29) 12)
  [void](Row '+15 (20 -5)' 450 (118+$i*29) 12 ([Drawing.Color]::Blue))
 }
 $r=Paint 'character-sheet'
 Check (@($r | Where-Object {$_.ready -and $_.role -eq 'character-field'}).Count -eq 21) 'complete character sheet: all 21 field cells drawn'
 Frame
 Fill 250 100 550 530 ([Drawing.Color]::FromArgb(42,44,101))
 [void](Row 'Equipment' 270 120 19)
 $rows=@('REQ LEV : 35','REQ STR : 0','REQ DEX : 0','REQ INT : 60','REQ LUK : 25','REQ FAM : 0','LUK : +3','MaxHP : +5','Weapon Def. : +18','Magic Def. : +21 (20 +1)','Remaining Enhancements : 7','Critical Rate : +2%')
 for($i=0;$i -lt $rows.Count;$i++){[void](Row $rows[$i] 270 (180+$i*31) 14)}
 $r=Paint 'equipment-fields'
 Check (@($r | Where-Object {$_.ready -and $_.role -eq 'item-field'}).Count -eq $rows.Count) 'equipment: all requirement, HP and combat keys drawn'
 $report | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 (Join-Path $out 'summary.json')
 Write-Output "Native production pipeline: $checks checks passed; controlled measured OCR input, not live-game OCR."
} finally {$form.Dispose()}
