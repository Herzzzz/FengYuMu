# 词库词形匹配回归：复数后缀必须命中单数条目（2026-11-19）
#
# 现象：任务原文里的 "Lupins" 匹配不到词库的 "Lupin -> 猴子"，模型只能音译成
# 「路平」。修法：FindInBuckets 精确匹配失败后允许 +s / +es 后缀命中单数条目，
# 但只对 4 字母以上的词生效，避免短词误伤。
param([string]$ExePath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = if($ExePath){$ExePath}else{Join-Path $repoRoot 'bin/candidate-r3/枫语幕.exe'}
if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
}
$assembly = Load-TestAssembly $exe
$all = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'

$overlayType = $assembly.GetType('MapleOverlay.OverlayForm', $false)
$storeType = $assembly.GetType('MapleOverlay.TranslationStore', $false)
$entryType = $assembly.GetType('MapleOverlay.TranslationEntry', $true)
$matchType = $assembly.GetType('MapleOverlay.MatchResult', $true)
if($null -eq $storeType){ throw '找不到 TranslationStore' }
if($null -eq $entryType){ throw '找不到 TranslationEntry' }
if($null -eq $matchType){ throw '找不到 MatchResult' }

$find = $storeType.GetMethod('FindInBuckets', $all)
if($null -eq $find){ throw '找不到 FindInBuckets' }

$listType = [System.Collections.Generic.List``1].MakeGenericType($entryType)
$dictType = [System.Collections.Generic.Dictionary``2].MakeGenericType([char], $listType)

function New-Entry([string]$english, [string]$chinese){
    $e = [Activator]::CreateInstance($entryType)
    $entryType.GetField('English').SetValue($e, $english)
    $entryType.GetField('Chinese').SetValue($e, $chinese)
    $entryType.GetField('Category').SetValue($e, '怀旧服-怪物#35')
    $entryType.GetField('Normalized').SetValue($e, $english.ToLowerInvariant())
    $entryType.GetField('TaskId').SetValue($e, '')
    return $e
}

function New-Buckets([string]$english){
    $dict = [Activator]::CreateInstance($dictType)
    $list = [Activator]::CreateInstance($listType)
    [void]$list.Add((New-Entry $english '猴子'))
    $dict.Add([char]$english.Substring(0,1).ToLowerInvariant(), $list)
    return $dict
}

function Invoke-Find([string]$text, $buckets){
    $results = $find.Invoke($null, [object[]]@($text, $buckets, $null))
    return ,$results
}

$checks = 0
function Assert-Found([string]$text, [string]$expectedChinese, [int]$expectedLength){
    $buckets = New-Buckets 'Lupin'
    $results = Invoke-Find $text $buckets
    if($results.Count -ne 1){ throw "「$text」应命中 1 条，实际 $($results.Count) 条" }
    $match = $results[0]
    $chinese = $match.Entry.Chinese
    $length = [int]$match.Length
    if($chinese -ne $expectedChinese){ throw "「$text」译文应为「$expectedChinese」，实际「$chinese」" }
    if($length -ne $expectedLength){ throw "「$text」命中长度应为 $expectedLength，实际 $length" }
}

function Assert-NotFound([string]$text){
    $buckets = New-Buckets 'Lupin'
    $results = Invoke-Find $text $buckets
    if($results.Count -ne 0){ throw "「$text」不应命中，实际命中 $($results.Count) 条" }
}

# 1. 单数：原有行为不变
Assert-Found 'Lupin in the forest' '猴子' 5
$checks++
# 2. 复数 +s：本次修复的核心
Assert-Found 'the Lupins in the forest' '猴子' 6
$checks++
# 3. 句子里任意位置
Assert-Found 'collect 25 vials from the Lupins' '猴子' 6
$checks++
# 4. 大小写不敏感（Normalize 后）
Assert-Found 'LUPINS everywhere' '猴子' 6
$checks++
# 5. 不继续的词形不能被吞（LupinX 不是复数）
Assert-NotFound 'Lupine'
$checks++
# 6. +es 形式：Boxes 与 Box
$boxBuckets = New-Buckets 'Box'
$r = Invoke-Find 'the Boxes on the floor' $boxBuckets
if($r.Count -ne 1 -or $r[0].Entry.Chinese -ne '猴子' -or [int]$r[0].Length -ne 5){
    throw '+es 后缀匹配失败'
}
$checks++
# 7. 3 字母词也做复数（Owls -> Owl）
$owlBuckets = New-Buckets 'Owl'
$r2 = Invoke-Find 'Owls at night' $owlBuckets
if($r2.Count -ne 1 -or [int]$r2[0].Length -ne 4){ throw '3 字母词的复数匹配失败' }
$checks++
# 8. 2 字母词不启用复数，避免缩写误伤
$hpBuckets = New-Buckets 'Hp'
$r3 = Invoke-Find 'Hps everywhere' $hpBuckets
if($r3.Count -ne 0){ throw '2 字母词不应启用复数匹配' }
$checks++

Write-Output "词形匹配：单数、+s、句中、大小写、拒绝非复数、+es、3字母复数、2字母边界共 $checks 项通过"
