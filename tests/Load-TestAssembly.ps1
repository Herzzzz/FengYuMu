# Shared test helper: load the built 枫语幕.exe for reflection-based assertions.
#
# Why this exists:
# This repository lives under a path that contains non-ASCII directory names
# (D:\GPT\文件\枫语幕\...). For a path like that the CLR's Assembly.LoadFile treats the
# assembly as coming from a remote location and refuses it:
#
#   An attempt was made to load an assembly from a network location ...
#
# The same bytes load fine from an ASCII-only path, and the file carries no
# Zone.Identifier stream, so this is an environment quirk rather than a real trust
# boundary. A cross experiment over {old,new} content x {ASCII,CJK} directory x
# {ASCII,CJK} filename showed the directory is the only variable that matters.
#
# UnsafeLoadFrom is the documented API for exactly this case ("the caller vouches that
# the assembly is local, skip the zone check"). It is used *only* as a fallback, and no
# assertion in any test is relaxed by it: the same assembly, the same reflection, the
# same expectations. If the CLR ever stops mis-judging the path, the plain LoadFile
# branch is taken and the fallback never runs.

$script:TestAssemblyRepositoryRoot = Split-Path -Parent $PSScriptRoot
function Load-TestAssembly([string]$path) {
    if ([String]::IsNullOrEmpty($path)) { throw 'Load-TestAssembly 需要一个程序集路径' }
    if (-not (Test-Path -LiteralPath $path)) { throw "找不到程序集：$path" }
    # Build synchronization gate. A reflection test against a stale EXE silently validates the
    # previous build: the sources and the artefact can drift apart while the suite still passes.
    # The assembly therefore has to be at least as new as every source file it was built from.
    $path = (Resolve-Path -LiteralPath $path).Path
    $repoRoot = $script:TestAssemblyRepositoryRoot
    $srcDir = Join-Path $repoRoot 'src'
    if (Test-Path -LiteralPath $srcDir) {
        $assemblyTime = (Get-Item -LiteralPath $path).LastWriteTimeUtc
        $newest = Get-ChildItem -LiteralPath $srcDir -Filter '*.cs' |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if ($null -ne $newest -and $newest.LastWriteTimeUtc -gt $assemblyTime) {
            throw ("程序集比源码旧：$($newest.Name) 晚于程序集构建时间。请先运行 src\build.ps1。")
        }
    }
    . (Join-Path $repoRoot 'tools/Build-Evidence.ps1')
    Assert-BuildEvidence $path $repoRoot
    try {
        return [Reflection.Assembly]::LoadFile($path)
    } catch [System.NotSupportedException] {
        if ($_.Exception.Message -notmatch 'network location') { throw }
        Write-Verbose "路径被判为网络位置，改用 UnsafeLoadFrom：$path"
        return [Reflection.Assembly]::UnsafeLoadFrom($path)
    }
}
