param([Parameter(Mandatory=$true)][string]$ExePath,[switch]$Before,[string]$BeforeRepository='/workspace/FengYuMu-option2-completeness-r5')
$ErrorActionPreference='Stop'
$root=if($Before){$BeforeRepository}else{Split-Path -Parent $PSScriptRoot}
. (Join-Path $root 'tests/Load-TestAssembly.ps1')
$assembly=Load-TestAssembly $ExePath
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$type=$assembly.GetType('MapleOverlay.TranslationStore',$true)
$dictionary=Join-Path $root '枫语幕词库.tsv'
$originalHash=(Get-FileHash -LiteralPath $dictionary -Algorithm SHA256).Hash
$cases=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/verified-journal-corrections.json') -Raw -Encoding UTF8|ConvertFrom-Json
function Store([string]$path){$s=$type.GetConstructors($flags)[0].Invoke([object[]]@($path));$type.GetMethod('Load',$flags).Invoke($s,@())|Out-Null;return $s}
function Entry($match){$match.GetType().GetField('Entry',$flags).GetValue($match)}
function Value($entry,[string]$name){$entry.GetType().GetField($name,$flags).GetValue($entry)}
function Hits($s,[string]$method,[string]$text,[string]$id=''){
 $arguments=if($method -eq 'FindTaskMatches'){[object[]]@($text,$id)}else{[object[]]@($text)}
 return @($type.GetMethod($method,$flags).Invoke($s,$arguments))
}
function ExactBody($s,[string]$text,[string]$id){
 $entries=@((Hits $s 'FindTaskMatches' $text $id)|ForEach-Object{Entry $_}|Where-Object{(Value $_ 'English') -eq $text})
 if($entries.Count -ne 1){throw ('Expected one independently matched journal entry; id='+$id+' count='+$entries.Count)}
 return $entries[0]
}
$count=0
function Check([bool]$ok,[string]$name){if(-not $ok){throw $name};$script:count++;Write-Output ('PASS: '+$name)}
$store=Store ([string]$dictionary)
foreach($case in $cases){
 $id=$case.category.Split('#')[-1];$entry=ExactBody $store $case.english $id
 Check ((Value $entry 'Chinese') -eq $case.chinese) ('journal translation contains only its observed stage: '+$id)
}
Check ((Get-FileHash -LiteralPath $dictionary -Algorithm SHA256).Hash -eq $originalHash) 'default-value correction never rewrites the TSV'
$temp=Join-Path $root ('.bench/r6-dictionary-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force|Out-Null
try{
 $c=@($cases|Where-Object{$_.category -eq '怀旧服-任务说明#10317'})[0]
 $custom=Join-Path $temp 'custom.tsv'
 $changedEnglish=$c.english+' This is a different journal stage.'
 $lines=@(
  ($c.english+"`t用户自定义正文，请保留。`t"+$c.category),
  ($c.english+"`t"+$c.previousChinese+"`t怀旧服-任务说明#999999"),
  ($changedEnglish+"`t"+$c.previousChinese+"`t"+$c.category),
  ("Icarus's Dream`t我的任务别名`t怀旧服-任务#local-title"))
 [IO.File]::WriteAllLines($custom,$lines,[Text.UTF8Encoding]::new($false));$customHash=(Get-FileHash $custom).Hash
 $user=Store $custom
 Check ((Value (ExactBody $user $c.english '10317') 'Chinese') -eq '用户自定义正文，请保留。') 'explicit custom Chinese always wins over the shipped correction'
 Check ((Value (ExactBody $user $c.english '999999') 'Chinese') -eq $c.previousChinese) 'matching English cannot rewrite another quest ID'
 Check ((Value (ExactBody $user $changedEnglish '10317') 'Chinese') -eq $c.previousChinese) 'changed source wording cannot trigger a shipped-default correction'
 $alias=@(Hits $store 'FindTaskNameMatches' "Icarus's Dream")
 Check ($alias.Count -eq 1 -and (Value (Entry $alias[0]) 'Chinese') -eq '伊卡路斯的飞行梦想') 'observed CW title is independently translatable'
 Check ([string]$type.GetMethod('ResolveUniqueTaskTitleId',$flags).Invoke($store,@("Icarus's Dream")) -eq '') 'server title alone does not invent an original quest ID'
 $alias=@(Hits $user 'FindTaskNameMatches' "Icarus's Dream")
 Check ($alias.Count -eq 1 -and (Value (Entry $alias[0]) 'Chinese') -eq '我的任务别名') 'custom server-title wording is retained'
 Check ((Get-FileHash $custom).Hash -eq $customHash) 'loading the custom dictionary leaves every input byte unchanged'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
$name=@(Hits $store 'FindMatches' 'Screw'|ForEach-Object{Entry $_}|Where-Object{(Value $_ 'English') -eq 'Screw'})[0]
$description=$type.GetMethod('GetItemOverview',$flags).Invoke($store,@('4003000'))
$text=[string](Value $description 'English')
$checkName=$type.GetMethod('HasItemNameAndDescription',$flags)
Check ([bool]$checkName.Invoke($store,@('Screw',$name.PSObject.BaseObject,$text))) 'short Screw name can be corroborated by its complete same-ID description'
Check (-not [bool]$checkName.Invoke($store,@('Screw',$name.PSObject.BaseObject,$text.Substring(0,12)))) 'partial prose cannot waive icon evidence for a short item name'
$other=$type.GetMethod('GetItemOverview',$flags).Invoke($store,@('4003009'))
Check (-not [bool]$checkName.Invoke($store,@('Screw',$name.PSObject.BaseObject,[string](Value $other 'English')))) 'another item description cannot corroborate Screw'
Check (-not [bool]$checkName.Invoke($store,@('Scroll',$name.PSObject.BaseObject,$text))) 'wrong name cannot borrow an unrelated item description'
Check ([bool]$checkName.Invoke($store,@('Screw 250',$name.PSObject.BaseObject,$text))) 'the observed quantity does not suppress its exact item-name prefix'
$countBeforeReload=$type.GetProperty('Count',$flags).GetValue($store)
$type.GetMethod('Load',$flags).Invoke($store,@())|Out-Null
Check ($type.GetProperty('Count',$flags).GetValue($store) -eq $countBeforeReload) 'reloading does not accumulate synthetic aliases or corrections'
if($count -ne 23){throw "Unexpected check count: $count"}
Write-Output ('R6 journal/item: 23/23 passed; actual candidate '+(Get-FileHash $ExePath -Algorithm SHA256).Hash+'; Windows/CW OCR pixels unverified.')
