param([Parameter(Mandatory=$true)][string]$ExePath,[string]$Tier='')
$ErrorActionPreference='Stop'
if([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT){throw 'Requires native 64-bit Windows and Windows PowerShell 5.1 (.NET Framework 4.8); not executed on Linux.'}
if(-not [Environment]::Is64BitProcess){throw 'Run 64-bit Windows PowerShell.'}
if([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA'){throw 'Run powershell.exe -STA -NoProfile -File this-script.ps1 ...'}
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Web.Extensions
$exe=(Resolve-Path -LiteralPath $ExePath).Path
$assembly=[Reflection.Assembly]::LoadFrom($exe)
$flags=[Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
# Start the actual executable so AppDomain.BaseDirectory, dictionary and component
# paths match real use. Loading WinForms inside powershell would use PS's base path.
$ui=Start-Process -FilePath $exe -ArgumentList '--image-test' -PassThru
try{
 $watch=[Diagnostics.Stopwatch]::StartNew()
 do{Start-Sleep -Milliseconds 100;$ui.Refresh()}while(-not $ui.HasExited -and $ui.MainWindowHandle -eq 0 -and $watch.ElapsedMilliseconds -lt 15000)
 if($ui.HasExited -or $ui.MainWindowHandle -eq 0 -or $ui.MainWindowTitle -notmatch '本地图片 OCR 测试'){throw 'Actual picture window failed to open'}
 Write-Output 'PASS: actual executable opens local picture window without game login'
 [void]$ui.CloseMainWindow()
 if(-not $ui.WaitForExit(5000)){throw 'Picture application did not close normally'}
}finally{try{if(-not $ui.HasExited){$ui.Kill()}}catch{};$ui.Dispose()}
$folder=Join-Path (Split-Path -Parent $exe) 'PrimaryOcr'
if([string]::IsNullOrEmpty($Tier)){$Tier=if(Test-Path (Join-Path $folder 'Sdcb.SimdPaddleOCR.Models.ChineseV6Medium.dll')){'Medium'}else{'Small'}}
if($Tier -notin @('Small','Medium')){throw 'Tier must be Small or Medium'}
$validator=$assembly.GetType('MapleOverlay.PrimaryOcrClient',$true).GetMethod('Validate',$flags)
if(-not $validator.Invoke($null,[object[]]@($folder))){throw 'Model component manifest failed'}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('fengyumu-r10-smoke-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp|Out-Null
$process=$null
try{
 $png=Join-Path $temp 'local-generated-fixture.png'
 $bitmap=New-Object Drawing.Bitmap 500,120
 try{
  $g=[Drawing.Graphics]::FromImage($bitmap)
  $font=New-Object Drawing.Font 'Consolas',28
  try{$g.Clear([Drawing.Color]::White);$g.DrawString('LUK +2',$font,[Drawing.Brushes]::Black,15,25);$bitmap.Save($png,[Drawing.Imaging.ImageFormat]::Png)}finally{$font.Dispose();$g.Dispose()}
 }finally{$bitmap.Dispose()}
 $start=New-Object Diagnostics.ProcessStartInfo
 $start.FileName=Join-Path $folder 'PrimaryOcrHelper.exe';$start.Arguments='--server '+$Tier
 $start.WorkingDirectory=$folder;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
 $start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true
 $start.StandardOutputEncoding=[Text.UTF8Encoding]::new($false)
 $process=[Diagnostics.Process]::Start($start)
 $ready=$process.StandardOutput.ReadLineAsync()
 if(-not $ready.Wait(15000) -or $ready.Result -ne 'READY'){throw 'Model startup failed or exceeded product limit'}
 Write-Output ('PASS: native '+$Tier+' model initializes')
 $json=(@{path=$png}|ConvertTo-Json -Compress)
 $request=$assembly.GetType('MapleOverlay.PrimaryOcrClient',$true).GetMethod('AsciiJson',$flags).Invoke($null,[object[]]@($json))
 $process.StandardInput.WriteLine($request);$process.StandardInput.Flush()
 $response=$process.StandardOutput.ReadLineAsync()
 if(-not $response.Wait(60000)){throw 'Recognition exceeded product limit'}
 $data=$response.Result|ConvertFrom-Json
 if(-not $data.ok -or @($data.lines).Count -eq 0 -or -not (($data.lines.text -join ' ') -match '2')){throw 'PNG/helper/model recognition failed'}
 $parse=$assembly.GetType('MapleOverlay.OcrResult',$true).GetMethod('ParsePaddle',$flags)
 $decoded=$parse.Invoke($null,[object[]]@($response.Result,[Drawing.Size]::new(500,120)))
 if($decoded.GetType().GetField('Lines',$flags).GetValue($decoded).Count -eq 0){throw 'Framework JSON/word geometry rejected actual model payload'}
 Write-Output 'PASS: native PNG -> model -> JSON -> actual app geometry validation'
 Write-Output 'This is a generated fixture smoke check; real game capture/F8/Chinese fusion still require desktop testing.'
}finally{
 if($process){try{if(-not $process.HasExited){$process.Kill()}}catch{};$process.Dispose()}
 Remove-Item -LiteralPath $temp -Recurse -Force
}
