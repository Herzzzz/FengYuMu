param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(-not $OutputDirectory){$OutputDirectory=Join-Path $root 'bin/primary-ocr/PrimaryOcr'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a new output directory; existing components are preserved.'}
$stage=Join-Path ([IO.Path]::GetTempPath()) ('fengyumu-primary-build-'+[Guid]::NewGuid().ToString('N'))
try{
 & dotnet build (Join-Path $PSScriptRoot 'PrimaryOcrHelper/PrimaryOcrHelper.csproj') -c Release -p:Platform=x64 -o $stage
 if($LASTEXITCODE -ne 0){throw 'Primary OCR helper build failed'}
 New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
 foreach($file in Get-ChildItem $stage -File){if($file.Extension -eq '.exe' -or $file.Extension -eq '.dll' -or $file.Name -like '*.exe.config'){Copy-Item $file.FullName $OutputDirectory}}
 Copy-Item (Join-Path $PSScriptRoot 'PrimaryOcrHelper/licenses') (Join-Path $OutputDirectory 'licenses') -Recurse
 $base=[IO.Path]::GetFullPath($OutputDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
 $rows=Get-ChildItem $OutputDirectory -File -Recurse|Sort-Object FullName|ForEach-Object{
  $_.FullName.Substring($base.Length).Replace([IO.Path]::DirectorySeparatorChar,'/')+"`t"+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
 }
 [IO.File]::WriteAllLines((Join-Path $OutputDirectory 'model-manifest.tsv'),[string[]]$rows,[Text.UTF8Encoding]::new($false))
 Write-Output 'Built PrimaryOcr directory with Small/Medium models. Copy the complete directory beside the newly built main EXE. Native execution is a separate test.'
}finally{if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}}
