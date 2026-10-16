param(
    [string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$EvidenceRoot = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) '.video-review-5420')
)

$ErrorActionPreference = 'Stop'
$dictionary = Join-Path $RepoRoot '枫语幕词库.tsv'
if (-not (Test-Path -LiteralPath $dictionary)) { throw "缺少词库：$dictionary" }

$expected = @(
    @("Teo's Collection", '怀旧服-任务#10010'),
    @('Teo in Lith Harbor told me his hobby is collecting weird things. He asked me to hunt Slimes near the town and bring back 1 Slime Bubble. He warned me that it might be hard to find, though!', '怀旧服-任务说明#10010'),
    @('[Event] Growing Leaf', '怀旧服-任务#500009'),
    @('The Maple Administrator in Henesys is handing out Pale Maple Leaf for a special event! Nurture yours and see what it becomes...', '怀旧服-任务说明#500009'),
    @('First Greeting with Anne, the Town Resident', '怀旧服-任务#506013'),
    @("It looks like Arthur, the town clerk, is looking for someone to help with their work. Let's go check out Community Board in front of Henesys Town Hall.", '怀旧服-任务说明#506013'),
    @('The master blacksmith of Perion, Silas Irons, seems to be looking for someone worthy of becoming his apprentice.', '怀旧服-任务说明#80008'),
    @('It seems Vicious, the carpenter of Henesys, is looking for someone worthy of becoming his apprentice.', '怀旧服-任务说明#80017'),
    @('JM From tha Streetz Looking for a Partner!', '怀旧服-任务#80020'),
    @('The leatherworking master JM From tha Streetz of Kerning City seems to be looking for someone who could become his business partner.', '怀旧服-任务说明#80020'),
    @('The Arcforging master, Chrishrama of Sleepywood, seems to be searching for someone who could become his apprentice.', '怀旧服-任务说明#80023'),
    @('Shadow of the World Tree', '怀旧服-任务#10607'),
    @('The Roots of Regret', '怀旧服-任务#10607'),
    @("Let's go see Grendel the Really Old in Ellinia. He seems deeply troubled by something...", '怀旧服-任务说明#10607'),
    @('Arwen the Fairy from Ellinia has lost something...', '怀旧服-任务说明#10200'),
    @('Whenever I walk past Wing the Fairy in Ellinia, he seems to be asking for help. I wonder why...', '怀旧服-任务说明#10210'),
    @('A beautiful girl by the name of Riel from Florina Beach seems to be in need of help...', '怀旧服-任务说明#10700'),
    @('I hear that Camila in Henesys is worried about something...', '怀旧服-任务说明#10105'),
    @('Maya and the Weird Medicine', '怀旧服-任务#10106'),
    @('I hear that Maya in Henesys is very sick...', '怀旧服-任务说明#10106'),
    @('I delivered the Glowing Mushroom from the cave entrance to Arwen the Fairy.', '怀旧服-任务说明#500001'),
    @('[Event] A New Adventure Awaits', '怀旧服-任务#500008'),
    @("You delivered Betty's research report to Cherry. Now that I've come all this way... why not hop aboard the ship bound for Ossyria?", '怀旧服-任务说明#500008'),
    @('Athena Pierce in Henesys welcomed me warmly, and I gained a new job, obtaining even greater power!', '怀旧服-任务说明#20203'),
    @('Grendel the Really Old in Ellinia welcomed me warmly, and I gained a new job, obtaining even greater power!', '怀旧服-任务说明#20103'),
    @('Dark Lord in Kerning City welcomed me warmly, and I gained a new job, obtaining even greater power!', '怀旧服-任务说明#20303'),
    @('Dances with Balrog in Perion welcomed me warmly, and I gained a new job, obtaining even greater power!', '怀旧服-任务说明#20003'),
    @('To Henesys, the Prairie Town', '怀旧服-任务#506000'),
    @("Arthur, the Town Clerk I met at Henesys Town Hall, suggested I become a resident of Henesys. Once I've made up my mind, I should speak with Arthur for more details.", '怀旧服-任务说明#506000'),
    @('To the Gray City, Kerning City', '怀旧服-任务#506100'),
    @('City Clerk Roxy, whom I met in Kerning City, suggested that I become a resident of Kerning City. Which city suits me better, Henesys or Kerning City? Once I make up my mind, I should speak with the City Clerk of that city for more details.', '怀旧服-任务说明#506100'),
    @('First Greeting with Pia, the Town Resident', '怀旧服-任务#506011'),
    @("I greeted Pia, a resident of Henesys. I have a feeling we're going to get along great!", '怀旧服-任务说明#506011'),
    @('Donating to Henesys', '怀旧服-任务#视频5420-75s'),
    @("I gathered the items requested by Arthur, the town clerk, and brought them to him. I'm glad I could contribute to the development of our town.", '怀旧服-任务说明#视频5420-75s')
)

