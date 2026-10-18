$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'tests/Load-TestAssembly.ps1')
$assembly=Load-TestAssembly (Join-Path $root '枫语幕.exe')
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$storeType=$assembly.GetType('MapleOverlay.TranslationStore',$true)
$semType=$assembly.GetType('MapleOverlay.ItemTextSemantics',$true)
$formType=$assembly.GetType('MapleOverlay.OverlayForm',$true)
$store=$storeType.GetConstructors($flags)[0].Invoke([object[]]@([string](Join-Path $root '枫语幕词库.tsv')))
$storeType.GetMethod('Load',$flags).Invoke($store,@())|Out-Null
$inspect=@($semType.GetMethods($flags)|Where-Object{$_.Name -eq 'Inspect' -and $_.GetParameters().Count -eq 2})[0]
$bodyMethod=@($semType.GetMethods($flags)|Where-Object{$_.Name -eq 'TranslateVisibleBody'})[0]
$normalize=$storeType.GetMethod('Normalize',$flags)
function N([string]$text){return [string]$normalize.Invoke($null,[object[]]@($text))}
function Audit([string]$en,[string]$zh){return $inspect.Invoke($null,[object[]]@($en,$zh))}
function Body([string]$text,[string]$id=''){return $bodyMethod.Invoke($null,[object[]]@($store,$text,$id))}
function F($object,[string]$name){return $object.GetType().GetField($name,$flags).GetValue($object)}
function Status($audit){return [string](F $audit 'Status')}
function Counts([string]$en,[string]$zh){$a=Audit $en $zh;return ((Status $a)+'/'+[string](F $a 'Reason'))}
$count=0
function Check([bool]$ok,[string]$name){if(-not $ok){throw ('FAIL: '+$name)};$script:count++;Write-Output ('PASS: '+$name)}
function Equal([object]$left,[object]$right,[string]$name){$ls=[string]$left;$rs=[string]$right;if($ls -ne $rs){Write-Output ('DEBUG left=['+$ls+'] right=['+$rs+'] ltype='+$left.GetType().FullName+' rtype='+$right.GetType().FullName)};Check ($ls -eq $rs) ($name+' ['+$ls+']')}

# ---- shared semantic guard: grammar, numbers, effects and limits ----
Equal 'Trusted' (Status (Audit 'A potion made out of red herbs. Recovers 100 HP.' '由红色草药制成的药水。恢复100 HP。')) 'Recovers 100 HP pair is trusted'
Equal 'Trusted' (Status (Audit 'A potion made out of red herbs. Restores 100 HP.' '由红色草药制成的药水。恢复100 HP。')) 'Restores/Recovers spelling variant keeps complete semantics'
Equal 'Trusted' (Status (Audit 'RECOVERS   100HP.' '恢复100 HP。')) 'case, punctuation and whitespace do not change the verified pair'
Equal 'Trusted' (Status (Audit 'Restores 40% of HP and MP.' '恢复40%的HP和MP。')) 'percent shared HP/MP pair is trusted'
Equal 'Trusted' (Status (Audit 'Recovers 100 for both HP and MP.' '恢复100 HP和100 MP。')) 'shared fixed quantity expands to both attributes'
Equal 'Trusted' (Status (Audit 'Attack Power +10 for 10 min.' '10分钟内攻击力 +10。')) 'positive value and duration bind together'
Equal 'Trusted' (Status (Audit 'Accuracy -5.5 for 5 min.' '5分钟内命中率 -5.5。')) 'negative decimal value and duration bind together'
Equal 'Trusted' (Status (Audit 'A Meso Pouch containing 10000 Mesos.' '装有10000金币的金币袋。')) 'price/quantity is isolated from effect values'
Equal 'Trusted' (Status (Audit 'Returns you to the nearest town.' '返回最近的城镇。')) 'nearest-town return is trusted'
$perionEntry=@($storeType.GetMethod('GetItemDescriptionsById',$flags).Invoke($store,[object[]]@('2030003')))[0]
Check ([string](F $perionEntry 'ItemStatus') -eq 'Trusted') 'Perion return is trusted without inventing a map ID'
Equal 'Conflict' (Status (Audit 'Recovers 100 HP and 50 MP.' '恢复50 HP和100 MP。')) 'swapped HP/MP values are rejected'
Equal 'Conflict' (Status (Audit 'Recovers 100 HP.' '恢复100 MP。')) 'asymmetric HP->MP rewrite is rejected'
Equal 'Conflict' (Status (Audit 'Restores 40% of HP.' '恢复40 HP。')) 'percent versus fixed value is rejected'
Check ((Status (Audit 'Recovers 100 HP.' '恢复100 HP。额外奖励。')) -ne 'Trusted') 'unobserved extra Chinese sentence is rejected'
Equal 'Conflict' (Status (Audit 'Recovers 100 HP.' '恢复100 HP，仅限副本内使用。')) 'extra Chinese limit is rejected'
Equal 'Conflict' (Status (Audit 'Recovers 100 HP.' '恢复100 HP，但不是药水。')) 'extra Chinese negation is rejected'
Equal 'Conflict' (Status (Audit 'Restores 35% of HP and MP.' '恢复50%的HP和50%的MP。')) 'different percent values are rejected'

