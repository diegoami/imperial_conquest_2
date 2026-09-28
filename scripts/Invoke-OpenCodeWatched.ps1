<#
.SYNOPSIS
    Runs `opencode run` as a watched child process: it fails fast when no session starts, and it
    never waits past a total deadline. Dot-source this file, then call Invoke-OpenCodeWatched.

.DESCRIPTION
    The 2026-09-28 incident: `opencode run` for PR #488's review bootstrapped, logged "init", and
    then did nothing for an hour. It never created a session, so no model was ever called, and the
    runner, which called OpenCode synchronously, waited until the user killed it by hand.

    Invoke-OpenCodeWatched:
      1. passes `--title <Title>` so the run's session can be found;
      2. starts the real opencode.exe (not the npm shim) with stdin from an empty file and stdout
         and stderr redirected to files, which it reads back as UTF-8 (so an em dash survives);
      3. polls `opencode session list --format json` (run in -WorkDir, because the list is scoped
         to that directory's project) every -PollSec until a session with that title, or a session
         in -WorkDir created after the start, appears. If none is created (by the session's own
         `created` time) within -StartupTimeoutSec, it kills the process tree and throws
         "OpenCode created no session within N s ...";
      4. then waits for the run to exit until -TotalTimeoutSec after the start, killing the tree
         and throwing on timeout;
      5. returns the output and the exit code. A non-zero exit is returned, not thrown: the caller
         decides.
    A thrown timeout is a [System.TimeoutException] whose Data['Reason'] is a short reason
    ("no session in 180 s", "no exit in 3600 s") for a fallback chain to report.

    Only processes this function started are ever killed (taskkill /T on their own PIDs).
#>

function Resolve-OpenCodeExe {
    # The real executable, never the npm shim: `opencode.cmd` cannot carry a multi-line prompt
    # through cmd.exe, and killing the shim's process would leave opencode.exe running.
    if ($env:IC2_OPENCODE_EXE) {
        if (Test-Path -LiteralPath $env:IC2_OPENCODE_EXE) { return (Resolve-Path -LiteralPath $env:IC2_OPENCODE_EXE).Path }
        throw "IC2_OPENCODE_EXE points at a missing file: $env:IC2_OPENCODE_EXE"
    }
    $found = @(Get-Command opencode -All -ErrorAction SilentlyContinue)
    if (-not $found) { throw 'opencode is not on PATH.' }
    foreach ($c in $found) {
        if ($c.Source -and $c.Source -like '*.exe') { return $c.Source }
    }
    foreach ($c in $found) {
        if (-not $c.Source) { continue }
        $dir = Split-Path $c.Source -Parent
        # The npm layout: <prefix>\opencode.cmd -> <prefix>\node_modules\opencode-ai\bin\opencode.exe
        $npmExe = Join-Path $dir 'node_modules\opencode-ai\bin\opencode.exe'
        if (Test-Path -LiteralPath $npmExe) { return $npmExe }
        # Any other shim: take the .exe path it names, relative to its own directory.
        $text = Get-Content -Raw -LiteralPath $c.Source -ErrorAction SilentlyContinue
        if ($text -match '(?:%dp0%|\$basedir)[\\/]+([^"\s]+?\.exe)') {
            $exe = Join-Path $dir ($Matches[1] -replace '/', '\')
            if (Test-Path -LiteralPath $exe) { return $exe }
        }
    }
    throw "Could not find opencode.exe behind $($found[0].Source); set IC2_OPENCODE_EXE to it."
}

function ConvertTo-OpenCodeArgument([string] $Value) {
    # One argv element, quoted for CommandLineToArgvW (backslashes before a quote are doubled).
    if ($Value -eq '') { return '""' }
    if ($Value -notmatch '[\s"]') { return $Value }
    $sb = [System.Text.StringBuilder]::new('"')
    $slashes = 0
    foreach ($ch in $Value.ToCharArray()) {
        if ($ch -eq '\') { $slashes++; continue }
        if ($ch -eq '"') { [void]$sb.Append([char]'\', 2 * $slashes + 1).Append('"') }
        else { [void]$sb.Append([char]'\', $slashes).Append($ch) }
        $slashes = 0
    }
    [void]$sb.Append([char]'\', 2 * $slashes).Append('"')
    return $sb.ToString()
}

function Stop-OpenCodeTree([System.Diagnostics.Process] $Process) {
    # Kills the process this script started and everything under it. Never anything else.
    if (-not $Process -or $Process.HasExited) { return }
    & taskkill.exe /T /F /PID $Process.Id 2>&1 | Out-Null
    [void]$Process.WaitForExit(15000)
}

function Get-OpenCodeTail([string] $Path, [int] $Lines = 30) {
    if (-not (Test-Path -LiteralPath $Path)) { return '(no output file)' }
    $text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    if (-not $text.Trim()) { return '(empty)' }
    return (($text -split "`r?`n") | Select-Object -Last $Lines) -join "`n"
}

function Find-OpenCodeSession {
    param([string] $Exe, [string] $WorkDir, [string] $Title, [long] $StartedMs, [string] $LogDir)
    # `session list` is scoped to the project of its working directory, so it runs in -WorkDir.
    # It bootstraps OpenCode too, so it gets its own deadline and is killed if it hangs.
    $out = Join-Path $LogDir "$Title.list.json"
    $err = Join-Path $LogDir "$Title.list.err.txt"
    $p = Start-Process -FilePath $Exe -ArgumentList 'session list --format json -n 20' -WorkingDirectory $WorkDir `
        -NoNewWindow -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
    if (-not $p.WaitForExit(60000)) { Stop-OpenCodeTree $p; return $null }
    $json = [System.IO.File]::ReadAllText($out, [System.Text.Encoding]::UTF8)
    if (-not $json.Trim()) { return $null }
    # Unrolled explicitly: Windows PowerShell's ConvertFrom-Json emits a JSON array as one object.
    try { $sessions = @(($json | ConvertFrom-Json) | ForEach-Object { $_ }) } catch { return $null }
    $dir = $WorkDir.TrimEnd('\', '/')
    foreach ($s in $sessions) { if ($s.title -eq $Title) { return $s } }
    foreach ($s in $sessions) {
        if ($s.directory -and $s.directory.TrimEnd('\', '/') -ieq $dir -and [long]$s.created -ge $StartedMs - 5000) { return $s }
    }
    return $null
}

function Invoke-OpenCodeWatched {
    [CmdletBinding()]
    param(
        # The `opencode` arguments after the executable, for example
        # @('run', '--dir', $wt, '--agent', 'x', '--model', 'opencode/glm-5.3'). Must start with 'run'.
        [Parameter(Mandatory)] [string[]] $Arguments,
        # The message, passed as the last argument.
        [Parameter(Mandatory)] [string] $Prompt,
        # The run's directory: its working directory, and where its session is looked for.
        [Parameter(Mandatory)] [string] $WorkDir,
        # A unique session title, for example ic2-pr488-glm-20260928201900.
        [Parameter(Mandatory)] [string] $Title,
        [int] $StartupTimeoutSec = 180,
        [int] $TotalTimeoutSec = 3600,
        [int] $PollSec = 10,
        # Where the stdout/stderr files go; they are kept for diagnosis.
        [string] $LogDir = (Join-Path ([System.IO.Path]::GetTempPath()) 'ic2-opencode')
    )
    if ($Arguments[0] -ne 'run') { throw "Invoke-OpenCodeWatched runs 'opencode run'; got '$($Arguments[0])'." }
    $WorkDir = (Resolve-Path -LiteralPath $WorkDir).Path
    $exe = Resolve-OpenCodeExe
    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
    $outFile = Join-Path $LogDir "$Title.out.txt"
    $errFile = Join-Path $LogDir "$Title.err.txt"
    $inFile = Join-Path $LogDir "$Title.in.txt"
    # An empty stdin: `opencode run` never waits on an inherited console or pipe.
    [System.IO.File]::WriteAllText($inFile, '')

    $all = @($Arguments) + @('--title', $Title, $Prompt)
    $argLine = ($all | ForEach-Object { ConvertTo-OpenCodeArgument $_ }) -join ' '
    if ($argLine.Length -gt 32000) { throw "The opencode command line is $($argLine.Length) characters; Windows allows 32767. Shorten the brief." }

    $startedMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $exe -ArgumentList $argLine -WorkingDirectory $WorkDir -NoNewWindow -PassThru `
        -RedirectStandardInput $inFile -RedirectStandardOutput $outFile -RedirectStandardError $errFile
    $null = $p.Handle   # keeps the handle, so ExitCode is readable after the exit
    Write-Host "opencode: pid $($p.Id), session title $Title, output $outFile"

    try {
        # Startup watch: a session must appear within StartupTimeoutSec.
        $session = $null
        while (-not $session -and -not $p.HasExited) {
            $remainingMs = [int]($StartupTimeoutSec * 1000 - $clock.ElapsedMilliseconds)
            if ($remainingMs -le 0) { break }
            if ($p.WaitForExit([Math]::Min($PollSec * 1000, $remainingMs))) { break }
            $found = Find-OpenCodeSession -Exe $exe -WorkDir $WorkDir -Title $Title -StartedMs $startedMs -LogDir $LogDir
            # In time means created within StartupTimeoutSec of the start, however long the lookup took.
            if ($found -and ([long]$found.created - $startedMs) -le $StartupTimeoutSec * 1000) { $session = $found }
        }
        if (-not $session -and -not $p.HasExited) {
            Stop-OpenCodeTree $p
            $ex = [System.TimeoutException]::new("OpenCode created no session within $StartupTimeoutSec s (the startup hang of 2026-09-28); killed pid $($p.Id). stderr tail ($errFile):`n$(Get-OpenCodeTail $errFile)")
            $ex.Data['Reason'] = "no session in $StartupTimeoutSec s"
            throw $ex
        }
        if ($session) { Write-Host "opencode: session $($session.id) started after $([int]$clock.Elapsed.TotalSeconds) s" }

        # Run watch: the whole run ends within TotalTimeoutSec of the start.
        $remainingMs = [int][Math]::Max(0, $TotalTimeoutSec * 1000 - $clock.ElapsedMilliseconds)
        if (-not $p.WaitForExit($remainingMs)) {
            Stop-OpenCodeTree $p
            $ex = [System.TimeoutException]::new("OpenCode did not finish within $TotalTimeoutSec s; killed pid $($p.Id). stderr tail ($errFile):`n$(Get-OpenCodeTail $errFile)")
            $ex.Data['Reason'] = "no exit in $TotalTimeoutSec s"
            throw $ex
        }
        $p.WaitForExit()
        # A run that finished before the first poll: record its session anyway.
        if (-not $session) {
            $session = Find-OpenCodeSession -Exe $exe -WorkDir $WorkDir -Title $Title -StartedMs $startedMs -LogDir $LogDir
            if ($session) { Write-Host "opencode: session $($session.id) (the run ended before the first poll)" }
        }
    }
    catch {
        Stop-OpenCodeTree $p
        throw
    }

    $stdout = [System.IO.File]::ReadAllText($outFile, [System.Text.Encoding]::UTF8)
    $stderr = [System.IO.File]::ReadAllText($errFile, [System.Text.Encoding]::UTF8)
    return [pscustomobject]@{
        Output    = ($stdout.TrimEnd() + "`n" + $stderr.TrimEnd()).Trim()
        StdOut    = $stdout
        StdErr    = $stderr
        ExitCode  = $p.ExitCode
        SessionId = if ($session) { $session.id } else { $null }
        Seconds   = [int]$clock.Elapsed.TotalSeconds
    }
}
