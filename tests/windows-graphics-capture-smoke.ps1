param([Parameter(Mandatory=$true)][string]$ExePath,[long]$WindowHandle=0)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Load-TestAssembly.ps1')
$assembly=Load-TestAssembly $ExePath
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class CaptureSmokeNativeR3 {
 [StructLayout(LayoutKind.Sequential)]public struct Rect{public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)]public struct Point{public int X,Y;}
 [DllImport("user32.dll")]public static extern bool GetClientRect(IntPtr window,out Rect rectangle);
 [DllImport("user32.dll")]public static extern bool ClientToScreen(IntPtr window,ref Point point);
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr window);
 [DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
'@
if($WindowHandle -eq 0){
 $games=@(Get-Process | Where-Object {$_.ProcessName -match '^MapleStory' -and $_.ProcessName -notmatch 'launcher|patcher|updater|setup' -and $_.MainWindowHandle -ne [IntPtr]::Zero})
 if($games.Count -ne 1){throw 'Need exactly one real MapleStory client, or explicit -WindowHandle.'}
 $WindowHandle=$games[0].MainWindowHandle.ToInt64()
}
$handle=[IntPtr]::new($WindowHandle);$gameProcessId=[uint32]0
[CaptureSmokeNativeR3]::GetWindowThreadProcessId($handle,[ref]$gameProcessId)|Out-Null
$process=Get-Process -Id $gameProcessId
if($process.ProcessName -notmatch '^MapleStory' -or $process.ProcessName -match 'launcher|patcher|updater|setup'){throw 'Target must be an actual MapleStory process.'}
if(-not [CaptureSmokeNativeR3]::IsWindowVisible($handle) -or [CaptureSmokeNativeR3]::IsIconic($handle)){throw 'Keep the game visible and windowed.'}
$priorDpi=[CaptureSmokeNativeR3]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
if($priorDpi -eq [IntPtr]::Zero){throw 'Cannot establish physical-pixel coordinates for native smoke test.'}
$capture=$null
try {
 $rect=[CaptureSmokeNativeR3+Rect]::new();$point=[CaptureSmokeNativeR3+Point]::new()
 if(-not [CaptureSmokeNativeR3]::GetClientRect($handle,[ref]$rect) -or -not [CaptureSmokeNativeR3]::ClientToScreen($handle,[ref]$point)){throw 'Cannot measure game client.'}
 $bounds=[Drawing.Rectangle]::new($point.X,$point.Y,$rect.Right-$rect.Left,$rect.Bottom-$rect.Top)
 $flags=[Reflection.BindingFlags]'Public,NonPublic,Instance'
 $type=$assembly.GetType('MapleOverlay.GraphicsWindowCapture',$true)
 $capture=[Activator]::CreateInstance($type,$true)
 for($i=1;$i -le 3;$i++){
  $watch=[Diagnostics.Stopwatch]::StartNew();$bitmap=$null
  try{
   $bitmap=$type.GetMethod('Capture',$flags).Invoke($capture,[object[]]@($handle,$gameProcessId,$bounds))
   if($bitmap.Size -ne $bounds.Size){throw 'Returned frame is not the measured client size.'}
   $colors=New-Object 'System.Collections.Generic.HashSet[int]'
   for($y=0;$y -lt $bitmap.Height;$y+=[Math]::Max(1,[int]($bitmap.Height/12))){for($x=0;$x -lt $bitmap.Width;$x+=[Math]::Max(1,[int]($bitmap.Width/24))){$colors.Add($bitmap.GetPixel($x,$y).ToArgb())|Out-Null}}
   if($colors.Count -lt 2){throw 'Rendered game pixels absent or protected; cannot count this as usable capture.'}
   Write-Output "PASS: native target frame $i width=$($bitmap.Width) height=$($bitmap.Height) elapsedMs=$($watch.ElapsedMilliseconds) sampledColors=$($colors.Count)"
  }finally{if($null -ne $bitmap){$bitmap.Dispose()}}
 }
 Write-Output 'Native graphics capture: 3/3 passed; backend smoke only. Does not prove physical F8, OCR, occlusion guard or fusion correctness.'
}finally{
 if($null -ne $capture){$capture.Dispose()}
 [CaptureSmokeNativeR3]::SetThreadDpiAwarenessContext($priorDpi)|Out-Null
}
