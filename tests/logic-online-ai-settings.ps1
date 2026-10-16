param([string]$ExePath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = if ([String]::IsNullOrWhiteSpace($ExePath)) { Join-Path $repoRoot 'bin/candidate-r3/枫语幕.exe' } else { $ExePath }
if (-not (Get-Command Load-TestAssembly -ErrorAction SilentlyContinue)) { . (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1') }
$assembly = Load-TestAssembly $exe
$all = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'

$settingsType = $assembly.GetType('MapleOverlay.OnlineAiSettings', $true)
$clientType = $assembly.GetType('MapleOverlay.OnlineAiClient', $true)
$presetType = $assembly.GetType('MapleOverlay.OnlineAiPresets', $true)
$settings = [Activator]::CreateInstance($settingsType, $true)
$provider = [string]$settingsType.GetField('Provider', $all).GetValue($settings)
$endpoint = [string]$settingsType.GetField('Endpoint', $all).GetValue($settings)
$model = [string]$settingsType.GetField('Model', $all).GetValue($settings)
$ready = [bool]$settingsType.GetProperty('IsReady', $all).GetValue($settings, $null)
if ($provider -ne '豆包（火山方舟）' -or
    $endpoint -ne 'https://ark.cn-beijing.volces.com/api/v3/responses' -or
    $model -ne '' -or $ready) {
    throw '联网AI默认服务、接口、模型或未填Key状态不正确'
}
$settingsType.GetField('ApiKey', $all).SetValue($settings, 'fixture-key')
if ([bool]$settingsType.GetProperty('IsReady', $all).GetValue($settings,$null)) { throw 'Missing model ID was treated as ready' }
$settingsType.GetField('Model', $all).SetValue($settings, 'fixture-model-id')
if (-not [bool]$settingsType.GetProperty('IsReady', $all).GetValue($settings, $null)) {
    throw '填写完整 HTTPS 接口、模型和 Key 后仍未进入联网就绪状态'
}

$presets = @($presetType.GetField('All', $all).GetValue($null))
$presetNames = @($presets | ForEach-Object { [string]$_.GetType().GetField('Name', $all).GetValue($_) })
foreach ($required in @('豆包（火山方舟）','DeepSeek（开放平台）','智谱（开放平台）','自定义兼容接口')) {
    if ($presetNames -notcontains $required) { throw "联网AI服务缺少：$required" }
}
$doubao21 = $presets | Where-Object { [string]$_.GetType().GetField('Name', $all).GetValue($_) -eq '豆包（火山方舟）' } | Select-Object -First 1
$doubao21Endpoint = [string]$doubao21.GetType().GetField('Endpoint', $all).GetValue($doubao21)
$doubao21Model = [string]$doubao21.GetType().GetField('Model', $all).GetValue($doubao21)
if ($doubao21Endpoint -ne 'https://ark.cn-beijing.volces.com/api/v3/responses' -or
    $doubao21Model -ne '') {
    throw '新预设应要求用户核对服务端模型 ID'
}

$build = $clientType.GetMethod('BuildRequestBody', $all)
$describeHttp = $clientType.GetMethod('DescribeHttpFailure', $all)
$badKey = [string]$describeHttp.Invoke($null, [object[]]@(401, '', '豆包（火山方舟）'))
$noModel = [string]$describeHttp.Invoke($null, [object[]]@(403,
    '{"error":{"code":"PermissionDenied"}}', '豆包（火山方舟）'))
$notOpen = [string]$describeHttp.Invoke($null, [object[]]@(404,
    '{"error":{"code":"ModelNotOpen"}}', '豆包（火山方舟）'))
$noQuota = [string]$describeHttp.Invoke($null, [object[]]@(429, '', '豆包（火山方舟）'))
if (-not $badKey.Contains('不要填 Access Key 或 Secret Key') -or
    -not $noModel.Contains('模型未开通') -or
    -not $noModel.Contains('PermissionDenied') -or
    -not $notOpen.Contains('模型未开通') -or
    $notOpen.Contains('API Key 已通过验证') -or
    -not $noQuota.Contains('额度')) {
    throw '联网AI没有把密钥、模型权限和额度错误说明白'
}
$doubaoBody = [string]$build.Invoke($null, [object[]]@($settings,
    'which event is it i JUST went to orbis bro BriskIcedTea', '简体中文', "Orbis = 天空之城`n"))
foreach ($required in @('冒险岛怀旧服国际服老玩家','禁止逐词硬译','玩家ID','只输出一行最终译文',
    '不得漏译、重复或编造','Orbis = 天空之城','BriskIcedTea','"thinking":{"type":"disabled"}',
    '"max_output_tokens":512')) {
    if (-not $doubaoBody.Contains($required)) { throw "豆包逐句请求缺少：$required" }
}
$prompt = [string]$clientType.GetMethod('BuildSystemPrompt', $all).Invoke($null,
    [object[]]@('简体中文'))
if ($prompt.Length -gt 500) {
    throw "联网AI逐句提示词再次膨胀：$($prompt.Length) 字符（要求不超过500）"
}
foreach ($forbidden in @('本地AI译文','校对员')) {
    if ($doubaoBody.Contains($forbidden)) { throw "联网AI仍暴露中间复核工作：$forbidden" }
}

$settingsType.GetField('Endpoint', $all).SetValue($settings, 'https://api.deepseek.com/v1/chat/completions')
$settingsType.GetField('Model', $all).SetValue($settings, 'deepseek-chat')
$deepSeekBody = [string]$build.Invoke($null, [object[]]@($settings,
    'How do you whisper back someone?', '简体中文', "whisper = 悄悄话`n"))
$deepSeekRequest = $deepSeekBody | ConvertFrom-Json
if ($deepSeekRequest.model -ne 'deepseek-chat' -or $deepSeekRequest.max_tokens -ne 512 -or
    @($deepSeekRequest.messages).Count -ne 2 -or $deepSeekRequest.PSObject.Properties.Name -contains 'thinking' -or
    -not $deepSeekBody.Contains('怎么回复别人的悄悄话')) {
    throw 'DeepSeek 标准 chat-completions 请求或冒险岛口语示例不正确'
}

$settingsType.GetField('Endpoint', $all).SetValue($settings, 'https://open.bigmodel.cn/api/paas/v4/chat/completions')
$settingsType.GetField('Model', $all).SetValue($settings, 'glm-4-flash-250414')
$glmBody = [string]$build.Invoke($null, [object[]]@($settings, 'S> Kumbi 200k', '简体中文', "Kumbi = 雪花镖`n"))
if (-not $glmBody.Contains('"do_sample":false') -or -not $glmBody.Contains('Kumbi = 雪花镖')) {
    throw '智谱免费备用没有使用稳定输出或冒险岛词库术语'
}

$overlaySource = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
if($null -ne $assembly.GetType('MapleOverlay.OfflineAiClient',$false) -or
    $null -ne $assembly.GetType('MapleOverlay.AiInstallForm',$false)){throw 'Offline feature remains'}
$onlineParameters=$clientType.GetMethod('TranslateAsync',$all).GetParameters()
if($onlineParameters.Count -ne 5 -or $onlineParameters[4].ParameterType -ne [Threading.CancellationToken]){throw 'Online cancellation contract missing'}
$formType = $assembly.GetType('MapleOverlay.OnlineAiForm', $true)
$form = [Activator]::CreateInstance($formType, $true)
try {
    $providerBox = $formType.GetField('provider', $all).GetValue($form)
    if ($providerBox.SelectedIndex -lt 0 -or [string]::IsNullOrWhiteSpace([string]$providerBox.SelectedItem)) {
        throw '联网AI界面没有明确显示当前服务'
    }
} finally {
    $form.Dispose()
}
if ($overlaySource.Contains('new ToolStripMenuItem("同步AI词库")')) {
    throw '托盘仍显示重复的同步AI词库入口'
}
if (-not $overlaySource.Contains('重新显示AI翻译悬浮窗') -or
    -not $overlaySource.Contains('tray.DoubleClick += delegate { ShowMainPanel(); }')) {
    throw '托盘没有提供主界面双击恢复和悬浮窗右键恢复'
}

$runRoot = Split-Path -Parent $exe
$uiError = Join-Path $runRoot 'online_ai_ui_test_error.txt'
$uiImage = Join-Path $runRoot 'online_ai_ui_test.png'
Remove-Item -LiteralPath $uiError -Force -ErrorAction SilentlyContinue
if (-not (Get-Command Start-ProductExe -ErrorAction SilentlyContinue)) { . (Join-Path $PSScriptRoot 'Start-ProductExe.ps1') }
Start-ProductExe -FilePath $exe -ArgumentList '--online-ai-ui-test' `
    -WorkingDirectory $runRoot -WindowStyle Hidden -Wait
if (Test-Path -LiteralPath $uiError) {
    throw "联网AI设置界面渲染失败：$(Get-Content $uiError -Raw -Encoding UTF8)"
}
if (-not (Test-Path -LiteralPath $uiImage) -or (Get-Item -LiteralPath $uiImage).Length -lt 12000) {
    throw '联网AI设置界面截图未生成或内容为空'
}

Write-Output "联网AI设置：豆包默认、DeepSeek快速备选、智谱免费备用、自定义接口、$($prompt.Length)字符精简提示词、最多3路有序并发、词库术语、非思考最终输出、离线组件移除、请求取消与简明UI通过"
