# Shared test helper: start 枫语幕.exe without triggering the attachment-manager warning.
#
# Why this exists:
# The repository lives under a path with non-ASCII directory names, and Windows classifies
# that path as being in the Internet zone. When a test starts the built EXE through
# Start-Process (which goes through ShellExecute), the attachment manager therefore shows
# an "Open File - Security Warning / Unknown Publisher" prompt for every launch. With 29
# launch sites across the suite that dialog interrupts the run repeatedly.
#
# This helper starts the process directly through CreateProcess (UseShellExecute = false),
# which is also the more correct way to launch a build artifact under test: no shell, no
# document-association resolution, no security prompt.
#
# It is a drop-in replacement for the patterns used in the suite:
#     Start-Process -FilePath $exe -ArgumentList @(...) -WorkingDirectory $dir -Wait
#     $p = Start-Process -FilePath $exe -ArgumentList @(...) -PassThru -NoNewWindow
# Output is suppressed unless -PassThru is given, matching Start-Process behaviour.

function Start-ProductExe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [object]$ArgumentList = $null,
        [string]$WorkingDirectory = $null,
        [string]$RedirectStandardOutput = $null,
        [string]$RedirectStandardError = $null,
        [string]$WindowStyle = $null,
        [switch]$Wait,
        [switch]$PassThru,
        [switch]$NoNewWindow
    )

    if ([String]::IsNullOrEmpty($WorkingDirectory)) {
        $WorkingDirectory = Split-Path -Parent $FilePath
    }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false

    # -WindowStyle is accepted for call-site compatibility with Start-Process. It is a
    # no-op here: WindowStyle and CreateNoWindow are mutually exclusive, and hiding the
    # console is the behaviour these call sites actually rely on.
    if (-not [String]::IsNullOrEmpty($WindowStyle)) { $psi.CreateNoWindow = $true }
    if ($NoNewWindow) { $psi.CreateNoWindow = $true }

    $captureStdout = -not [String]::IsNullOrEmpty($RedirectStandardOutput)
    $captureStderr = -not [String]::IsNullOrEmpty($RedirectStandardError)
    if ($captureStdout) { $psi.RedirectStandardOutput = $true }
    if ($captureStderr) { $psi.RedirectStandardError = $true }

    # Normalise the argument list the same way Start-Process does: either a single string
    # that already holds all switches, or an array of individual tokens.
    if ($null -ne $ArgumentList) {
        if ($ArgumentList -is [string]) {
            $psi.Arguments = [string]$ArgumentList
        } else {
            $parts = @()
            foreach ($item in @($ArgumentList)) {
                $text = [string]$item
                if ($text -match '\s' -and $text -notmatch '^".*"$') { $text = '"' + $text + '"' }
                $parts += $text
            }
            $psi.Arguments = ($parts -join ' ')
        }
    }

    $process = [System.Diagnostics.Process]::Start($psi)

    # Begin async reads before waiting. A synchronous ReadToEnd on a full pipe buffer would
    # deadlock, which matters because some product test modes emit long reports.
    $stdoutTask = $null
    $stderrTask = $null
    if ($captureStdout) { $stdoutTask = $process.StandardOutput.ReadToEndAsync() }
    if ($captureStderr) { $stderrTask = $process.StandardError.ReadToEndAsync() }

    if ($Wait) { $process.WaitForExit() }

    if ($captureStdout) {
        $text = if ($stdoutTask -and $stdoutTask.Wait(30000)) { $stdoutTask.Result } else { '' }
        [System.IO.File]::WriteAllText($RedirectStandardOutput, $text, (New-Object System.Text.UTF8Encoding($false)))
    }
    if ($captureStderr) {
        $text = if ($stderrTask -and $stderrTask.Wait(30000)) { $stderrTask.Result } else { '' }
        [System.IO.File]::WriteAllText($RedirectStandardError, $text, (New-Object System.Text.UTF8Encoding($false)))
    }

    # -PassThru must hand back the live Process object. Write-Output -NoEnumerate prevents
    # PowerShell from unrolling it, which would otherwise turn the return value into a
    # collection and make .ExitCode unavailable to the caller.
    if ($PassThru) { Write-Output -NoEnumerate $process; return }
    $process.Dispose()
    return
}
