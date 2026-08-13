$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$winmd = 'C:\Windows\System32\WinMetadata'

& $csc /nologo /target:winexe /optimize+ /platform:anycpu /win32manifest:"$scriptDir\app.manifest" /out:"$repoRoot\枫语幕.exe" `
  /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
  /reference:System.Windows.Forms.dll `
  /reference:System.Web.Extensions.dll `
  /reference:System.Security.dll `
  /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll `
  /reference:"$framework\System.Runtime.dll" `
  /reference:"$framework\System.Runtime.WindowsRuntime.dll" `
  /reference:"$winmd\Windows.Foundation.winmd" `
  /reference:"$winmd\Windows.Globalization.winmd" `
  /reference:"$winmd\Windows.Graphics.winmd" `
  /reference:"$winmd\Windows.Media.winmd" `
  /reference:"$winmd\Windows.Storage.winmd" `
  "$scriptDir\MapleOverlay.cs" "$scriptDir\SimpleForms.cs" "$scriptDir\OfflineChat.cs"

if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }
Write-Host "编译完成: $((Resolve-Path (Join-Path $repoRoot '枫语幕.exe')).Path)"
