$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot '枫语幕.exe'
$assembly = [Reflection.Assembly]::LoadFile($exe)
$all = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'

$settingsType = $assembly.GetType('MapleOverlay.OnlineAiSettings', $true)
$clientType = $assembly.GetType('MapleOverlay.OnlineAiClient', $true)
$presetType = $assembly.GetType('MapleOverlay.OnlineAiPresets', $true)
$settings = [Activator]::CreateInstance($settingsType, $true)
$provider = [string]$settingsType.GetField('Provider', $all).GetValue($settings)
$endpoint = [string]$settingsType.GetField('Endpoint', $all).GetValue($settings)
$model = [string]$settingsType.GetField('Model', $all).GetValue($settings)
$ready = [bool]$settingsType.GetProperty('IsReady', $all).GetValue($settings, $null)
if ($provider -ne '豆包 2.0 Lite（推荐）' -or
    $endpoint -ne 'https://ark.cn-beijing.volces.com/api/v3/responses' -or
    $model -ne 'doubao-seed-2-0-lite-260215' -or $ready) {
    throw '联网AI默认服务、接口、模型或未填Key状态不正确'
}
$settingsType.GetField('ApiKey', $all).SetValue($settings, 'test-key')
if (-not [bool]$settingsType.GetProperty('IsReady', $all).GetValue($settings, $null)) {
    throw '填写完整 HTTPS 接口、模型和 Key 后仍未进入联网就绪状态'
}

$presets = @($presetType.GetField('All', $all).GetValue($null))
$presetNames = @($presets | ForEach-Object { [string]$_.GetType().GetField('Name', $all).GetValue($_) })
foreach ($required in @('豆包 2.0 Lite（推荐）','DeepSeek V4 Flash（快速）','智谱 GLM-4-Flash（免费备用）','自定义兼容接口')) {
    if ($presetNames -notcontains $required) { throw "联网AI服务缺少：$required" }
}

$build = $clientType.GetMethod('BuildRequestBody', $all)
$describeHttp = $clientType.GetMethod('DescribeHttpFailure', $all)
$badKey = [string]$describeHttp.Invoke($null, [object[]]@(401, '', '豆包 2.0 Lite（推荐）'))
$noModel = [string]$describeHttp.Invoke($null, [object[]]@(403,
    '{"error":{"code":"PermissionDenied"}}', '豆包 2.0 Lite（推荐）'))
$noQuota = [string]$describeHttp.Invoke($null, [object[]]@(429, '', '豆包 2.0 Lite（推荐）'))
if (-not $badKey.Contains('不要填 Access Key 或 Secret Key') -or
    -not $noModel.Contains('模型未开通') -or
    -not $noModel.Contains('PermissionDenied') -or
    -not $noQuota.Contains('额度')) {
    throw '联网AI没有把密钥、模型权限和额度错误说明白'
}
$doubaoBody = [string]$build.Invoke($null, [object[]]@($settings,
    'which event is it i JUST went to orbis bro BriskIcedTea', '简体中文', "Orbis = 天空之城`n"))
foreach ($required in @('冒险岛怀旧服国际服老玩家','禁止逐词硬译','玩家ID','只输出一行最终译文',
    '不得漏译、重复或编造','Orbis = 天空之城','BriskIcedTea','"thinking":{"type":"disabled"}')) {
    if (-not $doubaoBody.Contains($required)) { throw "豆包逐句请求缺少：$required" }
}
foreach ($forbidden in @('本地AI译文','校对员')) {
    if ($doubaoBody.Contains($forbidden)) { throw "联网AI仍暴露中间复核工作：$forbidden" }
}

$settingsType.GetField('Endpoint', $all).SetValue($settings, 'https://api.deepseek.com/v1/chat/completions')
$settingsType.GetField('Model', $all).SetValue($settings, 'deepseek-v4-flash')
$deepSeekBody = [string]$build.Invoke($null, [object[]]@($settings,
    'How do you whisper back someone?', '简体中文', "whisper = 悄悄话`n"))
if (-not $deepSeekBody.Contains('"thinking":{"type":"disabled"}') -or
    -not $deepSeekBody.Contains('怎么回复别人的悄悄话')) {
    throw 'DeepSeek 快速非思考请求或冒险岛口语示例没有写入'
}

$settingsType.GetField('Endpoint', $all).SetValue($settings, 'https://open.bigmodel.cn/api/paas/v4/chat/completions')
$settingsType.GetField('Model', $all).SetValue($settings, 'glm-4-flash-250414')
$glmBody = [string]$build.Invoke($null, [object[]]@($settings, 'S> Kumbi 200k', '简体中文', "Kumbi = 雪花镖`n"))
if (-not $glmBody.Contains('"do_sample":false') -or -not $glmBody.Contains('Kumbi = 雪花镖')) {
    throw '智谱免费备用没有使用稳定输出或冒险岛词库术语'
}

$chatSource = Get-Content (Join-Path $repoRoot 'src\OfflineChat.cs') -Raw -Encoding UTF8
$overlaySource = Get-Content (Join-Path $repoRoot 'src\MapleOverlay.cs') -Raw -Encoding UTF8
foreach ($required in @('联网AI实时翻译（不用租服务器）','小白教程：','打开申请页面','保存并测试',
    '不用租服务器，也不用每次打开网页','接口地址：','模型名称：','API Key：',
    '不要填 Access Key 或 Secret Key','DescribeFailure(ex, settings)',
    'TranslateWithPreferredAiAsync','连接失败，自动改用离线备用')) {
    if (-not $chatSource.Contains($required)) { throw "联网AI简明界面或主链路缺少：$required" }
}
foreach ($removed in @('联网复核（可选）','单次识别翻译（兼容模式）','审核AI纠错','加入纠错候选')) {
    if ($chatSource.Contains($removed)) { throw "联网AI界面仍残留旧入口：$removed" }
}
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

$uiError = Join-Path $repoRoot 'online_ai_ui_test_error.txt'
$uiImage = Join-Path $repoRoot 'online_ai_ui_test.png'
Remove-Item -LiteralPath $uiError -Force -ErrorAction SilentlyContinue
Start-Process -FilePath $exe -ArgumentList '--online-ai-ui-test' `
    -WorkingDirectory $repoRoot -WindowStyle Hidden -Wait
if (Test-Path -LiteralPath $uiError) {
    throw "联网AI设置界面渲染失败：$(Get-Content $uiError -Raw -Encoding UTF8)"
}
if (-not (Test-Path -LiteralPath $uiImage) -or (Get-Item -LiteralPath $uiImage).Length -lt 12000) {
    throw '联网AI设置界面截图未生成或内容为空'
}

Write-Output '联网AI设置：豆包默认、DeepSeek快速备选、智谱免费备用、自定义接口、逐句冒险岛提示词、词库术语、非思考最终输出、离线兜底与简明UI通过'
