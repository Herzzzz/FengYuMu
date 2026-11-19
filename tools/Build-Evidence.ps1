$script:BuildEvidenceRoot = Split-Path -Parent $PSScriptRoot

function Get-BuildInputs([string]$RepositoryRoot = $script:BuildEvidenceRoot) {
    $inputs = @(Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'src') -File |
        Where-Object { $_.Extension -in @('.cs', '.ico', '.manifest', '.ps1') } |
        Sort-Object Name | ForEach-Object {
            [pscustomobject]@{ path = 'src/' + $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        })
    if ($inputs.Count -eq 0) { throw '未找到构建输入' }
    return $inputs
}

function Write-BuildEvidence([string]$ExePath, [string]$RepositoryRoot = $script:BuildEvidenceRoot,
    [string]$Compiler = 'Windows .NET Framework csc.exe') {
    # Source evidence archives intentionally omit .git. Native builds from that
    # snapshot still record all input hashes without pretending it is a clean repo.
    $head = $null
    $worktree = @('Source snapshot; no Git metadata')
    $hasGit = (Test-Path -LiteralPath (Join-Path $RepositoryRoot '.git')) -and
        ($null -ne (Get-Command git -ErrorAction SilentlyContinue))
    if ($hasGit) {
        $head = (& git -C $RepositoryRoot rev-parse HEAD)
        $worktree = @(& git -C $RepositoryRoot status --short --untracked-files=all)
    }
    $evidence = [ordered]@{
        format = 1
        builtAtUtc = [DateTime]::UtcNow.ToString('o')
        compiler = $Compiler
        exeSha256 = (Get-FileHash -LiteralPath $ExePath -Algorithm SHA256).Hash
        inputs = @(Get-BuildInputs $RepositoryRoot)
        head = $head
        worktree = $worktree
        repositoryMetadataAvailable = $hasGit
    }
    $evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath ($ExePath + '.build.json') -Encoding UTF8
    return $evidence
}

function Assert-BuildEvidence([string]$ExePath, [string]$RepositoryRoot = $script:BuildEvidenceRoot) {
    $recordPath = $ExePath + '.build.json'
    if (-not (Test-Path -LiteralPath $recordPath)) { throw '产物缺少构建输入记录；请从当前源码运行 src/build.ps1' }
    $record = Get-Content -LiteralPath $recordPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($record.exeSha256 -ne (Get-FileHash -LiteralPath $ExePath -Algorithm SHA256).Hash) {
        throw 'EXE 哈希与构建记录不一致'
    }
    $current = @(Get-BuildInputs $RepositoryRoot)
    if ($current.Count -ne @($record.inputs).Count) { throw '构建输入文件集合已变化' }
    foreach ($inputFile in $current) {
        $saved = @($record.inputs | Where-Object path -eq $inputFile.path)
        if ($saved.Count -ne 1 -or $saved[0].sha256 -ne $inputFile.sha256) {
            throw "源码与产物漂移：$($inputFile.path)；请重新构建"
        }
    }
}