# ---- known shipped data conflicts must never paint ----
$conflict1=@($storeType.GetMethod('GetItemDescriptionsById',$flags).Invoke($store,[object[]]@('2020004')))[0]
$conflict2=@($storeType.GetMethod('GetItemDescriptionsById',$flags).Invoke($store,[object[]]@('2020003')))[0]
Check ([string](F $conflict1 'ItemStatus') -eq 'Conflict') '500 HP hamburger row is stored as conflict'
Check ([string](F $conflict2 'ItemStatus') -eq 'Conflict') '360 HP pizza row is stored as conflict'
$hamburger=Body 'A hamburger with bulgogi in it. Recovers 500 HP.'
Check (-not [bool](F $hamburger 'Matched')) 'hamburger conflict body is not translated'
Check ([string](F $hamburger 'Reason') -like 'data-conflict*') 'hamburger conflict records its reason'
$pizza=Body 'A freshly-baked pizza. Recovers 360 HP.'
Check (-not [bool](F $pizza 'Matched')) 'pizza conflict body is not translated'
$hits=@($storeType.GetMethod('FindItemTextMatches',$flags).Invoke($store,[object[]]@('Recovers 500 HP.',[string]'2020004')))
Check ($hits.Count -eq 0) 'FindItemTextMatches never returns a conflicted ordinary row'

