<#
.SYNOPSIS
    Runs `opencode run` as a watched child process: it fails fast when no session starts, and it
    never waits past a total deadline. Dot-source this file, then call Invoke-OpenCodeWatched.

.DESCRIPTION
    The 2026-09-28 incidents: `opencode run` for PR #488's review (20:19) and for PR #490's (21:42)
    bootstrapped, logged "init", and then did nothing. Neither created a session, so no model was
    called, and the runner, which called OpenCode synchronously, waited until killed by hand.
    The cause was stdin: `opencode run` reads stdin whenever it is not a terminal, and it creates
    no session until stdin reaches end-of-file. Started from a background shell whose stdin is a
    pipe that never closes, it waits forever (reproduced: stdin /dev/null answered in 6 s, a pipe
    held open 40 s answered in 43 s). Closing stdin is the fix; the startup watch is the net.

    Invoke-OpenCodeWatched:
      1. passes `--title <Title>-<random token>`, unique per run, so the run's session can be found;
      2. starts the real opencode.exe (not the npm shim) with stdin from an EMPTY FILE, so it reads
         end-of-file at once, and stdout and stderr redirected to files, which it reads back as
         UTF-8 (so an em dash survives);
      3. polls `opencode session list --format json` (run in -WorkDir, because the list is scoped
         to that directory's project) every -PollSec until a session with exactly that title, in
         -WorkDir, created after the start, appears. Each lookup is bounded by the time left. If
         no session appears within -StartupTimeoutSec (or -TotalTimeoutSec, if sooner), it kills
         the process tree and throws "OpenCode created no session within N s ...". A run that
         exits without a session also throws;
      4. then waits for the run to exit until -TotalTimeoutSec after the start, killing the tree
         and throwing on timeout. While it waits, the IDLE WATCH polls the session's `updated`
         time (the same bounded `session list` lookup, every -PollSec but at most once a minute):
         if it has not advanced for -IdleTimeoutSec, it kills the tree and throws "OpenCode
         session idle for N s". `updated` advances at each step boundary of the run (a tool
         call's completion, the next model turn), not while a tool runs or a reply streams
         (measured 2026-09-28), so -IdleTimeoutSec must exceed the longest single step;
      5. returns the output and the exit code. A non-zero exit is returned, not thrown: the caller
         decides. The run's temporary files are deleted after an exit 0 and kept otherwise (their
         paths are in the result and in any thrown message).
    Every failure of OpenCode itself (not found, no session, no exit, exited without a session,
    idle) is thrown with Data['OpenCodeInfra'] = $true and a short Data['Reason'] ("no session in
    180 s", "no exit in 3600 s", "session idle for 600 s") for a fallback chain; test it with
    Test-OpenCodeInfraFailure. Anything else thrown is a defect of the caller, not a reason to try
    another model.

    Only processes this function started are ever killed (taskkill /T on their own PIDs).
#>

function New-OpenCodeFailure([string] $Reason, [string] $Message, [switch] $Timeout) {
    # An infrastructure failure: the exception a fallback chain may catch and move past.
    $ex = if ($Timeout) { [System.TimeoutException]::new($Message) } else { [System.InvalidOperationException]::new($Message) }
    $ex.Data['OpenCodeInfra'] = $true
    $ex.Data['Reason'] = $Reason
    return $ex
}

function Test-OpenCodeInfraFailure($ErrorRecord) {
    return [bool]($ErrorRecord.Exception -and $ErrorRecord.Exception.Data['OpenCodeInfra'])
}

function Resolve-OpenCodeExe {
    # The real executable, never the npm shim: `opencode.cmd` cannot carry a multi-line prompt
    # through cmd.exe, and killing the shim's process would leave opencode.exe running.
    if ($env:IC2_OPENCODE_EXE) {
        if (Test-Path -LiteralPath $env:IC2_OPENCODE_EXE) { return (Resolve-Path -LiteralPath $env:IC2_OPENCODE_EXE).Path }
        throw (New-OpenCodeFailure 'opencode not found' "IC2_OPENCODE_EXE points at a missing file: $env:IC2_OPENCODE_EXE")
    }
    $found = @(Get-Command opencode -All -ErrorAction SilentlyContinue)
    if (-not $found) { throw (New-OpenCodeFailure 'opencode not found' 'opencode is not on PATH.') }
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
    throw (New-OpenCodeFailure 'opencode not found' "Could not find opencode.exe behind $($found[0].Source); set IC2_OPENCODE_EXE to it.")
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
    [void]$Process.WaitForExit(5000)
}

function Get-OpenCodeTail([string] $Path, [int] $Lines = 30) {
    if (-not (Test-Path -LiteralPath $Path)) { return '(no output file)' }
    $text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    if (-not $text.Trim()) { return '(empty)' }
    return (($text -split "`r?`n") | Select-Object -Last $Lines) -join "`n"
}

function Find-OpenCodeSession {
    param([string] $Exe, [string] $WorkDir, [string] $Title, [long] $StartedMs, [string] $LogDir, [int] $TimeoutMs, [string] $InFile)
    # `session list` is scoped to the project of its working directory, so it runs in -WorkDir.
    # It bootstraps OpenCode too, so it is bounded by the caller's time left and killed if it
    # overruns. Its files are deleted once read.
    if ($TimeoutMs -le 0) { return $null }
    $out = Join-Path $LogDir "$Title.list.json"
    $err = Join-Path $LogDir "$Title.list.err.txt"
    $json = ''
    try {
        $p = Start-Process -FilePath $Exe -ArgumentList 'session list --format json -n 20' -WorkingDirectory $WorkDir `
            -NoNewWindow -PassThru -RedirectStandardInput $InFile -RedirectStandardOutput $out -RedirectStandardError $err
        if (-not $p.WaitForExit($TimeoutMs)) { Stop-OpenCodeTree $p; return $null }
        $json = [System.IO.File]::ReadAllText($out, [System.Text.Encoding]::UTF8)
    } finally {
        Remove-Item -LiteralPath $out, $err -Force -ErrorAction SilentlyContinue
    }
    if (-not $json.Trim()) { return $null }
    # Unrolled explicitly: Windows PowerShell's ConvertFrom-Json emits a JSON array as one object.
    try { $sessions = @(($json | ConvertFrom-Json) | ForEach-Object { $_ }) } catch { return $null }
    # This run's session: its unique title, in its directory, created after it started.
    $dir = $WorkDir.TrimEnd('\', '/')
    foreach ($s in $sessions) {
        if ($s.title -ceq $Title -and $s.directory -and $s.directory.TrimEnd('\', '/') -ieq $dir -and [long]$s.created -ge $StartedMs - 1000) { return $s }
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
        # The session title's prefix, for example ic2-pr488-glm; a random token is appended.
        [Parameter(Mandatory)] [string] $Title,
        [int] $StartupTimeoutSec = 180,
        [int] $TotalTimeoutSec = 3600,
        # Kill the run when its session's `updated` time has not advanced for this long; 0 disables.
        # The callers pass their own default (external-implement.ps1 900, external-review.ps1 600).
        [int] $IdleTimeoutSec = 600,
        [int] $PollSec = 10,
        # Where the run's files go; deleted after an exit 0, kept for diagnosis otherwise.
        [string] $LogDir = (Join-Path ([System.IO.Path]::GetTempPath()) 'ic2-opencode')
    )
    if ($Arguments[0] -ne 'run') { throw "Invoke-OpenCodeWatched runs 'opencode run'; got '$($Arguments[0])'." }
    $WorkDir = (Resolve-Path -LiteralPath $WorkDir).Path
    $exe = Resolve-OpenCodeExe
    $Title = "$Title-$([guid]::NewGuid().ToString('N').Substring(0, 12))"
    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
    $outFile = Join-Path $LogDir "$Title.out.txt"
    $errFile = Join-Path $LogDir "$Title.err.txt"
    $inFile = Join-Path $LogDir "$Title.in.txt"
    # stdin is an EMPTY FILE, never the caller's: `opencode run` reads a non-terminal stdin to its
    # end before it creates a session, so an inherited pipe that never closes hangs it at "init"
    # (both 2026-09-28 hangs). An empty file gives it end-of-file at once.
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

    $files = @($outFile, $errFile, $inFile)
    $startupMs = [long][Math]::Min($StartupTimeoutSec, $TotalTimeoutSec) * 1000
    $totalMs = [long]$TotalTimeoutSec * 1000
    try {
        # Startup watch: a session must appear within StartupTimeoutSec (and TotalTimeoutSec).
        $session = $null
        while (-not $session -and -not $p.HasExited) {
            $remainingMs = $startupMs - $clock.ElapsedMilliseconds
            if ($remainingMs -le 0) { break }
            if ($p.WaitForExit([int][Math]::Min($PollSec * 1000, $remainingMs))) { break }
            $session = Find-OpenCodeSession -Exe $exe -WorkDir $WorkDir -Title $Title -StartedMs $startedMs -LogDir $LogDir `
                -InFile $inFile -TimeoutMs ([int][Math]::Max(0, $startupMs - $clock.ElapsedMilliseconds))
        }
        if (-not $session -and -not $p.HasExited) {
            Stop-OpenCodeTree $p
            $secs = [int]($startupMs / 1000)
            throw (New-OpenCodeFailure "no session in $secs s" -Timeout ("OpenCode created no session within $secs s (the startup hang of 2026-09-28: is stdin closed?); killed pid $($p.Id). Files kept: $($files -join ', '). stderr tail:`n$(Get-OpenCodeTail $errFile)"))
        }
        if ($session) { Write-Host "opencode: session $($session.id) started after $([int]$clock.Elapsed.TotalSeconds) s" }

        # Run watch: the whole run ends within TotalTimeoutSec of the start, and (the idle watch)
        # its session's `updated` time advances at least every IdleTimeoutSec. `updated` is epoch
        # ms on this machine's clock, so the idle time is now minus the last `updated` seen; a
        # lookup that fails or times out leaves it unchanged, so the idle clock keeps running.
        $idleMs = [long]$IdleTimeoutSec * 1000
        $idlePollMs = [int]([Math]::Max($PollSec, [Math]::Min(60, [Math]::Ceiling($IdleTimeoutSec / 5))) * 1000)
        $lastUpdated = if ($session) { [long]$session.updated } else { $startedMs }
        while ($true) {
            $remainingMs = [Math]::Max(0, $totalMs - $clock.ElapsedMilliseconds)
            $waitMs = if ($session -and $idleMs -gt 0) { [Math]::Min($idlePollMs, $remainingMs) } else { $remainingMs }
            if ($p.WaitForExit([int]$waitMs)) { break }
            if ($clock.ElapsedMilliseconds -ge $totalMs) {
                Stop-OpenCodeTree $p
                throw (New-OpenCodeFailure "no exit in $TotalTimeoutSec s" -Timeout ("OpenCode did not finish within $TotalTimeoutSec s; killed pid $($p.Id). Files kept: $($files -join ', '). stderr tail:`n$(Get-OpenCodeTail $errFile)"))
            }
            $seen = Find-OpenCodeSession -Exe $exe -WorkDir $WorkDir -Title $Title -StartedMs $startedMs -LogDir $LogDir `
                -InFile $inFile -TimeoutMs ([int][Math]::Max(0, [Math]::Min(30000, $totalMs - $clock.ElapsedMilliseconds)))
            if ($p.HasExited) { break }
            if ($seen -and [long]$seen.updated -gt $lastUpdated) { $lastUpdated = [long]$seen.updated }
            $idleFor = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() - $lastUpdated
            if ($idleFor -ge $idleMs) {
                Stop-OpenCodeTree $p
                $secs = [int]($idleFor / 1000)
                throw (New-OpenCodeFailure "session idle for $IdleTimeoutSec s" -Timeout ("OpenCode session idle for $secs s (its updated time last advanced $secs s ago; the limit is $IdleTimeoutSec s); killed pid $($p.Id). Files kept: $($files -join ', '). stderr tail:`n$(Get-OpenCodeTail $errFile)"))
            }
        }
        $p.WaitForExit()
        # A run that finished before the first poll: its session must still exist, or it never ran.
        # This lookup is bounded like the others: the time left before the total deadline, at most 15 s.
        if (-not $session) {
            $session = Find-OpenCodeSession -Exe $exe -WorkDir $WorkDir -Title $Title -StartedMs $startedMs -LogDir $LogDir `
                -InFile $inFile -TimeoutMs ([int][Math]::Max(0, [Math]::Min(15000, $totalMs - $clock.ElapsedMilliseconds)))
            if (-not $session) {
                throw (New-OpenCodeFailure "exited without a session (exit $($p.ExitCode))" ("OpenCode exited with $($p.ExitCode) without creating a session. Files kept: $($files -join ', '). stderr tail:`n$(Get-OpenCodeTail $errFile)"))
            }
            Write-Host "opencode: session $($session.id) (the run ended before the first poll)"
        }
    }
    catch {
        Stop-OpenCodeTree $p
        throw
    }

    $stdout = [System.IO.File]::ReadAllText($outFile, [System.Text.Encoding]::UTF8)
    $stderr = [System.IO.File]::ReadAllText($errFile, [System.Text.Encoding]::UTF8)
    if ($p.ExitCode -eq 0) { Remove-Item -LiteralPath $files -Force -ErrorAction SilentlyContinue }
    else { Write-Host "opencode: exit $($p.ExitCode); files kept: $($files -join ', ')" }
    return [pscustomobject]@{
        Output    = ($stdout.TrimEnd() + "`n" + $stderr.TrimEnd()).Trim()
        StdOut    = $stdout
        StdErr    = $stderr
        ExitCode  = $p.ExitCode
        SessionId = $session.id
        Title     = $Title
        Files     = if ($p.ExitCode -eq 0) { @() } else { $files }
        Seconds   = [int]$clock.Elapsed.TotalSeconds
    }
}
