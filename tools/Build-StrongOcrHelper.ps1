# Builds the standalone strong-OCR helper into tools\StrongOcrHelper\bin\<Configuration>\net48.
# The NuGet cache is fixed so nothing lands in a C: drive cache and no system NuGet configuration
# is changed.
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$NuGetCache = 'D:\GPT\cache\nuget',
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
    $stageRoot = Join-Path $repoRoot 'StrongOcr'
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
    Get-ChildItem -LiteralPath $output -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $stageRoot -Force
    }
    $licenseSource = Join-Path $helperRoot 'StrongOcrHelper\licenses'
    $licenseTarget = Join-Path $stageRoot 'licenses'
    New-Item -ItemType Directory -Path $licenseTarget -Force | Out-Null
    Get-ChildItem -LiteralPath $licenseSource -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $licenseTarget -Force
    }
    Write-Host "helper 已暂存：$stageRoot"
}