# ---- ordinary item translation paths ----
$potion=Body 'A potion made out of red herbs. Restores 100 HP.' '2000000'
Check ([bool](F $potion 'Matched') -and -not [bool](F $potion 'Estimated')) 'ordinary potion body matches trusted full row'
Equal '由红色草药制成的药水。恢复100 HP。' ([string](F $potion 'Translation')) 'ordinary potion body keeps both visible sentences'
$newValue=Body 'Restores 120 HP.' '2000000'
Check ([bool](F $newValue 'Matched')) 'new restore value is translated by the complete template'
Equal '恢复120 HP。' ([string](F $newValue 'Translation')) 'new restore value preserves the visible number'
$newThousand=Body 'Restores 1,000 HP.' '2000000'
Equal '恢复1,000 HP。' ([string](F $newThousand 'Translation')) 'thousands separator is preserved'
$perion=Body 'Returns you to Perion.' '2030003'
Equal '返回勇士部落。' ([string](F $perion 'Translation')) 'Perion body keeps the approved town translation'
Check ([string](F $perion 'ItemId') -eq '2030003') 'Perion body resolves its own item id'
$unknownPlace=Body 'Returns you to Nowhere.' ''
Check ([bool](F $unknownPlace 'Matched')) 'unknown return destination is still translated'
Equal '返回Nowhere。' ([string](F $unknownPlace 'Translation')) 'unknown return location keeps the observed name'
Check ([string](F $unknownPlace 'ItemId') -eq '') 'unknown return location invents no map/item id'
$truncated=Body 'Restores 100 H' '2000000'
Check (-not [bool](F $truncated 'Matched')) 'truncated visible tail is preserved'
$estimated=Body 'A potion made out of red herbs. Restores 100 HPS.' '2000000'
Check ([bool](F $estimated 'Matched') -and [bool](F $estimated 'Estimated')) 'limited OCR deviation is marked estimated, not exact'
$unknownAppend=Body 'A potion made out of red herbs. Restores 100 HP. New unknown effect.' '2000000'
Check (-not [bool](F $unknownAppend 'Matched')) 'unknown appended sentence rejects the whole visible body'
$conditionChange=Body 'Improves Accuracy on hats. Success rate: 10%, Accuracy+5 If failed, the item will not be destroyed.' '2040003'
Check (-not [bool](F $conditionChange 'Matched')) 'changed scroll failure condition is rejected'
$scroll=Body 'Improves Accuracy on hats. Success rate: 10%, Accuracy+5 If failed, the item will be destroyed at a 100% rate.' '2040003'
Check ([bool](F $scroll 'Matched')) 'enhancement scroll description is translated'
$scrollText=[string](F $scroll 'Translation')
Check ($scrollText.Contains('成功率') -and $scrollText.Contains('10%') -and $scrollText.Contains('命中率') -and $scrollText.Contains('5')) 'scroll translation keeps success rate, target and increment'
Check ($scrollText.Contains('失败') -and $scrollText.Contains('摧毁')) 'scroll translation keeps the failure consequence'
$share=Body 'A wooden table that allows you to play Omok.' ''
Check ([bool](F $share 'Matched')) 'identical description shared by several ids is translatable'
Check ([bool](F $share 'Shared') -and [string](F $share 'ItemId') -eq '') 'shared description never picks an arbitrary id'
Equal '可以用来玩五子棋的木桌。' ([string](F $share 'Translation')) 'shared description uses the identical Chinese translation'

