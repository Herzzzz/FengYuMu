$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot 'tools/Build-Evidence.ps1')
$temporary=Join-Path $repoRoot ('.bench/source-snapshot-'+[Guid]::NewGuid().ToString('N'))
try {
 New-Item -ItemType Directory -Path (Join-Path $temporary 'src') -Force|Out-Null
 [IO.File]::WriteAllText((Join-Path $temporary 'src/fixture.cs'),'fixture-a')
 $exe=Join-Path $temporary 'fixture.bin';[IO.File]::WriteAllText($exe,'binary fixture; not a compiled product')
 $record=Write-BuildEvidence $exe $temporary 'fixture compiler'
 if($record.repositoryMetadataAvailable -or $null -ne $record.head -or @($record.inputs).Count -ne 1){throw 'Snapshot metadata misrepresented'}
 Write-Output 'PASS: source archive without .git creates explicit snapshot build record'
 Assert-BuildEvidence $exe $temporary
 Write-Output 'PASS: snapshot build record validates its actual file/input hashes'
 [IO.File]::WriteAllText((Join-Path $temporary 'src/fixture.cs'),'fixture-b')
 $rejected=$false
 try {Assert-BuildEvidence $exe $temporary}catch{$rejected=$true}
 if(-not $rejected){throw 'Snapshot source drift passed'}
 Write-Output 'PASS: source drift is rejected even without Git metadata'
 Write-Output 'Snapshot build evidence: 3/3 passed; filesystem fixtures only, no product build claimed.'
} finally {if(Test-Path $temporary){Remove-Item -LiteralPath $temporary -Recurse -Force}}
