$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
$benchRoot = Join-Path $repoRoot ('.bench\glossary-reload-' + [Guid]::NewGuid().ToString('N'))
$dictionaryPath = Join-Path $benchRoot '枫语幕词库.tsv'
$initial = "# 枫语幕测试词库`r`nHot Reload Term`t旧译文`t怀旧服-聊天术语`r`n"
$updated = "# 枫语幕测试词库`r`nHot Reload Term`t新译文`t怀旧服-聊天术语`r`n"

New-Item -ItemType Directory -Path $benchRoot -Force | Out-Null
try {
    $assembly = [Reflection.Assembly]::LoadFile($exe)
    $allStatic = [Reflection.BindingFlags]'Static,NonPublic,Public'
    $allInstance = [Reflection.BindingFlags]'Instance,NonPublic,Public'

    $dictionaryType = $assembly.GetType('MapleOverlay.DictionaryOnlyForm', $true)
    $atomicWrite = $dictionaryType.GetMethod('WriteDictionaryAtomically', $allStatic)
    if ($null -eq $atomicWrite) { throw '词库编辑器缺少原子保存入口' }
    $atomicWrite.Invoke($null, [object[]]@([string]$dictionaryPath, [string]$initial)) | Out-Null
    if ([IO.File]::ReadAllText($dictionaryPath) -ne $initial) { throw '首次原子保存内容不一致' }
    if (@(Get-ChildItem -LiteralPath $benchRoot -Filter '*.saving-*').Count -ne 0) {
        throw '成功保存后残留临时词库文件'
    }

    # A replace failure must keep the last complete target and remove its temporary file.
    $locked = [IO.File]::Open($dictionaryPath, [IO.FileMode]::Open,
        [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $replaceFailed = $false
    try {
        try { $atomicWrite.Invoke($null,
            [object[]]@([string]$dictionaryPath, [string]$updated)) | Out-Null }
        catch { $replaceFailed = $true }
    }
    finally { $locked.Dispose() }
    if (-not $replaceFailed) { throw '锁定目标时原子替换没有失败，无法验证旧文件保护' }
    if ([IO.File]::ReadAllText($dictionaryPath) -ne $initial) { throw '替换失败破坏了原词库' }
    if (@(Get-ChildItem -LiteralPath $benchRoot -Filter '*.saving-*').Count -ne 0) {
        throw '替换失败后残留临时词库文件'
    }

    $overlayType = $assembly.GetType('MapleOverlay.OverlayForm', $true)
    $chatType = $assembly.GetType('MapleOverlay.OfflineChatForm', $true)
    $constructor = $chatType.GetConstructor($allInstance, $null,
        [Type[]]@($overlayType, [string]), $null)
    $chat = $constructor.Invoke([object[]]@($null, [string]$benchRoot))
    try {
        $exact = $chatType.GetMethod('TryExactGlossaryTranslation', $allInstance)
        $reload = $chatType.GetMethod('ReloadGlossary', $allInstance)
        $indexField = $chatType.GetField('glossaryExactTranslations', $allInstance)
        if ($null -eq $exact -or $null -eq $reload -or $null -eq $indexField) {
            throw 'AI词库热更新或精确索引入口缺失'
        }
        $first = [object[]]@('Hot Reload Term', '')
        if (-not [bool]$exact.Invoke($chat, $first) -or $first[1] -ne '旧译文') {
            throw "初始精确词条异常：$($first[1])"
        }

        $atomicWrite.Invoke($null,
            [object[]]@([string]$dictionaryPath, [string]$updated)) | Out-Null
        $count = [int]$reload.Invoke($chat, @())
        $second = [object[]]@('Hot Reload Term', '')
        if ($count -ne 1 -or -not [bool]$exact.Invoke($chat, $second) -or $second[1] -ne '新译文') {
            throw "保存后AI词库没有立即生效：count=$count value=$($second[1])"
        }
        $index = $indexField.GetValue($chat)
        if ($index.Count -ne 1 -or -not $index.ContainsKey('hotreloadterm')) {
            throw '精确词条没有进入O(1)索引'
        }

        # A read failure must not clear or partially replace the live index.
        $locked = [IO.File]::Open($dictionaryPath, [IO.FileMode]::Open,
            [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $reloadFailed = $false
        try {
            try { $reload.Invoke($chat, @()) | Out-Null }
            catch { $reloadFailed = $true }
        }
        finally { $locked.Dispose() }
        if (-not $reloadFailed) { throw '锁定词库时重载没有失败，无法验证稳定索引保护' }
        $preserved = [object[]]@('Hot Reload Term', '')
        if (-not [bool]$exact.Invoke($chat, $preserved) -or $preserved[1] -ne '新译文') {
            throw 'AI词库重载失败后没有保留上一次的稳定索引'
        }
    }
    finally { $chat.Dispose() }

    $chatSource = Get-Content (Join-Path $repoRoot 'src\OfflineChat.cs') -Raw -Encoding UTF8
    $methodStart = $chatSource.IndexOf('private async Task ProcessPendingChatAsync(')
    $methodEnd = $chatSource.IndexOf(
        'private async Task<string> TranslateWithPreferredAiAsync(', $methodStart)
    if ($methodStart -lt 0 -or $methodEnd -le $methodStart) { throw '找不到实时翻译队列处理方法' }
    $methodSource = $chatSource.Substring($methodStart, $methodEnd - $methodStart)
    $cacheCheck = $methodSource.IndexOf('if (!translationCache.TryGetValue')
    $glossaryBuild = $methodSource.IndexOf('string glossary = BuildGlossaryForTarget')
    if ($cacheCheck -lt 0 -or $glossaryBuild -lt $cacheCheck) {
        throw '实时翻译仍在缓存命中前扫描完整词库'
    }

    Write-Output '词库原子保存、AI即时热更新、失败保留稳定索引、精确词条O(1)索引：通过'
}
finally {
    if (Test-Path -LiteralPath $benchRoot) {
        Remove-Item -LiteralPath $benchRoot -Recurse -Force
    }
}
