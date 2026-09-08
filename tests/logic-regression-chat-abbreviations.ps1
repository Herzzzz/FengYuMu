param(
    [string]$ExpectedVersion = '2.2.2.0',
    [string]$ExpectedDisplayVersion = 'v2.2.2'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'logic-regression-v2.2.ps1') `
    -ExpectedVersion $ExpectedVersion -ExpectedDisplayVersion $ExpectedDisplayVersion
$a = [Reflection.Assembly]::LoadFile((Join-Path $root '枫语幕.exe'))
$st = $a.GetType('MapleOverlay.TranslationStore', $true)
$sc = $st.GetConstructor([Reflection.BindingFlags]'Instance,NonPublic,Public', $null, [Type[]]@([string]), $null)
$s = $sc.Invoke([object[]]@([string](Join-Path $root '枫语幕词库.tsv')))
$st.GetMethod('Load').Invoke($s, @()) | Out-Null
foreach ($term in @('PQ','KPQ','RJPQ','R>PQ','WTB','WATK')) {
    if (@($st.GetMethod('FindMatches').Invoke($s, @($term))).Count) { throw "聊天词污染F8：$term" }
}
$ft = $a.GetType('MapleOverlay.OfflineChatForm', $true)
$fc = $ft.GetConstructor([Reflection.BindingFlags]'Instance,NonPublic,Public', $null, [Type[]]@($a.GetType('MapleOverlay.OverlayForm',$true),[string]), $null)
$f = $fc.Invoke([object[]]@($null,[string]$root))
$exact = $ft.GetMethod('TryExactGlossaryTranslation',[Reflection.BindingFlags]'Instance,NonPublic')
$build = $ft.GetMethod('BuildGlossary',[Reflection.BindingFlags]'Instance,NonPublic')
$norm = $ft.GetMethod('NormalizeChatPhrase',[Reflection.BindingFlags]'Static,NonPublic')
function Check([string]$x,[string]$want) {
    $v=[object[]]@($x,$null)
    if(-not [bool]$exact.Invoke($f,$v) -or [string]$v[1] -ne $want){throw "$x -> $($v[1])"}
}
Check 'PQ' '组队任务'
Check 'KPQ' '废弃都市组队任务'
Check 'RJPQ' '罗密欧与朱丽叶组队任务'
Check 'R>PQ' '招募组队任务队员'
Check 'WTB' '想收购'
$multi=[object[]]@('RPQ',$null)
if([bool]$exact.Invoke($f,$multi)){throw 'RPQ不应脱离上下文硬翻'}
if([string]$norm.Invoke($null,@('R>PQ')) -eq [string]$norm.Invoke($null,@('RPQ'))){throw 'R>PQ/RPQ发生碰撞'}
$g=[string]$build.Invoke($f,@('R>PQ KPQ RPQ'))
foreach($x in @('R>PQ = 招募组队任务队员','KPQ = 废弃都市组队任务','RPQ = 组队任务招募（具体含义需结合上下文）')){
    if(-not $g.Contains($x)){throw "AI上下文缺少：$x"}
}
$f.Dispose()
$cs=Get-Content (Join-Path $root 'src\OfflineChat.cs') -Raw -Encoding UTF8
$l=$cs.IndexOf('private void LoadGlossary()')
$b=$cs.IndexOf('private async Task InitializeKnowledgeInBackgroundAsync')
if($l -lt 0 -or $b -le $l -or $cs.Substring($l,$b-$l).Contains('MapleKnowledgeInitializer.Initialize')){throw '聊天词库仍同步初始化AI知识'}
foreach($x in @('cachedServerPath','cachedModelPath','contextOnlyGlossaryKeys','可靠的聊天缩写必须按术语表展开')){
    if(-not $cs.Contains($x)){throw "实现缺少：$x"}
}
if($cs.Contains('chatContext')){throw '实时AI仍会把历史原句拼入当前消息，可能造成重复和串句'}
$mo=Get-Content (Join-Path $root 'src\MapleOverlay.cs') -Raw -Encoding UTF8
if(-not $mo.Contains('File.ReadLines(path, Encoding.UTF8)')){throw '主词库未使用流式读取'}
Write-Output '聊天缩写隔离、多义词保护、低卡顿加载回归：通过'