$rows = @{}
$lineNumber = 0
foreach ($line in [IO.File]::ReadLines($dictionary, [Text.Encoding]::UTF8)) {
    $lineNumber++
    if ([String]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#')) { continue }
    $parts = $line.Split("`t")
    if ($parts.Count -lt 3) { throw "第 $lineNumber 行少于 3 列" }
    $key = $parts[0].Trim() + "`t" + $parts[2].Trim()
    if (-not $rows.ContainsKey($key)) { $rows[$key] = @() }
    $rows[$key] += $lineNumber
}

foreach ($item in $expected) {
    $key = $item[0] + "`t" + $item[1]
    if (-not $rows.ContainsKey($key)) { throw "缺少增量词条：$($item[0]) / $($item[1])" }
    if ($rows[$key].Count -ne 1) { throw "增量词条重复：$($item[0]) / $($item[1])" }
}

$ocrCases = @(
    @('ocr-panels-after\ocr-000.0.txt', '特奥的收藏'),
    @('ocr-panels-after\ocr-003.0.txt', '【活动】成长中的枫叶'),
    @('ocr-panels-after\ocr-019.0.txt', '世界树的阴影'),
    @('ocr-panels-after\ocr-019.0.txt', '遗憾的根源'),
    @('ocr-after\ocr-039.0.txt', '玛雅与奇怪的药'),
    @('ocr-panels-after\ocr-056.0.txt', '前往草原之城射手村'),
    @('ocr-panels-after\ocr-060.0.txt', '前往灰色之城废弃都市'),
    @('ocr-after\ocr-068.0.txt', '初次问候镇民皮亚'),
    @('ocr-panels-after\ocr-075.0.txt', '向射手村捐赠')
)

foreach ($case in $ocrCases) {
    $path = Join-Path $EvidenceRoot $case[0]
    if (-not (Test-Path -LiteralPath $path)) { throw "缺少 OCR 证据：$path" }
    $content = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    if (-not $content.Contains($case[1])) { throw "OCR 证据未命中：$($case[1]) / $path" }
}

$timings = @()
Get-ChildItem -LiteralPath (Join-Path $EvidenceRoot 'ocr-panels-after') -Filter '*.txt' | ForEach-Object {
    $content = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
    $match = [regex]::Match($content, '耗时毫秒=(\d+)')
    if (-not $match.Success) { throw "OCR 证据缺少耗时：$($_.FullName)" }
    $timings += [int]$match.Groups[1].Value
}
if ($timings.Count -lt 23) { throw "OCR 面板样本不足：$($timings.Count) < 23" }
if (($timings | Measure-Object -Maximum).Maximum -gt 2000) { throw '存在超过 2 秒的 OCR 视频样本' }

[pscustomobject]@{
    增量词条 = $expected.Count
    实帧关键命中 = $ocrCases.Count
    OCR样本 = $timings.Count
    最大耗时毫秒 = ($timings | Measure-Object -Maximum).Maximum
    平均耗时毫秒 = [Math]::Round(($timings | Measure-Object -Average).Average)
    结果 = '通过'
} | Format-List