# ---- panel planning through the real BuildLabels pipeline ----
$formCtor=@($formType.GetConstructors($flags)|Where-Object{$_.GetParameters().Count -eq 3})[0]
$form=$formCtor.Invoke([object[]]@($true,$null,$store))
$screen=New-Object Drawing.Rectangle 0,0,1920,1080
$form.Bounds=$screen
$formType.GetField('captureBounds',$flags).SetValue($form,$screen)
$formType.GetField('gameBounds',$flags).SetValue($form,$screen)
$formType.GetField('imageTestSize',$flags).SetValue($form,(New-Object Drawing.Size 1920,1080))
$lineType=$assembly.GetType('MapleOverlay.OcrLine',$true)
$wordType=$assembly.GetType('MapleOverlay.OcrWord',$true)
$resultType=$assembly.GetType('MapleOverlay.OcrResult',$true)
$scopeType=$assembly.GetType('MapleOverlay.LabelBuildScope',$true)
function MakeLine([string]$text,[float]$x,[float]$y){
 $line=[Activator]::CreateInstance($lineType,$true)
 $lineType.GetField('Text',$flags).SetValue($line,$text)
 $words=$lineType.GetField('Words',$flags).GetValue($line)
 $cx=$x
 foreach($token in ($text -split ' ')){
  if($token.Length -eq 0){continue}
  $word=[Activator]::CreateInstance($wordType,$true)
  $wordType.GetField('Text',$flags).SetValue($word,$token)
  $w=[Math]::Max(5.0,[float]($token.Length*7))
  $wordType.GetField('BoundingRect',$flags).SetValue($word,(New-Object Drawing.RectangleF $cx,$y,$w,16))
  $words.Add($word);$cx+=$w+4
 }
 return $line
}
function MakeResult([string[]]$texts,[float]$x=700,[float]$y=260){
 $r=[Activator]::CreateInstance($resultType,$true)
 $lines=$resultType.GetField('Lines',$flags).GetValue($r)
 $all=@();$row=0
 foreach($t in $texts){$line=MakeLine $t $x ($y+22*$row);$lines.Add($line);$all+=$t;$row++}
 $resultType.GetField('Text',$flags).SetValue($r,($all -join "`n"))
 return $r
}
function Build($result,[float]$scale=1.0){
 $prepared=New-Object Drawing.Bitmap 1920,1080
 try{
  $args=[object[]]::new(4);$args[0]=$result;$args[1]=[float]$scale;$args[2]=[Drawing.Bitmap]$prepared
  $args[3]=[Enum]::Parse($scopeType,'Discovery')
  $script:built=$formType.GetMethod('BuildLabels',$flags).Invoke($form,$args)
 }finally{$prepared.Dispose()}
}
function Labels{return $script:built}
function ItemLabels($labels){return @($labels|Where-Object{$null -ne $_.GetType().GetField('ItemPanel',$flags).GetValue($_)})}
function Texts($labels){return @($labels|ForEach-Object{ $_.GetType().GetField('Text',$flags).GetValue($_) })}
function FindText($labels,[string]$text){return @($labels|Where-Object{ $_.GetType().GetField('Text',$flags).GetValue($_) -eq $text })}
function PlanText($labels){
 $plans=@();$seen=@()
 foreach($label in $labels){
  $plan=$label.GetType().GetField('ItemPanel',$flags).GetValue($label)
  if($null -eq $plan){continue}
  $key=[Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($plan)
  if($seen -contains $key){continue}
  $seen+=$key;$plans+=$plan.Describe()
 }
 return @($plans)
}

Build (MakeResult @('Red Potion','Account-Bound. Transferable within world.','A potion made out of red herbs.','Restores 100 HP.'))
$labels=Labels
Check ((Texts $labels) -contains '红色药水') 'panel paints the item name'
Check ((Texts $labels) -contains '账号绑定。可在世界内交易。') 'panel paints the auxiliary account/trade clauses'
Check ((Texts $labels) -contains '由红色草药制成的药水。恢复100 HP。') 'panel paints the complete ordinary description'
$plan=(ItemLabels $labels)[0].GetType().GetField('ItemPanel',$flags).GetValue((ItemLabels $labels)[0])
Check ([int](F $plan 'Recognized') -eq 3) 'panel records name, body and auxiliary as three regions'
Check ([int](F $plan 'Planned') -eq 3) 'panel plans all three regions'
Check ([int](F $plan 'Painted') -eq 0) 'BuildLabels alone does not claim a paint pass'
$font=[float](F $plan 'FontSize')
Check ($font -ge 11.0 -and $font -le 20.0) 'panel uses one measured shared font size'
$panelBounds=F $plan 'PanelBounds'
foreach($label in ItemLabels $labels){
 $source=$label.GetType().GetField('SourceBounds',$flags).GetValue($label)
 Check ($panelBounds.Contains($source)) 'item label source stays inside its measured panel bounds'
 Check ([float]$label.GetType().GetField('LayoutFontSize',$flags).GetValue($label) -gt 0) 'item label carries the shared font size into composition'
}

Build (MakeResult @('Return Scroll to Perion','Returns you to Perion.'))
$labels=Labels
Check ((Texts $labels) -contains '勇士部落回城卷') 'return scroll name is painted'
Check ((Texts $labels) -contains '返回勇士部落。') 'return scroll body is painted together with the name'
$plans=PlanText $labels
Check ($plans.Count -eq 1) 'return scroll uses one local item plan'

Build (MakeResult @('Hamburger','A hamburger with bulgogi in it.','Recovers 500 HP.'))
$labels=Labels
Check ((Texts $labels) -contains '汉堡') 'conflict item still paints its independently verified name'
Check (-not ((Texts $labels) -contains '夹着韩式烤肉的汉堡包。恢复400血量。')) 'conflict body never paints despite an exact English hit'
$planText=(PlanText $labels)[0]
Check ($planText.Contains('item-body:preserved/data-conflict')) 'conflict body records data-conflict at the plan stage'

Build (MakeResult @('Red Potion','This is a new effect.','Restores 120 HP.'))
$labels=Labels
Check ((Texts $labels) -contains '红色药水') 'unknown body does not suppress a verified name'
Check (-not ((Texts $labels) -contains '恢复120 HP。')) 'unknown appended body is preserved, not partially painted'

Build (MakeResult @('Red Potion'))
Check ((ItemLabels (Labels)).Count -eq 0) 'a bare item name without local context is not claimed as a tooltip'

Build (MakeResult @('Returns you to Perion.'))
$labels=Labels
Check ((Texts $labels) -contains '返回勇士部落。') 'single-line body-only return scroll is recognized from its complete trusted row'

Build (MakeResult @('Cutie Hair'))
$labels=Labels
Check ((Texts $labels).Count -eq 0) 'same English name with different Chinese variants is preserved as ambiguous'

Build (MakeResult @('Quest Summary','Returns you to Perion.'))
$labels=Labels
Check (-not ((Texts $labels) -contains '返回勇士部落。')) 'quest summary context does not borrow the return-scroll body'
Build (MakeResult @('Current Level : 1','Next Level : 2','Returns you to Perion.'))
$labels=Labels
Check (-not ((Texts $labels) -contains '返回勇士部落。')) 'skill detail context does not borrow the return-scroll body'
Build (MakeResult @('Player: Returns you to Perion.'))
$labels=Labels
Check (-not ((Texts $labels) -contains '返回勇士部落。')) 'player chat never becomes an item tooltip'

# ---- measured/estimated geometry and fixed shared typography ----
Build (MakeResult @('Red Potion','A potion made out of red herbs.','Restores 100 HP.') 640 300) 1.25
$labels=Labels
Check ((Texts $labels) -contains '红色药水') 'DPI-scaled ordinary tooltip is still recognized'
foreach($label in ItemLabels $labels){
 $source=$label.GetType().GetField('SourceBounds',$flags).GetValue($label)
 Check ($source.Left -ge 0 -and $source.Right -le 1920 -and $source.Top -ge 0 -and $source.Bottom -le 1080) 'DPI-scaled source bounds stay finite and inside the frame'
}
$fit=$formType.GetMethod('FitFixedFont',$flags)
Check ($null -ne $fit) 'shared-font fit helper is available to composition'
$bm=New-Object Drawing.Bitmap 1,1
$g=[Drawing.Graphics]::FromImage($bm)
try{
 $tiny=$fit.Invoke($null,[object[]]@($g,[string]('由红色草药制成的药水。恢复100 HP。'),(New-Object Drawing.SizeF 12,10),[float]11.0,'SimSun'))
 Check ($null -eq $tiny) 'a translation that cannot fit the measured area is preserved, never truncated'
 $roomy=$fit.Invoke($null,[object[]]@($g,[string]'恢复100 HP。',(New-Object Drawing.SizeF 200,40),[float]11.0,'SimSun'))
 Check ($null -ne $roomy) 'a translation with measured room is allowed to paint'
 if($null -ne $roomy){$roomy.Dispose()}
}finally{$g.Dispose();$bm.Dispose()}

if($count -ne 53){throw "Unexpected check count: $count"}
Write-Output ('ITEM-001 logic: '+$count+'/'+$count+' passed; dictionary SHA-256='+(Get-FileHash -LiteralPath (Join-Path $root '枫语幕词库.tsv') -Algorithm SHA256).Hash+'; real game pixels UNVERIFIED.')
[Environment]::Exit(0)


