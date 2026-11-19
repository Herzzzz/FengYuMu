function Assert-BenchmarkAcceptance([string]$Report, [int]$FirstBudgetMs,
    [int]$FinalBudgetMs, [int]$MinimumHits = 0, [int]$BaselineMs = 0) {
    if ($Report.StartsWith('错误=')) { throw 'Benchmark 返回错误' }
    $values = @{}
    foreach ($field in @('首次可见耗时','最终收敛耗时','命中数量')) {
        $match = [regex]::Match($Report, '(?m)^' + [regex]::Escape($field) + '=(\d+)\r?$')
        if (-not $match.Success) { throw "Benchmark 缺少完整指标：$field" }
        $values[$field] = [int]$match.Groups[1].Value
    }
    if ($FirstBudgetMs -gt 0 -and $values['首次可见耗时'] -gt $FirstBudgetMs) { throw '首显阶段估算超出硬门槛' }
    if ($values['最终收敛耗时'] -gt $FinalBudgetMs) { throw '最终收敛超出硬门槛' }
    if ($values['命中数量'] -lt $MinimumHits) { throw '命中数量低于验收要求' }
    if ($BaselineMs -gt 0 -and $values['最终收敛耗时'] -gt [Math]::Floor($BaselineMs * 1.1)) {
        throw '最终收敛相对基线恶化超过 10%'
    }
}
