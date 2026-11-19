$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
function Get-ActualMethod([string]$text,[string]$signature) {
 $start=$text.IndexOf($signature,[StringComparison]::Ordinal)
 if($start -lt 0){throw "Missing production method: $signature"}
 $open=$text.IndexOf('{',$start);$depth=1;$end=$open+1
 while($depth -gt 0 -and $end -lt $text.Length){if($text[$end] -eq '{'){$depth++};if($text[$end] -eq '}'){$depth--};$end++}
 return $text.Substring($start,$end-$start)
}
$frame=[IO.File]::ReadAllText((Join-Path $root 'src/OcrFrame.cs'))
$parse=Get-ActualMethod $frame '        internal static OcrResult ParsePaddle'
$frame=$frame.Replace($parse,'').Replace('using System.Web.Script.Serialization;','')
$client=[IO.File]::ReadAllText((Join-Path $root 'src/PrimaryOcrClient.cs'))
$validate=Get-ActualMethod $client '        internal static bool Validate'
$ascii=Get-ActualMethod $client '        internal static string AsciiJson'
$strong=[IO.File]::ReadAllText((Join-Path $root 'src/StrongOcrClient.cs'))
$normalize=Get-ActualMethod $strong '        internal static double NormalizeConfidence'
$finite=Get-ActualMethod $strong '        private static bool IsFinite'
$extra='namespace MapleOverlay { internal class StrongOcrClient { internal const float MinimumConfidence=0.42f;'+$normalize+$finite+'} internal class PrimaryOcrClient {'+$validate+$ascii+'} }'
$tx=[IO.File]::ReadAllText((Join-Path $root 'src/UpdateFileTransaction.cs'))
$harness=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'PrimaryOcrProtocolHarness.cs'))
Add-Type -TypeDefinition ('#pragma warning disable CS0649'+"`n"+'using System;using System.IO;using System.Text;using System.Collections;using System.Collections.Generic;using System.Drawing;using System.Security.Cryptography;'+
 ($frame -replace '(?m)^using .*;\r?\n','')+($tx -replace '(?m)^using .*;\r?\n','')+$extra+$harness)
$count=[PrimaryOcrProtocolHarness]::Run()
Write-Output "Primary OCR protocol: $count passed; exact production DTO validation/component hashes. Framework JSON decoding, PNG/GDI/UI require native Windows."
