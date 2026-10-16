$ErrorActionPreference = 'Stop'
function Restore-WindowsBuildReferences([string]$Destination) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $packages = @(
        @('microsoft.netframework.referenceassemblies.net48','1.0.3','8A7E348538E7EB91351696911689F49E3D4F63F8BAB517432BBE159B8B1104A2'),
        @('microsoft.windows.sdk.contracts','10.0.19041.1','7DC674587605F02B0B22266071834DD6E23EFAEED0D615C2A7F1C2601C18698D'),
        @('system.runtime.windowsruntime','4.7.0','E36BC23B21D5C14372706D3098F0BF5E383F7F98D1B36F02EAB31184C37EB8EC')
    )
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($package in $packages) {
        $name = $package[0]; $version = $package[1]; $expected = $package[2]
        $archive = Join-Path $Destination "$name.$version.nupkg"
        if (-not (Test-Path $archive) -or (Get-FileHash $archive -Algorithm SHA256).Hash -ne $expected) {
            Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$name/$version/$name.$version.nupkg" -OutFile $archive -UseBasicParsing
        }
        if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $expected) { throw "构建引用包校验失败：$name $version" }
        $expanded = Join-Path $Destination $name
        # Re-extract verified bytes; a previously modified cache cannot affect a build.
        if (Test-Path $expanded) { Remove-Item $expanded -Recurse -Force }
        [IO.Compression.ZipFile]::ExtractToDirectory($archive, $expanded)
    }
    return [IO.Path]::GetFullPath($Destination)
}
