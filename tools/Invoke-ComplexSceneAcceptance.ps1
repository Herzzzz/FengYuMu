param(
    [string]$ImageDirectory = 'D:\TEMP',
    [string]$OutputDirectory = 'D:\GPT\文件\枫语幕\验收\2026-10-12-14图'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot 'tests\Start-ProductExe.ps1')

$imageIds = @(
    'c29685ee-ee32-4914-860e-430128b712b2',
    'e5002805-b884-4e2e-ad90-3716fb72bb8e',
    '1a42ed39-f0d4-4860-9231-81cb9576efbe',
    '192e7b6b-fa23-4a3b-a2c4-bec58d7850fa',
    '6987a838-775a-484f-b14d-386a9cd902b6',
    '792115d3-a3d4-48bf-803b-20be0ca6d81c',
    '6f68f274-cbf2-4756-bdcf-940c5fd8c4a4',
    'aef7aff7-7286-4596-8e79-04955753ccb0',
    '20fc3ca0-07dd-4334-ab84-fef02d7ea5da',
    '162084c4-ef79-44b9-86b4-cb1e791dd0c9',
    '4599aa49-699c-4f07-bfb7-304a51f7061b',
    'de4d2ddd-2afa-473e-b970-d6aa93d9bc25',
    'fa43ec66-aad0-47db-bd65-41d4b9ebeef3',
    '2444a6b3-1a9b-413e-87e8-25c612bed62a'
)

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$exe = Join-Path $repoRoot '枫语幕.exe'
$runFile = Join-Path $repoRoot 'last_run.txt'
$rows = @()

for ($index = 0; $index -lt $imageIds.Count; $index++) {
    $number = $index + 1
    $id = $imageIds[$index]
    $image = Join-Path $ImageDirectory ('codex-clipboard-' + $id + '.png')
    if (-not (Test-Path -LiteralPath $image)) { throw "缺少验收图片：$image" }

    Write-Host ('[IMAGE {0:00}] {1}' -f $number, $image)
    if (Test-Path -LiteralPath $runFile) { Remove-Item -LiteralPath $runFile -Force }
    Start-ProductExe -FilePath $exe -ArgumentList @(
        '--benchmark', "--benchmark-image=$image", '--benchmark-range=balanced'
    ) -WorkingDirectory $repoRoot -Wait
    if (-not (Test-Path -LiteralPath $runFile)) { throw "第 $number 张未产生 last_run.txt" }

    $raw = Get-Content -LiteralPath $runFile -Raw -Encoding UTF8
    if ($raw.StartsWith('错误=')) { throw "第 $number 张执行失败：$raw" }
    $final = [regex]::Match($raw, '最终收敛耗时=(\d+)')
    $first = [regex]::Match($raw, '首次可见耗时=(\d+)')
    $hits = [regex]::Match($raw, '命中数量=(\d+)')
    $labels = [regex]::Match($raw, '覆盖标签=([^\r\n]*)')
    if (-not $final.Success -or -not $first.Success -or -not $hits.Success) {
        throw "第 $number 张报告字段不完整"
    }

    $finalMs = [int]$final.Groups[1].Value
    $row = [pscustomobject]@{
        image = $number
        id = $id
        firstMs = [int]$first.Groups[1].Value
        finalMs = $finalMs
        hits = [int]$hits.Groups[1].Value
        under2500 = $finalMs -le 2500
        labels = $labels.Groups[1].Value
    }
    $requiredLabels = @{
        1 = @('我把所需物品交给了射手村公园的凯茜', '猪猪/蘑菇五子棋')
        2 = @('去林中之城找萨比特拉玛吧', '粉红花束')
        6 = @('可使用至：11512027 00:36 UTC', '我感受到一股强大的力量！',
            '右键点击增益图标即可取消变身。')
        9 = @('角色信息', '保存名字', '交易请求', '悄悄话', '邀请聊天',
            '加为好友', '加入黑名单', '举报')
    }
    if ($requiredLabels.ContainsKey($number)) {
        foreach ($required in $requiredLabels[$number]) {
            if (-not $row.labels.Contains($required)) {
                throw "第 $number 张缺少关键译文：$required"
            }
        }
    }
    $rows += $row
    Copy-Item -LiteralPath $runFile -Destination (Join-Path $OutputDirectory (
        'final-{0:00}-{1}.last_run.txt' -f $number, $id)) -Force
    Write-Host ('[RESULT {0:00}] {1}ms hits={2} pass={3}' -f
        $number, $row.finalMs, $row.hits, $row.under2500)
}

$summaryPath = Join-Path $OutputDirectory 'final-summary.json'
$rows | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
$average = [math]::Round((($rows | Measure-Object finalMs -Average).Average), 1)
$sorted = @($rows.finalMs | Sort-Object)
$median = [math]::Round(($sorted[6] + $sorted[7]) / 2, 1)
$passed = @($rows | Where-Object under2500).Count
$rows | Format-Table image, firstMs, finalMs, hits, under2500 -AutoSize
Write-Host "FINAL_AVG_MS=$average FINAL_MEDIAN_MS=$median PASS_COUNT=$passed/14"
