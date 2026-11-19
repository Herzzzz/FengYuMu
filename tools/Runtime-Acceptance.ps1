function Get-LiveEventIndices([int]$Start, [int]$EndExclusive) {
    for ($eventIndex=$Start; $eventIndex -lt $EndExclusive; $eventIndex++) { $eventIndex }
}
function Assert-LiveToggleEvidence([string[]]$Events) {
    $hotkeys = @(for ($i=0; $i -lt $Events.Count; $i++) {
        if ($Events[$i] -match ' hotkey-arrived id=1001(?:\s|$)') { $i }
    })
    if ($hotkeys.Count -lt 2) { throw '缺少本次 F8 显示和隐藏的两次 WM_HOTKEY；F9/F10 不能替代' }
    $first=$hotkeys[0]; $second=$hotkeys[1]
    $request = @((Get-LiveEventIndices ($first+1) $second) | Where-Object { $Events[$_] -match ' manual-request id=\d+ generation=(\d+)' } | Select-Object -First 1)
    if ($request.Count -ne 1) { throw '第一下 F8 没有开始新的手动识别请求' }
    $null = $Events[$request[0]] -match 'generation=(\d+)'; $generation=$Matches[1]
    $starts = @((Get-LiveEventIndices ($request[0]+1) $second) | Where-Object { $Events[$_] -match " ocr-start generation=$generation automatic=False(?:\s|`$)" })
    if ($starts.Count -eq 0) { throw '本次手动请求缺少同代 OCR 开始事件' }
    $paints = @((Get-LiveEventIndices ($starts[0]+1) $second) | Where-Object { $Events[$_] -match " overlay-first-paint generation=$generation elapsedMs=(\d+)" })
    if ($paints.Count -eq 0) { throw '本次 F8 请求没有成功绘制证据；检查捕获/OCR诊断' }
    $null = $Events[$paints[0]] -match 'elapsedMs=(\d+)'; $paintMs=[int]$Matches[1]
    $hidden = @((Get-LiveEventIndices ($second+1) $Events.Count) | Where-Object { $Events[$_] -match " overlay-hidden generation=$generation(?:\s|`$)" })
    if ($hidden.Count -eq 0) { throw '第二下 F8 没有隐藏同代覆盖层' }
    if (@((Get-LiveEventIndices ($hidden[0]+1) $Events.Count) | Where-Object { $Events[$_] -match " overlay-published generation=$generation(?:\s|`$)" }).Count -gt 0) {
        throw '隐藏后旧代识别结果重新发布'
    }
    return [pscustomobject]@{ Generation=[int]$generation; FirstPaintMs=$paintMs; Hidden=$true }
}
