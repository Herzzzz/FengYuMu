$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Add-Type -Path @((Join-Path $root 'src/CaptureProcessSession.cs'),(Join-Path $root 'src/UpdateFileTransaction.cs'),
 (Join-Path $root 'src/StrongOcrComponent.cs'),(Join-Path $root 'src/StrongOcrValidationCache.cs'),(Join-Path $PSScriptRoot 'FollowupSafetyHarness.cs'))
$temp=Join-Path $root ('.bench/followup-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $temp | Out-Null
try {
    $fixture=Join-Path $temp 'worker.ps1'
    @'
param([string]$PidFile)
[IO.File]::WriteAllText($PidFile,[string]$PID)
[Console]::WriteLine('READY'); [Console]::Out.Flush()
while ($null -ne ($command=[Console]::ReadLine())) {
    if ($command -eq 'HANG') { Start-Sleep -Seconds 15 }
    [Console]::WriteLine('OK:'+$command); [Console]::Out.Flush()
}
'@ | Set-Content -LiteralPath $fixture -Encoding UTF8
    $shell=(Get-Process -Id $PID).Path
    $count=[FollowupSafetyHarness]::Run($temp,$shell,$fixture).GetAwaiter().GetResult()
    if ($count -ne 11) { throw 'Unexpected safety check count' }
    Write-Output 'Followup safety: 11/11 passed; real subprocess/filesystem behavior, native PrintWindow and OCR model recognition unverified.'
} finally { Remove-Item -LiteralPath $temp -Recurse -Force }
