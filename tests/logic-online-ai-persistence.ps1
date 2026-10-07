$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "找不到程序集：$exe" }

$assembly = [Reflection.Assembly]::LoadFile($exe)
$all = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'
$settingsType = $assembly.GetType('MapleOverlay.OnlineAiSettings', $true)
if ($null -eq $settingsType) { throw '找不到 OnlineAiSettings' }

$load = $settingsType.GetMethod('Load', $all)
$save = $settingsType.GetMethod('Save', $all)
$clear = $settingsType.GetMethod('Clear', $all)
if ($null -eq $load -or $null -eq $save -or $null -eq $clear) {
    throw '联网AI设置缺少 Load/Save/Clear 入口'
}

function New-Settings {
    return [Activator]::CreateInstance($settingsType, $true)
}
function Get-Field($settings, [string]$name) {
    return $settingsType.GetField($name, $all).GetValue($settings)
}
function Set-Field($settings, [string]$name, $value) {
    $settingsType.GetField($name, $all).SetValue($settings, $value)
}

# ---------------------------------------------------------------------------
# The round-trip must not disturb the real player configuration, so the whole
# OnlineAI subkey is backed up and restored around the test. No network call is
# made: this only proves the choice survives a save/reload cycle.
# ---------------------------------------------------------------------------
$keyPath = 'HKCU:\Software\FengYuMu\OnlineAI'
$backup = @{}
$existed = Test-Path -LiteralPath $keyPath
if ($existed) {
    $item = Get-Item -LiteralPath $keyPath
    foreach ($name in $item.GetValueNames()) {
        $backup[$name] = $item.GetValue($name, $null,
            [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    }
}

try {
    # 1. A non-default provider choice must survive save -> load.
    $custom = New-Settings
    Set-Field $custom 'Provider' 'DeepSeek V4 Flash（快速）'
    Set-Field $custom 'Endpoint' 'https://api.deepseek.com/v1/chat/completions'
    Set-Field $custom 'Model' 'deepseek-v4-flash'
    Set-Field $custom 'ApiKey' 'sk-round-trip-test-key'
    $save.Invoke($custom, @())

    $reloaded = $load.Invoke($null, @())
    if ((Get-Field $reloaded 'Provider') -ne 'DeepSeek V4 Flash（快速）' -or
        (Get-Field $reloaded 'Endpoint') -ne 'https://api.deepseek.com/v1/chat/completions' -or
        (Get-Field $reloaded 'Model') -ne 'deepseek-v4-flash') {
        throw ("供应商/接口/模型没有持久化：" +
            "$(Get-Field $reloaded 'Provider') | $(Get-Field $reloaded 'Endpoint') | $(Get-Field $reloaded 'Model')")
    }
    if ((Get-Field $reloaded 'ApiKey') -ne 'sk-round-trip-test-key') {
        throw 'API Key 没有通过 DPAPI 加密往返恢复'
    }

    # 2. A second save must replace the previous choice, not append to it.
    Set-Field $custom 'Provider' '智谱 GLM-4-Flash（免费备用）'
    Set-Field $custom 'Endpoint' 'https://open.bigmodel.cn/api/paas/v4/chat/completions'
    Set-Field $custom 'Model' 'glm-4-flash-250414'
    Set-Field $custom 'ApiKey' ''
    $save.Invoke($custom, @())
    $reloaded = $load.Invoke($null, @())
    if ((Get-Field $reloaded 'Provider') -ne '智谱 GLM-4-Flash（免费备用）' -or
        (Get-Field $reloaded 'Model') -ne 'glm-4-flash-250414' -or
        (Get-Field $reloaded 'ApiKey') -ne '') {
        throw '第二次保存没有覆盖上一次的供应商选择'
    }

    # 3. The shipped default must be restored by Clear.
    $clear.Invoke($null, @())
    $reloaded = $load.Invoke($null, @())
    if ((Get-Field $reloaded 'Provider') -ne '豆包 2.1 Lite（推荐）' -or
        (Get-Field $reloaded 'Model') -ne 'doubao-seed-2-1-lite-260915' -or
        (Get-Field $reloaded 'ApiKey') -ne '') {
        throw '恢复默认设置没有回到豆包 2.1 Lite 且清空 Key'
    }

    # 4. A configuration written by the previous release must still load. The old
    #    build stamped "豆包 2.0 Lite（推荐）" and had no migration path.
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\FengYuMu\OnlineAI')
    try {
        $key.SetValue('Provider', '豆包 2.0 Lite（推荐）')
        $key.SetValue('Endpoint', 'https://ark.cn-beijing.volces.com/api/v3/responses')
        $key.SetValue('Model', 'doubao-seed-2-0-lite-260215')
        $key.DeleteValue('ApiKey', $false)
    } finally { $key.Close() }
    $reloaded = $load.Invoke($null, @())
    if ((Get-Field $reloaded 'Provider') -ne '豆包 2.0 Lite（兼容旧配置）' -or
        (Get-Field $reloaded 'Model') -ne 'doubao-seed-2-0-lite-260215') {
        throw "旧版豆包 2.0 配置没有迁移：$(Get-Field $reloaded 'Provider')"
    }

    # 5. A pre-provider configuration that only stored a custom endpoint must be
    #    recognised as the custom interface instead of silently resetting.
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\FengYuMu\OnlineAI')
    try {
        $key.DeleteValue('Provider', $false)
        $key.SetValue('Endpoint', 'https://example.invalid/v1/chat/completions')
        $key.SetValue('Model', 'my-custom-model')
    } finally { $key.Close() }
    $reloaded = $load.Invoke($null, @())
    if ((Get-Field $reloaded 'Provider') -ne '自定义兼容接口' -or
        (Get-Field $reloaded 'Model') -ne 'my-custom-model') {
        throw "旧自定义接口配置没有识别：$(Get-Field $reloaded 'Provider')"
    }
} finally {
    if (Test-Path -LiteralPath $keyPath) { Remove-Item -LiteralPath $keyPath -Recurse -Force }
    if ($existed -and $backup.Count -gt 0) {
        New-Item -Path $keyPath -Force | Out-Null
        foreach ($name in $backup.Keys) {
            New-ItemProperty -Path $keyPath -Name $name -Value $backup[$name] -Force | Out-Null
        }
    }
}

# The player's own configuration must be exactly as it was before this test.
$restored = @{}
if (Test-Path -LiteralPath $keyPath) {
    $item = Get-Item -LiteralPath $keyPath
    foreach ($name in $item.GetValueNames()) { $restored[$name] = $true }
}
if ($restored.Count -ne $backup.Count) {
    throw "测试后注册表没有完整恢复：$($restored.Count) vs $($backup.Count)"
}
foreach ($name in $backup.Keys) {
    if (-not $restored.ContainsKey($name)) { throw "测试后注册表缺少原值：$name" }
}

Write-Output ('联网AI配置记忆：供应商/接口/模型/Key 保存后重新加载可恢复，二次保存覆盖旧选择，' +
    'Clear 回到豆包 2.1 默认，旧版豆包 2.0 与旧自定义配置均可迁移；测试后注册表完整还原')
