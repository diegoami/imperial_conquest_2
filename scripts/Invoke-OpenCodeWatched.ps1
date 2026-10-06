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
      2. starts the real opencode.exe (not the npm shim) with stdin from a FILE holding the prompt
         (never the command line, which Windows caps at 32,767 characters; Get-OpenCodeRunInvocation),
         so it reads the whole prompt and then end-of-file, and stdout and stderr redirected to
         files, which it reads back as UTF-8 (so an em dash survives);
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

    OpenCode 1.x and 2.x (T98). Which CLI runs (user decision of 2026-10-03: prefer the npm 1.x CLI
    until a 2.x OpenCode Go login is proven): IC2_OPENCODE_EXE, else the npm CLI (1.x), else, only on
    a machine without npm, the newest %APPDATA%\ai.opencode.desktop\cli\<version>\opencode-cli.exe
    (the desktop app's 2.x). 2.x is opt-in through IC2_OPENCODE_EXE. The log line names the CLI and
    why it was chosen. `--version` is read once per executable (Get-OpenCodeCli); the arguments are
    built by the major version (Get-OpenCodeRunArguments); an unknown major fails with
    Reason 'unsupported opencode version' (the callers exit 3). 2.x differs from 1.x in four ways
    the watcher depends on: `run --dir` is gone (the process's own working directory is used), the
    variant is part of the model (`provider/model#variant`), `run` talks to a shared background
    service unless it is given `--standalone` (the watcher must own the process that does the work,
    so every 2.x call it makes, `run`, `session list` and `session export`, passes `--standalone`),
    and the export is `session export <id>`.

    Every OpenCode process the watcher starts for a run (run, session list, session export, and the
    opencode-go login check) runs with its own XDG data, cache and state directories
    (Initialize-OpenCodeDataHome), restored in the calling process afterwards. The one exception is
    the `--version` probe (Get-OpenCodeCli): it runs before the root is known (the root depends on
    the major version it reports), so it gets a neutral directory of its own,
    ~\.local\share\ic2-opencode-probe, which is where 2.x writes its startup log line.
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

function Get-OpenCodeFailureClass([string] $Reason) {
    # A failed attempt's cause without its numbers, so a chain can tell the same failure twice
    # (two startup hangs, two idle kills) from two different ones. The user's decision of
    # 2026-09-28: two consecutive attempts failing with one cause stop the chain with exit 3.
    switch -Regex ($Reason) {
        '^opencode not found'               { return 'not-found' }
        '^unsupported opencode version'     { return 'unsupported-version' }
        '^no authentication'                { return 'no-auth' }
        '^no session in'                    { return 'no-session' }
        '^session idle'                     { return 'idle' }
        '^no exit in'                       { return 'total-timeout' }
        '^exited without a session'         { return 'exited-without-session' }
        'fell back to the default agent'    { return 'fallback-agent' }
        '^exit -?\d+'                       { return 'non-zero-exit' }
        '^permission rejected'              { return 'permission-rejected' }
        'cut off|no header line|no verdict' { return 'cut-off' }
        default                             { return $Reason }
    }
}

function Test-OpenCodeAgentWarning([string] $StdErr, [string] $Agent) {
    # OpenCode's own warning when --agent names an agent it cannot find, as OpenCode 1.18 prints it
    # on stderr (checked 2026-09-28 with --agent no-such-agent-xyz):
    #   ESC[93mESC[1m! ESC[0m agent "no-such-agent-xyz" not found. Falling back to default agent
    # Matched only at the start of a line, after the "!" marker and colour codes, and only for the
    # requested agent's name, so a tool's output quoting the phrase does not trip it.
    # OpenCode 2.x (T98) has no such fallback: --agent with an unknown name stops the run with
    # exit 1 and "Error: Agent not found: "no-such-agent-xyz"" (recorded from 2.0.18, 2026-10-03), so
    # a non-zero exit covers it there. The session record (the export's .info.agent) stays the
    # authority where it is read.
    $ansi = '(?:\x1b\[[0-9;]*m|[ \t])*'
    $pattern = '(?m)^' + $ansi + '!' + $ansi + 'agent "?' + [regex]::Escape($Agent) + '"? not found\. Falling back to default agent'
    return [bool]($StdErr -match $pattern)
}

function Get-OpenCodePermissionRejection([string] $Text) {
    # What OpenCode's permission guard refused, or $null. In a non-interactive `opencode run` a
    # tool call that needs a permission the agent lacks (a path outside the working directory:
    # external_directory) is auto-rejected, and the rejection ENDS the run with exit 0 -- so without
    # this check the run looks finished (issue #501: three runs on 2026-09-29, T94 twice and T51
    # once). OpenCode 1.18 prints, at the start of a line:
    #   ESC[93mESC[1m! ESC[0mpermission requested: external_directory (C:\...\Temp\*); auto-rejecting
    # Matched only after the "!" marker and colour codes, so a tool's output quoting the phrase
    # (a log line with a prefix, a comment like this one) does not trip it. Returns the last one.
    # OpenCode 2.x (T98) keeps the guard (`run --auto` is its opt-out) and prints the same line,
    # recorded from 2.0.18 on 2026-10-03 (a read of C:\Windows\win.ini): the run then exits 1, not 0,
    # so the callers check the rejection before the exit code.
    #   ESC[93mESC[1m! ESC[0mpermission requested: external_directory (C:/Windows/*); auto-rejecting
    $ansi = '(?:\x1b\[[0-9;]*m|[ \t])*'
    $pattern = '(?m)^' + $ansi + '!' + $ansi + 'permission requested: (?<what>[^\r\n]*?); auto-rejecting'
    $found = [regex]::Matches($Text, $pattern)
    if ($found.Count -eq 0) { return $null }
    return $found[$found.Count - 1].Groups['what'].Value
}

function Get-OpenCodeEpochMs($Value) {
    # A session's `created` / `updated` as epoch milliseconds. 1.x and 2.x print a number (ms);
    # PowerShell 7's ConvertFrom-Json turns an ISO string into a DateTime, so that form is
    # accepted too, and a number small enough to be seconds is scaled. $null when it is none of them.
    if ($null -eq $Value) { return $null }
    if ($Value -is [datetime]) { return [DateTimeOffset]::new($Value.ToUniversalTime()).ToUnixTimeMilliseconds() }
    if ($Value -is [DateTimeOffset]) { return $Value.ToUnixTimeMilliseconds() }
    $n = 0.0
    if ([double]::TryParse([string]$Value, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$n)) {
        if ($n -lt 100000000000) { $n = $n * 1000 }
        return [long]$n
    }
    $d = [DateTimeOffset]::MinValue
    if ([DateTimeOffset]::TryParse([string]$Value, [ref]$d)) { return $d.ToUnixTimeMilliseconds() }
    return $null
}

function Export-OpenCodeSession {
    param($Cli, [string] $WorkDir, [string] $SessionId, [string] $OutFile, [string] $InFile, [int] $TimeoutMs = 60000)
    # The session's transcript as JSON (1.x `export <id>`, 2.x `session export --standalone <id>`)
    # written to -OutFile and KEPT (Done-when 1 of T98: the transcript under the run's -LogDir).
    # Bounded and killed like the session lookup. Returns $true when the file was written.
    $err = "$OutFile.err.txt"
    try {
        $p = Start-Process -FilePath $Cli.Exe -ArgumentList (Get-OpenCodeExportArguments $Cli.Major $SessionId) -WorkingDirectory $WorkDir `
            -NoNewWindow -PassThru -RedirectStandardInput $InFile -RedirectStandardOutput $OutFile -RedirectStandardError $err
        if (-not $p.WaitForExit($TimeoutMs)) { Stop-OpenCodeTree $p; return $false }
        return ($p.ExitCode -eq 0 -and (Test-Path -LiteralPath $OutFile) -and (Get-Item -LiteralPath $OutFile).Length -gt 0)
    } catch { return $false }
    finally { Remove-Item -LiteralPath $err -Force -ErrorAction SilentlyContinue }
}

function Get-OpenCodeSessionAgent([string] $ExportFile) {
    # The agent OpenCode recorded for the session (the export's .info.agent, in 1.x and 2.x), or
    # $null when the export is missing or unreadable.
    try {
        $json = [System.IO.File]::ReadAllText($ExportFile, [System.Text.Encoding]::UTF8)
        return (($json | ConvertFrom-Json).info.agent)
    } catch { return $null }
}

function Get-OpenCodeDesktopExe {
    # The newest opencode-cli.exe under the desktop app's cli\<version>\ folder, or $null. "Newest"
    # is by the folder's version number, not its name (2.0.9 < 2.0.18).
    if (-not $env:APPDATA) { return $null }
    $root = Join-Path $env:APPDATA 'ai.opencode.desktop\cli'
    if (-not (Test-Path -LiteralPath $root)) { return $null }
    $best = $null
    foreach ($d in @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue)) {
        $exe = Join-Path $d.FullName 'opencode-cli.exe'
        $v = $null
        if (-not (Test-Path -LiteralPath $exe) -or -not [version]::TryParse(($d.Name -replace '^v', ''), [ref]$v)) { continue }
        if (-not $best -or $v -gt $best.Version) { $best = [pscustomobject]@{ Version = $v; Exe = $exe } }
    }
    if ($best) { return $best.Exe }
    return $null
}

function Get-OpenCodeCli {
    # The CLI that runs: its executable, its --version (read ONCE per executable per process), and
    # the major version the argument builders switch on. An unknown major is an OpenCode failure
    # (the callers exit 3): the argument syntax of an unseen major is a guess, and a wrong guess
    # bills a model for nothing.
    $exe = Resolve-OpenCodeExe
    if (-not $script:OpenCodeCliCache) { $script:OpenCodeCliCache = @{} }
    if ($script:OpenCodeCliCache.ContainsKey($exe)) { $script:OpenCodeCliCache[$exe].Why = $script:OpenCodeExeWhy; return $script:OpenCodeCliCache[$exe] }
    $text = ''
    try {
        $psi = [System.Diagnostics.ProcessStartInfo]::new($exe, '--version')
        $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.RedirectStandardInput = $true
        $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
        # 2.x writes a startup log line under XDG_DATA_HOME even for --version: keep it out of the
        # caller's (and the desktop app's shared) directories. The environment of this one child only.
        $probe = Join-Path $HOME '.local\share\ic2-opencode-probe'
        foreach ($pair in @(@('XDG_DATA_HOME', 'data'), @('XDG_CACHE_HOME', 'cache'), @('XDG_STATE_HOME', 'state'))) {
            $d = Join-Path $probe $pair[1]
            New-Item -ItemType Directory -Force -Path $d | Out-Null
            $psi.Environment[$pair[0]] = $d
        }
        $vp = [System.Diagnostics.Process]::Start($psi)
        $vp.StandardInput.Close()
        $outTask = $vp.StandardOutput.ReadToEndAsync()
        if (-not $vp.WaitForExit(30000)) { Stop-OpenCodeTree $vp; throw 'timed out' }
        $text = $outTask.Result
    } catch {
        throw (New-OpenCodeFailure 'unreadable opencode version' "Could not read the version of ${exe}: $($_.Exception.Message) Set IC2_OPENCODE_EXE to a working opencode.")
    }
    if ($text -notmatch '(\d+)\.(\d+)\.(\d+)') {
        throw (New-OpenCodeFailure 'unreadable opencode version' "'$exe --version' printed no version number ('$($text.Trim())').")
    }
    $major = [int]$Matches[1]
    if ($major -ne 1 -and $major -ne 2) {
        throw (New-OpenCodeFailure "unsupported opencode version $($Matches[0])" "OpenCode $($Matches[0]) ($exe) is not supported: these scripts build arguments for OpenCode 1.x and 2.x only. Set IC2_OPENCODE_EXE to a supported CLI.")
    }
    $cli = [pscustomobject]@{ Exe = $exe; Version = "$($Matches[1]).$($Matches[2]).$($Matches[3])"; Major = $major; Why = $script:OpenCodeExeWhy }
    $script:OpenCodeCliCache[$exe] = $cli
    return $cli
}

function Get-OpenCodeRunArguments {
    param([int] $Major, [string] $WorkDir, [string] $Agent, [string] $Model, [string] $Variant, [string] $Title)
    # The `opencode run` arguments before the message, by major version.
    #   1.x: run --dir <dir> [--agent a] --model provider/model [--variant v] --title t
    #   2.x: run --standalone [--agent a] --model provider/model#variant --title t
    # 2.x has no --dir (the process's own working directory is used) and no --variant (the variant
    # is part of the model); --standalone makes `run` own a private server, so the process the
    # watcher kills is the one doing the work, not a shared background service that outlives it.
    $a = @('run')
    if ($Major -ge 2) { $a += '--standalone' } else { $a += @('--dir', $WorkDir) }
    if ($Agent) { $a += @('--agent', $Agent) }
    if ($Major -ge 2) { $a += @('--model', $(if ($Variant) { "$Model#$Variant" } else { $Model })) }
    else { $a += @('--model', $Model); if ($Variant) { $a += @('--variant', $Variant) } }
    if ($Title) { $a += @('--title', $Title) }
    return $a
}

function Get-OpenCodeRunInvocation([string[]] $RunArguments, [string] $Prompt) {
    # How a run gets its prompt: through STDIN, from a file, never on the command line. Windows caps
    # a command line at 32,767 characters, and a brief that pastes a whole task entry and a review
    # crossed it three times on 2026-10-06. `opencode run` reads a non-terminal stdin to its end and
    # takes it as the message, on 1.x and 2.x alike, and the file's end gives it the end-of-file it
    # waits for (the 2026-09-28 hangs). Measured 2026-10-06 on 1.18.34: a 39,111-character prompt
    # whose only instruction came after 39,000 characters of padding was followed. The 32,000 guard
    # stays for what is still on the command line (the flags, the model, the title).
    $line = (@($RunArguments) | ForEach-Object { ConvertTo-OpenCodeArgument $_ }) -join ' '
    if ($line.Length -gt 32000) { throw "The opencode command line is $($line.Length) characters; Windows allows 32767." }
    return [pscustomobject]@{ ArgumentLine = $line; StdIn = $Prompt; Delivery = "stdin, $($Prompt.Length) characters" }
}

function Get-OpenCodeListArguments([int] $Major) {
    # 2.x's `session list` and `session export` talk to the shared background service by default,
    # which would read the desktop app's data directory, not this run's: --standalone reads the
    # run's own (XDG_DATA_HOME) database.
    if ($Major -ge 2) { return 'session list --standalone --format json -n 20' }
    return 'session list --format json -n 20'
}

function Get-OpenCodeExportArguments([int] $Major, [string] $SessionId) {
    if ($Major -ge 2) { return "session export --standalone $SessionId" }
    return "export $SessionId"
}

function Resolve-OpenCodeExe {
    # The real executable, never the npm shim: `opencode.cmd` cannot carry a multi-line prompt
    # through cmd.exe, and killing the shim's process would leave opencode.exe running.
    # Which CLI runs (user decision of 2026-10-03: prefer the npm 1.x CLI until a 2.x OpenCode Go
    # login is proven): IC2_OPENCODE_EXE wins; otherwise the npm CLI (1.x); only on a machine
    # without npm, the newest opencode-cli.exe the desktop app bundles (2.x). The reason is kept in
    # $script:OpenCodeExeWhy for the log line.
    if ($env:IC2_OPENCODE_EXE) {
        if (Test-Path -LiteralPath $env:IC2_OPENCODE_EXE) {
            $script:OpenCodeExeWhy = 'IC2_OPENCODE_EXE is set'
            return (Resolve-Path -LiteralPath $env:IC2_OPENCODE_EXE).Path
        }
        throw (New-OpenCodeFailure 'opencode not found' "IC2_OPENCODE_EXE points at a missing file: $env:IC2_OPENCODE_EXE")
    }
    $npm = Find-OpenCodePathExe
    if ($npm.Exe) {
        $script:OpenCodeExeWhy = 'the npm CLI on PATH, preferred over the desktop app until a 2.x opencode-go login is proven'
        return $npm.Exe
    }
    $desktop = Get-OpenCodeDesktopExe
    if ($desktop) {
        $script:OpenCodeExeWhy = 'the newest desktop app CLI, because no npm CLI was found'
        return $desktop
    }
    throw (New-OpenCodeFailure 'opencode not found' $npm.Problem)
}

function Find-OpenCodePathExe {
    # The npm CLI's real executable from PATH: @{ Exe = <path> } or @{ Exe = $null; Problem = <why not> }.
    $found = @(Get-Command opencode -All -ErrorAction SilentlyContinue)
    if (-not $found) { return @{ Exe = $null; Problem = 'opencode is not on PATH, and the desktop app has no CLI under %APPDATA%\ai.opencode.desktop\cli.' } }
    foreach ($c in $found) {
        if ($c.Source -and $c.Source -like '*.exe') { return @{ Exe = $c.Source } }
    }
    foreach ($c in $found) {
        if (-not $c.Source) { continue }
        $dir = Split-Path $c.Source -Parent
        # The npm layout: <prefix>\opencode.cmd -> <prefix>\node_modules\opencode-ai\bin\opencode.exe
        $npmExe = Join-Path $dir 'node_modules\opencode-ai\bin\opencode.exe'
        if (Test-Path -LiteralPath $npmExe) { return @{ Exe = $npmExe } }
        # Any other shim: take the .exe path it names, relative to its own directory.
        $text = Get-Content -Raw -LiteralPath $c.Source -ErrorAction SilentlyContinue
        if ($text -match '(?:%dp0%|\$basedir)[\\/]+([^"\s]+?\.exe)') {
            $exe = Join-Path $dir ($Matches[1] -replace '/', '\')
            if (Test-Path -LiteralPath $exe) { return @{ Exe = $exe } }
        }
    }
    return @{ Exe = $null; Problem = "Could not find opencode.exe behind $($found[0].Source); set IC2_OPENCODE_EXE to it." }
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
    param($Cli, [string] $WorkDir, [string] $Title, [long] $StartedMs, [string] $LogDir, [int] $TimeoutMs, [string] $InFile)
    # `session list` is scoped to the project of its working directory, so it runs in -WorkDir.
    # It bootstraps OpenCode too, so it is bounded by the caller's time left and killed if it
    # overruns. Its files are deleted once read. Under 2.x it passes --standalone (see
    # Get-OpenCodeListArguments), so it reads the run's own database.
    if ($TimeoutMs -le 0) { return $null }
    $out = Join-Path $LogDir "$Title.list.json"
    $err = Join-Path $LogDir "$Title.list.err.txt"
    $json = ''
    try {
        $p = Start-Process -FilePath $Cli.Exe -ArgumentList (Get-OpenCodeListArguments $Cli.Major) -WorkingDirectory $WorkDir `
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
        $created = Get-OpenCodeEpochMs $s.created
        if ($s.title -ceq $Title -and $s.directory -and $s.directory.TrimEnd('\', '/') -ieq $dir -and $null -ne $created -and $created -ge $StartedMs - 1000) {
            $updated = Get-OpenCodeEpochMs $s.updated
            return [pscustomobject]@{ id = $s.id; title = $s.title; directory = $s.directory; created = $created; updated = $(if ($null -ne $updated) { $updated } else { $created }) }
        }
    }
    return $null
}

function Resolve-OpenCodeFullPath([string] $Path) {
    # One absolute path, from the calling process's current location: a relative path would be read
    # differently by every child (each runs in -WorkDir), so it is resolved before any directory is made.
    $expanded = [System.Environment]::ExpandEnvironmentVariables($Path)
    $full = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine((Get-Location).ProviderPath, $expanded))
    return $full.TrimEnd('\', '/')
}

function Initialize-OpenCodeDataHome {
    param([int] $Major = 1, [switch] $Alibaba)
    # -Alibaba (an alibaba-token-plan/* run, the owner's decisions of 2026-10-05): the run gets its own
    # data folder, <root>\data-alibaba (Get-OpenCodeDataDirs), which never holds an auth.json: that
    # provider authenticates by ALIBABA_TOKEN_PLAN_API_KEY alone, and an auth.json entry for it would
    # override the variable. Nothing is copied into it, and a run finding an auth.json there stops
    # (the file is never read, parsed or edited). Cache and state stay the root's. Other providers
    # keep <root>\data and the auth.json copy below unchanged.
    # Issue #540: gives every OpenCode process this script starts its OWN data, cache and state
    # directories, so the OpenCode desktop app (which shares ~/.local/share/opencode/ and moved
    # opencode.db to its 2.x schema on 2026-10-01, which the npm CLI 1.18 cannot read: "no such
    # column: project_id") can never reach them. OpenCode keeps its data under
    # $XDG_DATA_HOME\opencode\ (and its cache and state under XDG_CACHE_HOME and XDG_STATE_HOME,
    # which the 2.x desktop app shares too), and the child processes (run, session list, export)
    # inherit this process's environment, so all of them see the same database. Only these three
    # are separated: ~/.config/opencode/opencode.json and the repository's .opencode/agents/ stay
    # shared. Each gets its own subfolder of the root: <root>\data, <root>\cache, <root>\state.
    # The root is IC2_OPENCODE_DATA_HOME (a relative one is resolved to ONE absolute path first),
    # else ~\.local\share\ic2-opencode-<major>x: a 2.x run migrates the database to the 2.x schema,
    # which 1.x cannot read, so the two majors never share a default root, and a marker file
    # (schema-major.txt) refuses to run one major on a root the other has used (an explicit root).
    # auth.json is COPIED (never read, printed or logged: it holds API keys) from the default
    # directory, or from IC2_OPENCODE_AUTH_SOURCE, when the copy is missing or older, so a
    # re-authentication carries over. With no source and no copy the run stops here, before any
    # model is called. Returns what Restore-OpenCodeDataHome needs; the caller restores in a
    # `finally`, so the calling process's own XDG_* variables are back after the run.
    $root = if ($env:IC2_OPENCODE_DATA_HOME) { Resolve-OpenCodeFullPath $env:IC2_OPENCODE_DATA_HOME } else { Join-Path $HOME ".local\share\ic2-opencode-${Major}x" }
    $dirs = Get-OpenCodeDataDirs -Root $root -Alibaba:$Alibaba
    $dataDir = Join-Path $dirs.XDG_DATA_HOME 'opencode'
    $src = if ($env:IC2_OPENCODE_AUTH_SOURCE) { Resolve-OpenCodeFullPath $env:IC2_OPENCODE_AUTH_SOURCE } else { Join-Path $HOME '.local\share\opencode\auth.json' }
    $marker = Join-Path $root 'schema-major.txt'
    if (Test-Path -LiteralPath $marker) {
        $seen = ([System.IO.File]::ReadAllText($marker)).Trim()
        if ($seen -ne "$Major") {
            throw (New-OpenCodeFailure "data directory holds OpenCode $seen.x data" "The OpenCode data directory $root was used by OpenCode $seen.x and this run is $Major.x: the schemas differ (a 2.x run migrates the database, and 1.x cannot read it afterwards). Unset IC2_OPENCODE_DATA_HOME, or point it at a directory of its own for this major version.")
        }
    }
    # The 1.x layout before T98 had XDG_DATA_HOME = <root>, so its database, its auth.json and the
    # opencode-go console login (stored in that database) are in <root>\opencode. T98 moved the data
    # to <root>\data (cache and state beside it); without this move the 1.x lane would lose its login
    # and every opencode-go run would fail "Provider not found". Moved once, before any process
    # starts, only for 1.x, and only when the old folder exists and the new one does not (so it is
    # idempotent, and never merges two databases). The move is atomic: it either happens whole or not at all.
    $oldDir = Join-Path $root 'opencode'
    if (-not $Alibaba -and $Major -eq 1 -and (Test-Path -LiteralPath $oldDir) -and -not (Test-Path -LiteralPath $dataDir)) {
        try {
            New-Item -ItemType Directory -Force -Path $dirs.XDG_DATA_HOME | Out-Null
            # ONE atomic rename, never Move-Item: when a file inside is open (SQLite holds the database
            # open; an older run may still be using it) Move-Item falls back to moving file by file,
            # leaving a split folder that the guard above then never retries. Directory.Move moves
            # everything or nothing, so a failure leaves <root>\opencode whole and the next run retries.
            [System.IO.Directory]::Move($oldDir, $dataDir)
        } catch {
            throw (New-OpenCodeFailure 'data directory migration failed' "Could not move the old 1.x data directory $oldDir to $dataDir ($($_.Exception.Message)). Is an older OpenCode run still using it? Nothing was started.")
        }
        Write-Host "opencode: migrated the 1.x data directory $oldDir -> $dataDir (the login and the history move with it)"
    }
    foreach ($d in @($dataDir, $dirs.XDG_CACHE_HOME, $dirs.XDG_STATE_HOME)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
    if (-not (Test-Path -LiteralPath $marker)) { [System.IO.File]::WriteAllText($marker, "$Major") }
    $dst = Join-Path $dataDir 'auth.json'
    if ($Alibaba) {
        if (Test-Path -LiteralPath $dst) {
            # Not an infrastructure failure: no other model or retry fixes it, so the caller stops.
            throw [System.InvalidOperationException]::new("Alibaba Token Plan: the Alibaba runs' data folder $dataDir holds an auth.json; it must hold none (an alibaba-token-plan entry there overrides ALIBABA_TOKEN_PLAN_API_KEY). It was not read. Remove it, or tell the owner. Nothing was started.")
        }
        $auth = 'no auth.json (Alibaba Token Plan: ALIBABA_TOKEN_PLAN_API_KEY authenticates)'
    } elseif (Test-Path -LiteralPath $src) {
        if (-not (Test-Path -LiteralPath $dst) -or (Get-Item -LiteralPath $src).LastWriteTimeUtc -gt (Get-Item -LiteralPath $dst).LastWriteTimeUtc) {
            Copy-Item -LiteralPath $src -Destination $dst -Force
            $auth = 'auth.json copied'
        } else { $auth = 'auth.json already current' }
    } elseif (Test-Path -LiteralPath $dst) {
        $auth = 'auth.json source missing, the existing copy is used'
    } else {
        throw (New-OpenCodeFailure 'no authentication' "No authentication: there is no auth.json to copy ($src is missing) and none in the run's data directory ($dst). OpenCode would call no model. For the API-key providers run 'opencode auth login' (it writes the default auth.json), or set IC2_OPENCODE_AUTH_SOURCE to an auth.json. For opencode-go (the Go console login, which is stored in the data directory's database, not in auth.json) run 'opencode console login' with XDG_DATA_HOME=$($dirs.XDG_DATA_HOME). Nothing was started.")
    }
    $saved = [ordered]@{}
    foreach ($name in $dirs.Keys) {
        $saved[$name] = [System.Environment]::GetEnvironmentVariable($name, 'Process')
        [System.Environment]::SetEnvironmentVariable($name, $dirs[$name], 'Process')
    }
    Write-Host "opencode: data directory $dataDir (XDG_DATA_HOME=$($dirs.XDG_DATA_HOME), XDG_CACHE_HOME=$($dirs.XDG_CACHE_HOME), XDG_STATE_HOME=$($dirs.XDG_STATE_HOME); $auth)"
    return [pscustomobject]@{ Root = $root; DataDir = $dataDir; Saved = $saved }
}

function Get-OpenCodeDataDirs {
    # The XDG directories of a run under -Root: <root>\data, or <root>\data-alibaba for an Alibaba
    # Token Plan run (its own data folder, never holding an auth.json); cache and state are shared.
    param([string] $Root, [switch] $Alibaba)
    $data = Join-Path $Root $(if ($Alibaba) { 'data-alibaba' } else { 'data' })
    return [ordered]@{ XDG_DATA_HOME = $data; XDG_CACHE_HOME = (Join-Path $Root 'cache'); XDG_STATE_HOME = (Join-Path $Root 'state') }
}

function Test-OpenCodeGoLogin {
    param($Cli, [string] $WorkDir, [string] $Root, [string] $InFile)
    # Whether the run's data directory holds the opencode-go console login. Under 1.x, `models
    # opencode-go` (no session, no billing, in the run's isolated environment) answers "Error:
    # Provider not found: opencode-go" with exit 1 when the login is missing (observed 1.18.34); only
    # that exact answer counts as missing, so any other failure (a timeout, a network error) lets the
    # run go on. 2.0.18 has no per-provider `models` and no `console` command, and its `models
    # --standalone` printed nothing even with a fresh root, so a missing login cannot be detected
    # cheaply and reliably there: it is $true (unknown), and the run itself reports it.
    if ($Cli.Major -ge 2) { return $true }
    $out = Join-Path $Root 'go-login-check.out.txt'
    $err = Join-Path $Root 'go-login-check.err.txt'
    try {
        $p = Start-Process -FilePath $Cli.Exe -ArgumentList 'models opencode-go' -WorkingDirectory $WorkDir `
            -NoNewWindow -PassThru -RedirectStandardInput $InFile -RedirectStandardOutput $out -RedirectStandardError $err
        if (-not $p.WaitForExit(60000)) { Stop-OpenCodeTree $p; return $true }
        $text = [System.IO.File]::ReadAllText($err, [System.Text.Encoding]::UTF8) + [System.IO.File]::ReadAllText($out, [System.Text.Encoding]::UTF8)
        return -not ($p.ExitCode -ne 0 -and $text -match 'Provider not found: opencode-go')
    } catch { return $true }
    finally { Remove-Item -LiteralPath $out, $err -Force -ErrorAction SilentlyContinue }
}

function Restore-OpenCodeDataHome($State) {
    # Puts the calling process's XDG_DATA_HOME, XDG_CACHE_HOME and XDG_STATE_HOME back as they were
    # (removed again when they were not set).
    if (-not $State) { return }
    foreach ($name in $State.Saved.Keys) {
        # Remove-Item, not SetEnvironmentVariable($name, $null): PowerShell turns that $null into an empty string.
        if ($null -eq $State.Saved[$name]) { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue }
        else { [System.Environment]::SetEnvironmentVariable($name, $State.Saved[$name], 'Process') }
    }
}

# --- The Alibaba Token Plan (the user's decision of 2026-10-05, docs/environment.md) --------------
# Provider `alibaba-token-plan`. Its key is the Windows USER environment variable
# ALIBABA_TOKEN_PLAN_API_KEY, never an auth.json entry: a key in auth.json overrides the variable and
# breaks the provider ("Invalid API-key"). A process started before the variable was set lacks it
# ("Provider not found"), so both runner scripts call Import-AlibabaTokenPlanKey first. The key is
# never printed, copied to a file or logged.
function Import-AlibabaTokenPlanKey {
    # Copies ALIBABA_TOKEN_PLAN_API_KEY from the user environment into this process when the process
    # lacks it, so OpenCode's child processes inherit it. Returns whether the process has it now.
    if ([System.Environment]::GetEnvironmentVariable('ALIBABA_TOKEN_PLAN_API_KEY', 'Process')) { return $true }
    $value = try { [System.Environment]::GetEnvironmentVariable('ALIBABA_TOKEN_PLAN_API_KEY', 'User') } catch { $null }
    if (-not $value) { return $false }
    [System.Environment]::SetEnvironmentVariable('ALIBABA_TOKEN_PLAN_API_KEY', $value, 'Process')
    return $true
}

function Get-AlibabaFailure([string] $Text, [string] $DataDir) {
    # The two Alibaba errors that no other model or retry can fix (docs/environment.md): the message
    # to stop with, or $null. Read from OpenCode's stderr only, never from the model's words.
    if ($Text -match 'Invalid API[- ]?key') {
        return "Alibaba Token Plan: 'Invalid API-key'. Look for a stale alibaba-token-plan key that overrides ALIBABA_TOKEN_PLAN_API_KEY: an auth.json in $DataDir (the Alibaba runs' own data folder, which must hold none) or a provider key in ~\.config\opencode\opencode.json (neither was read here). Remove it by hand, or tell the owner; never put the key in an auth.json. Not retried."
    }
    if ($Text -match 'Provider not found:?\s*alibaba-token-plan') {
        return "Alibaba Token Plan: 'Provider not found'. ALIBABA_TOKEN_PLAN_API_KEY is not in the OpenCode process's environment (on Windows it is a user variable; in WSL it comes from ~/.config/ai-keys.env, loaded by ~/.bashrc and ~/.profile, or from WSLENV for commands started from Windows). Restart the session or shell so it picks it up; if it is still missing, tell the owner. Not retried."
    }
    return $null
}

function Get-OpenCodeErrorLines([string] $Text) {
    # OpenCode's own error lines: it prints a failed request on stderr as a line starting "Error: "
    # (observed 2026-10-06 on 1.x: "Error: Invalid API-key provided. ..." after the "> build · <model>"
    # header, and "Error: {" for an unknown model or a missing key). Only those lines are returned, so
    # nothing the model wrote is matched; stdout, the model's formatted reply, is never read here.
    return (@(($Text -split "`r?`n") | Where-Object { $_ -match '^\s*Error: ' }) -join "`n")
}

function Get-AlibabaRunFailure($Run, [string] $DataDir) {
    # The Alibaba stop message for a finished run, whatever its exit code (an exit 0 with the error
    # on stderr included), from OpenCode's own error lines only; or $null.
    return (Get-AlibabaFailure (Get-OpenCodeErrorLines $Run.StdErr) $DataDir)
}

# --- The free OpenRouter models (the owner's decision of 2026-10-06, docs/environment.md) ---------
# Advisory-only extra reviewers. They share one allowance (1,000 requests a day, about 20 a minute,
# across every free model; each agent step is one request), and a rate limit is skipped, never retried.
function Get-OpenRouterFreeRequests {
    # quota-tracker's /quota/openrouter: how many free-model requests are left today. Answered is
    # $false when the service does not answer or carries no free_model_daily_requests (an advisory
    # review is optional, so the caller then skips it). IC2_QUOTA_FREE_REQUESTS, when set, stands in
    # for the service (a test hook for -SelfTest: a number, or 'none' for a silent tracker); it is
    # never set in normal use.
    param([string] $Url = 'http://localhost:8765/quota/openrouter', [int] $TimeoutSec = 5)
    $silent = [pscustomobject]@{ Answered = $false; Remaining = $null }
    if ($env:IC2_QUOTA_FREE_REQUESTS) {
        if ($env:IC2_QUOTA_FREE_REQUESTS -eq 'none') { return $silent }
        return [pscustomobject]@{ Answered = $true; Remaining = [int]$env:IC2_QUOTA_FREE_REQUESTS }
    }
    try { $r = Invoke-RestMethod -Uri $Url -TimeoutSec $TimeoutSec -ErrorAction Stop } catch { return $silent }
    $f = $r.free_model_daily_requests
    if ($null -eq $f) { return $silent }
    # Seen 2026-10-06 as { used, limit, remaining }; a bare number is taken as the remaining count.
    $remaining = if ($f -is [ValueType]) { $f } elseif ($null -ne $f.remaining) { $f.remaining } elseif ($null -ne $f.limit -and $null -ne $f.used) { $f.limit - $f.used } else { $null }
    if ($null -eq $remaining) { return $silent }
    return [pscustomobject]@{ Answered = $true; Remaining = [int]$remaining }
}

function Get-OpenCodeRateLimit([string] $Text) {
    # The rate-limit error in OpenCode's own error lines (Get-OpenCodeErrorLines: only its "Error: "
    # lines, never the model's words), or $null: a 429, "rate limit" or "Too Many Requests".
    $lines = Get-OpenCodeErrorLines $Text
    $m = [regex]::Match($lines, '(?im)^.*(\b429\b|rate[- _]?limit|too many requests).*$')
    if ($m.Success) { return $m.Value.Trim() }
    return $null
}

# --- Routes and quota-tracker (CLAUDE.md rule 17, docs/environment.md) ----------------------------
# A route is the provider a model name runs through. The usual route comes from the name's model id;
# DeepSeek and GLM also run on the Alibaba Token Plan (the user's decision of 2026-10-05).
$script:OpenCodeRouteQuotaProvider = @{ go = 'opencode_go'; zai = 'zai'; alibaba = 'alibaba'; openai = 'openai'; minimax = 'minimax' }

function Get-OpenCodeRouteName([string] $ModelId) {
    # The route of a model id: go, zai, alibaba, openai, or the id's provider prefix.
    if ($ModelId -like 'opencode-go/*') { return 'go' }
    if ($ModelId -like 'zai-coding-plan/*') { return 'zai' }
    if ($ModelId -like 'alibaba-token-plan/*') { return 'alibaba' }
    if ($ModelId -like 'openai/*') { return 'openai' }
    return ($ModelId -split '/')[0]
}

function Get-QuotaAvoid {
    # quota-tracker's /avoid: the providers out of quota. Answered is $false when the service does not
    # answer, and the callers then keep each model's usual route.
    # IC2_QUOTA_AVOID, when set, stands in for the service (a test hook for -SelfTest: a comma list
    # of provider names, or 'none'); it is never set in normal use.
    param([string] $Url = 'http://localhost:8765/avoid', [int] $TimeoutSec = 5)
    if ($env:IC2_QUOTA_AVOID) {
        return [pscustomobject]@{ Answered = $true; Providers = @($env:IC2_QUOTA_AVOID -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -and $_ -ne 'none' }) }
    }
    try { $r = Invoke-RestMethod -Uri $Url -TimeoutSec $TimeoutSec -ErrorAction Stop }
    catch { return [pscustomobject]@{ Answered = $false; Providers = @() } }
    $names = @(@($r) | ForEach-Object { if ($_ -is [string]) { $_ } elseif ($_ -and $_.provider) { [string]$_.provider } } | Where-Object { $_ })
    return [pscustomobject]@{ Answered = $true; Providers = $names }
}

function Resolve-OpenCodeRoute {
    # Which model id a name runs on. -Usual is its usual id, -Alibaba its Alibaba id when it has one.
    # -Route auto takes the usual route unless quota-tracker's /avoid lists its provider, then the
    # Alibaba id when Alibaba itself is not avoided; Avoided says no route of the model has quota.
    # When the tracker did not answer (-Answered:$false) the usual route is kept. An explicit -Route
    # the model has no id for is Refused; an explicit -Route whose provider /avoid lists is Avoided.
    param([string] $Usual, [string] $Alibaba, [string] $Route = 'auto', [bool] $Answered = $false, [string[]] $Avoid = @())
    $usualRoute = Get-OpenCodeRouteName $Usual
    $out = { param($r, $m, $why, $avoided, $refused) [pscustomobject]@{ Route = $r; Model = $m; Why = $why; Avoided = [bool]$avoided; Refused = [bool]$refused } }
    if ($Route -eq 'auto') {
        if (-not $Answered) { return (& $out $usualRoute $Usual 'quota-tracker did not answer: the usual route' $false $false) }
        $provider = $script:OpenCodeRouteQuotaProvider[$usualRoute]
        if (-not $provider -or $Avoid -notcontains $provider) { return (& $out $usualRoute $Usual 'the usual route' $false $false) }
        if ($Alibaba -and $usualRoute -ne 'alibaba' -and $Avoid -notcontains 'alibaba') { return (& $out 'alibaba' $Alibaba "$provider is avoided (quota-tracker)" $false $false) }
        return (& $out $usualRoute $Usual "$provider is avoided (quota-tracker) and no other route of this model has quota" $true $false)
    }
    $forced = if ($Route -eq $usualRoute) { $Usual } elseif ($Route -eq 'alibaba' -and $Alibaba) { $Alibaba } else { $null }
    if ($forced) {
        $provider = $script:OpenCodeRouteQuotaProvider[$Route]
        $avoided = $Answered -and $provider -and $Avoid -contains $provider
        return (& $out $Route $forced $(if ($avoided) { "-Route $Route, but $provider is avoided (quota-tracker)" } else { "-Route $Route" }) $avoided $false)
    }
    $known = @($usualRoute) + $(if ($Alibaba -and $usualRoute -ne 'alibaba') { @('alibaba') } else { @() })
    return (& $out $null $null "-Route $Route does not serve this model (its routes: $($known -join ', '))" $false $true)
}

function Invoke-OpenCodeWatched {
    [CmdletBinding()]
    param(
        # The agent for `--agent`; empty runs OpenCode's default agent.
        [string] $Agent,
        # The model, provider/model (no variant): `opencode/glm-5.3`.
        [Parameter(Mandatory)] [string] $Model,
        # The model's variant, for example high or max; the CLI's syntax is chosen by its major
        # version (1.x `--variant v`, 2.x `provider/model#v`). Empty means none.
        [string] $Variant,
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
        # Where the run's files go; deleted after an exit 0, kept for diagnosis otherwise. The
        # session's transcript export is KEPT here, <title>.export.json, after any run whose session started.
        [string] $LogDir = (Join-Path ([System.IO.Path]::GetTempPath()) 'ic2-opencode'),
        # Prints the CLI that would run and its argument line, and returns without starting a run
        # (no session, no model, no billing, no data directory made): the check that costs nothing.
        # It does start `opencode --version`, with the neutral probe directories described above.
        [switch] $WhatIf
    )
    $WorkDir = (Resolve-Path -LiteralPath $WorkDir).Path
    $cli = Get-OpenCodeCli
    $titleToken = [guid]::NewGuid().ToString('N').Substring(0, 12)
    $fullTitle = "$Title-$titleToken"
    $ocArgs = Get-OpenCodeRunArguments -Major $cli.Major -WorkDir $WorkDir -Agent $Agent -Model $Model -Variant $Variant -Title $fullTitle
    if ($WhatIf) {
        $inv = Get-OpenCodeRunInvocation -RunArguments $ocArgs -Prompt $Prompt
        Write-Host "opencode $($cli.Version) ($($cli.Exe); $($cli.Why)), working directory $WorkDir"
        Write-Host "  would run: $($inv.ArgumentLine) <prompt via $($inv.Delivery)>"
        return [pscustomobject]@{ WhatIf = $true; Exe = $cli.Exe; Version = $cli.Version; Major = $cli.Major; ArgumentLine = $inv.ArgumentLine; Arguments = $ocArgs; PromptDelivery = $inv.Delivery }
    }
    $isAlibaba = $Model -like 'alibaba-token-plan/*'
    if ($isAlibaba -and -not (Import-AlibabaTokenPlanKey)) {
        # Not an infrastructure failure: no other model or retry fixes it, so the caller stops.
        throw [System.InvalidOperationException]::new("Alibaba Token Plan: ALIBABA_TOKEN_PLAN_API_KEY is set neither in this process nor in the user environment, so $Model would fail 'Provider not found'. Set the user variable (never in an auth.json). Nothing was started.")
    }
    $state = Initialize-OpenCodeDataHome -Major $cli.Major -Alibaba:$isAlibaba
    if ($Model -like 'opencode-go/*') {
        # Before the run, so a missing Go login stops here with its cause instead of a "Provider not found" run.
        $goIn = Join-Path $state.Root 'go-login-check.in.txt'
        [System.IO.File]::WriteAllText($goIn, '')
        $goOk = try { Test-OpenCodeGoLogin -Cli $cli -WorkDir $WorkDir -Root $state.Root -InFile $goIn } finally { Remove-Item -LiteralPath $goIn -Force -ErrorAction SilentlyContinue }
        if (-not $goOk) {
            Restore-OpenCodeDataHome $state
            throw (New-OpenCodeFailure 'no authentication' "No authentication: the data directory $($state.Root)\data has no opencode-go login (OpenCode: 'Provider not found: opencode-go'), so $Model cannot run. The Go console login is stored in that directory's database, not in auth.json: run 'opencode console login' with XDG_DATA_HOME=$($state.Root)\data (each root, one per major version, needs its own login). Nothing was started.")
        }
    }
    # OpenCode 2.x takes its working directory from the PWD environment variable when one is set, not
    # from the process's own directory: a watcher started from a git-bash shell inherited PWD=<the
    # shell's directory>, so the run's session was recorded under that directory (found 2026-10-03,
    # T98) and the watcher never found it. Children get PWD = the run's directory, restored afterwards
    # with the XDG variables.
    $state.Saved['PWD'] = [System.Environment]::GetEnvironmentVariable('PWD', 'Process')
    [System.Environment]::SetEnvironmentVariable('PWD', $WorkDir, 'Process')
    try {
        try {
            $run = Invoke-OpenCodeRun -Cli $cli -RunArguments $ocArgs -Agent $Agent -Prompt $Prompt -WorkDir $WorkDir -Title $fullTitle `
                -StartupTimeoutSec $StartupTimeoutSec -TotalTimeoutSec $TotalTimeoutSec -IdleTimeoutSec $IdleTimeoutSec -PollSec $PollSec -LogDir $LogDir
        } catch {
            # An Alibaba key error that surfaced as an infrastructure failure (an exit without a
            # session carries the stderr tail) stops the caller instead of advancing its chain.
            $why = if ($isAlibaba) { Get-AlibabaFailure (Get-OpenCodeErrorLines $_.Exception.Message) $state.DataDir }
            if ($why) { throw [System.InvalidOperationException]::new($why) }
            throw
        }
        # Checked whatever the exit code (Sol's round-2 review of PR 783, R3).
        if ($isAlibaba) {
            $why = Get-AlibabaRunFailure $run $state.DataDir
            if ($why) { throw [System.InvalidOperationException]::new($why) }
        }
        return $run
    } finally {
        Restore-OpenCodeDataHome $state
    }
}

function Invoke-OpenCodeRun {
    param($Cli, [string[]] $RunArguments, [string] $Agent, [string] $Prompt, [string] $WorkDir, [string] $Title,
        [int] $StartupTimeoutSec, [int] $TotalTimeoutSec, [int] $IdleTimeoutSec, [int] $PollSec, [string] $LogDir)
    # The watched run itself (see Invoke-OpenCodeWatched, which resolves the CLI, builds the
    # arguments and owns the XDG environment around this call).
    $exe = $Cli.Exe
    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
    $outFile = Join-Path $LogDir "$Title.out.txt"
    $errFile = Join-Path $LogDir "$Title.err.txt"
    $inFile = Join-Path $LogDir "$Title.in.txt"
    $exportFile = Join-Path $LogDir "$Title.export.json"
    # stdin is a FILE, never the caller's: `opencode run` reads a non-terminal stdin to its end
    # before it creates a session, so an inherited pipe that never closes hangs it at "init" (both
    # 2026-09-28 hangs). The run's stdin is the prompt file, whose end is that end-of-file; the
    # helper commands (session list, export) get the empty $inFile.
    [System.IO.File]::WriteAllText($inFile, '')
    $promptFile = Join-Path $LogDir "$Title.prompt.txt"
    $inv = Get-OpenCodeRunInvocation -RunArguments $RunArguments -Prompt $Prompt
    [System.IO.File]::WriteAllText($promptFile, $inv.StdIn, [System.Text.UTF8Encoding]::new($false))
    $argLine = $inv.ArgumentLine

    $startedMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Host "opencode: running $($Cli.Version) ($($Cli.Major).x arguments) from $exe ($($Cli.Why))"
    $p = Start-Process -FilePath $exe -ArgumentList $argLine -WorkingDirectory $WorkDir -NoNewWindow -PassThru `
        -RedirectStandardInput $promptFile -RedirectStandardOutput $outFile -RedirectStandardError $errFile
    $null = $p.Handle   # keeps the handle, so ExitCode is readable after the exit
    Write-Host "opencode: pid $($p.Id), session title $Title, output $outFile, prompt via $($inv.Delivery)"

    $files = @($outFile, $errFile, $inFile, $promptFile)
    $startupMs = [long][Math]::Min($StartupTimeoutSec, $TotalTimeoutSec) * 1000
    $totalMs = [long]$TotalTimeoutSec * 1000
    try {
        # Startup watch: a session must appear within StartupTimeoutSec (and TotalTimeoutSec).
        $session = $null
        while (-not $session -and -not $p.HasExited) {
            $remainingMs = $startupMs - $clock.ElapsedMilliseconds
            if ($remainingMs -le 0) { break }
            if ($p.WaitForExit([int][Math]::Min($PollSec * 1000, $remainingMs))) { break }
            $session = Find-OpenCodeSession -Cli $Cli -WorkDir $WorkDir -Title $Title -StartedMs $startedMs -LogDir $LogDir `
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
            $seen = Find-OpenCodeSession -Cli $Cli -WorkDir $WorkDir -Title $Title -StartedMs $startedMs -LogDir $LogDir `
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
            $session = Find-OpenCodeSession -Cli $Cli -WorkDir $WorkDir -Title $Title -StartedMs $startedMs -LogDir $LogDir `
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
    # The session's transcript, kept under -LogDir. Its .info.agent is also how the requested --agent
    # is checked: did OpenCode load it? Checked on OpenCode's own evidence only, never on the
    # model's words (a reviewer reading these scripts quotes the warning text: the false positive
    # of PR #482's reviews, 2026-09-28): the session record is the authority when it can be read;
    # OpenCode's warning line on stderr, anchored to its exact form and the requested agent's
    # name, is the fallback when it cannot.
    $exported = Export-OpenCodeSession -Cli $Cli -WorkDir $WorkDir -SessionId $session.id -OutFile $exportFile -InFile $inFile
    if ($exported) { Write-Host "opencode: transcript export $exportFile" } else { Write-Host "opencode: the session export failed (no transcript kept)" }
    $requestedAgent = if ($Agent) { $Agent } else { $null }
    $sessionAgent = if ($requestedAgent -and $exported) { Get-OpenCodeSessionAgent $exportFile } else { $null }
    $agentFallback = if (-not $requestedAgent) { $false }
        elseif ($sessionAgent) { $sessionAgent -ne $requestedAgent }
        else { Test-OpenCodeAgentWarning -StdErr $stderr -Agent $requestedAgent }
    if ($p.ExitCode -eq 0) { Remove-Item -LiteralPath $files -Force -ErrorAction SilentlyContinue }
    else { Write-Host "opencode: exit $($p.ExitCode); files kept: $($files -join ', ')" }
    return [pscustomobject]@{
        Output    = ($stdout.TrimEnd() + "`n" + $stderr.TrimEnd()).Trim()
        StdOut    = $stdout
        StdErr    = $stderr
        ExitCode  = $p.ExitCode
        SessionId = $session.id
        Title     = $Title
        # True when OpenCode ran its default agent instead of the requested --agent.
        AgentFallback = $agentFallback
        SessionAgent  = $sessionAgent
        # What OpenCode's permission guard auto-rejected (the rejection ended the run), or $null.
        PermissionRejected = Get-OpenCodePermissionRejection ($stdout + "`n" + $stderr)
        Files     = if ($p.ExitCode -eq 0) { @() } else { $files }
        # The kept transcript (JSON), or $null when the export failed.
        ExportFile = if ($exported) { $exportFile } else { $null }
        Version   = $Cli.Version
        Seconds   = [int]$clock.Elapsed.TotalSeconds
    }
}
