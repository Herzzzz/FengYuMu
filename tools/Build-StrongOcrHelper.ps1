# Builds the standalone strong-OCR helper into tools\StrongOcrHelper\bin\<Configuration>\net48.
# The NuGet cache is fixed so nothing lands in a C: drive cache and no system NuGet configuration
# is changed.
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$NuGetCache = (Join-Path $PSScriptRoot '../bin/nuget-cache'),
    [switch]$Stage
)
$ErrorActionPreference = 'Stop'
$env:NUGET_PACKAGES = $NuGetCache
$helperRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $helperRoot 'StrongOcrHelper\StrongOcrHelper.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw "找不到 helper 工程：$project" }
& dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw "dotnet restore 失败：$LASTEXITCODE" }
& dotnet build $project -c $Configuration -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet build 失败：$LASTEXITCODE" }
$output = Join-Path $helperRoot "StrongOcrHelper\bin\$Configuration\net48"
$total = (Get-ChildItem -LiteralPath $output -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("helper 输出：{0}（{1:N2} MiB）" -f $output, ($total / 1MB))
if ($Stage) {
    $repoRoot = Split-Path -Parent $helperRoot
    $stageDestination = Join-Path $repoRoot 'StrongOcr'
    $stageRoot = Join-Path $repoRoot ('bin/strongocr-stage-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
    Get-ChildItem -LiteralPath $output -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($output.TrimEnd('\','/').Length + 1)
        $destination = Join-Path $stageRoot $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
    }
    $licenseSource = Join-Path $helperRoot 'StrongOcrHelper\licenses'
    $licenseTarget = Join-Path $stageRoot 'licenses'
    New-Item -ItemType Directory -Path $licenseTarget -Force | Out-Null
    Get-ChildItem -LiteralPath $licenseSource -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $licenseTarget -Force
    }
    $manifest = Join-Path $stageRoot 'component-manifest.tsv'
    $lines = @('# FengYuMu StrongOcr component 1')
    $lines += @(Get-ChildItem -LiteralPath $stageRoot -Recurse -File | Where-Object { $_.FullName -ne $manifest } | Sort-Object FullName | ForEach-Object {
        $relative = $_.FullName.Substring($stageRoot.TrimEnd('\','/').Length + 1).Replace('\','/')
        $relative + "`t" + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
    $lines | Set-Content -LiteralPath $manifest -Encoding UTF8
    if (Test-Path -LiteralPath $stageDestination) {
        $previous = Join-Path $repoRoot ('bin/strongocr-previous-' + [Guid]::NewGuid().ToString('N'))
        Move-Item -LiteralPath $stageDestination -Destination $previous
    }
    Move-Item -LiteralPath $stageRoot -Destination $stageDestination
    Write-Host "helper 已暂存：$stageDestination"
}
