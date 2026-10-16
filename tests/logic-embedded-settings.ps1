param([string]$ExePath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([String]::IsNullOrWhiteSpace($ExePath)) { $ExePath = Join-Path $repoRoot 'bin/candidate/枫语幕.exe' }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
$assembly = Load-TestAssembly $ExePath
$flags = [Reflection.BindingFlags]'Static,Instance,NonPublic,Public'
$store = $assembly.GetType('MapleOverlay.UserSettings',$true)
$pathField = $store.GetField('FilePath',$flags)
$originalPath = $pathField.GetValue($null)
$temporary = Join-Path $repoRoot ('.bench/embedded-settings-' + [Guid]::NewGuid().ToString('N'))
$pathField.SetValue($null,(Join-Path $temporary 'settings.dat'))
$section = $assembly.GetType('MapleOverlay.UserSettingsSection',$true)
$ctor = $section.GetConstructors($flags)[0]
$set = $section.GetMethod('SetValue',$flags,$null,[Type[]]@([string],[object]),$null)
$fixture = $ctor.Invoke([object[]]@($pathField.GetValue($null),'Software\FengYuMu\OnlineAI',$null))
foreach ($pair in @(@('Provider','DeepSeek V4 Flash（快速）'),@('Endpoint','https://api.deepseek.com/v1'),
    @('Model','deepseek-v4-flash'),@('ApiKey',''),@('PanelTranslationEnabled','0'))) {
    $set.Invoke($fixture,[object[]]@($pair[0],$pair[1])) | Out-Null
}
$fixture.Dispose()
$rootSettings = $ctor.Invoke([object[]]@($pathField.GetValue($null),'Software\FengYuMu',$null))
$set.Invoke($rootSettings,[object[]]@('MainWindowSnapEnabled','0')) | Out-Null
$rootSettings.Dispose()
$main = $null
try {
    $settingsType = $assembly.GetType('MapleOverlay.OnlineAiSettings',$true)
    $settings = $settingsType.GetMethod('Load',$flags).Invoke($null,@())
    if ($settingsType.GetField('Endpoint',$flags).GetValue($settings) -ne 'https://api.deepseek.com/v1/chat/completions' -or
        $settingsType.GetField('Model',$flags).GetValue($settings) -ne 'deepseek-chat' -or
        $settingsType.GetField('Provider',$flags).GetValue($settings) -ne 'DeepSeek（开放平台）') {
        throw 'Legacy provider/model or base endpoint migration failed'
    }
    Write-Output 'PASS: actual file-backed settings load normalizes base endpoint and migrates shipped legacy preset'
    $mainType = $assembly.GetType('MapleOverlay.MainPanelForm',$true)
    $main = $mainType.GetConstructors($flags)[0].Invoke([object[]]@($null))
    $main.Show(); [Windows.Forms.Application]::DoEvents()
    $method = $mainType.GetMethod('ShowEmbeddedPage',$flags)
    foreach ($name in @('MapleOverlay.HotkeyForm','MapleOverlay.OnlineAiForm')) {
        $childType = $assembly.GetType($name,$true)
        $child = if ($name.EndsWith('HotkeyForm')) { $childType.GetConstructors($flags)[0].Invoke([object[]]@($null)) }
            else { [Activator]::CreateInstance($childType,$true) }
        $method.Invoke($main,[object[]]@($child,$null)) | Out-Null
        [Windows.Forms.Application]::DoEvents()
        if ($child.TopLevel -or $child.ShowInTaskbar -or $child.TopLevelControl -ne $main) {
            throw "$name opened outside the main window"
        }
        $topLevel = @([Windows.Forms.Application]::OpenForms | Where-Object { $_.TopLevel -and $_.Visible })
        if ($topLevel.Count -ne 1 -or $topLevel[0] -ne $main) { throw 'Extra top-level application window exists' }
        $child.Close(); [Windows.Forms.Application]::DoEvents()
        if ($main.IsDisposed -or -not $main.Visible) { throw 'Back navigation closed the main window' }
        Write-Output "PASS: $name uses one main window and returns without closing it"
    }
    $main.Size = [Drawing.Size]::new(600,850)
    $mainType.GetMethod('EndUserWindowInteraction',$flags).Invoke($main,@()) | Out-Null
    $before = $main.Size
    $mainType.GetMethod('ApplyDpiLayout',$flags).Invoke($main,[object[]]@(144,$true,$false)) | Out-Null
    if ($main.Size -ne $before) { throw 'DPI layout reset a manually committed window size' }
    Write-Output 'PASS: DPI content layout preserves manually committed outer size'
    Write-Output 'Embedded settings: 4/4 passed; real WinForms/file-backed paths, physical drag/keys/game/provider unverified.'
} finally {
    if ($null -ne $main) { $main.Dispose() }
    $pathField.SetValue($null,$originalPath)
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
