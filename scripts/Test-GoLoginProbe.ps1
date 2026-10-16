# Offline regression for #687. No provider calls, login files or credentials are used.
$ErrorActionPreference = 'Stop'
$watcher = Join-Path $PSScriptRoot 'Invoke-OpenCodeWatched.ps1'
$scratch = [IO.Directory]::CreateTempSubdirectory('ic2-go-probe-').FullName
$worker = {
    param($Watcher, $Root, $Name, $Mode)
    . $Watcher
    $script:round = 0
    function Stop-OpenCodeTree { param($Process) }
    function Start-Process {
        param($FilePath, $ArgumentList, $WorkingDirectory, [switch]$NoNewWindow,
              [switch]$PassThru, $RedirectStandardInput, $RedirectStandardOutput, $RedirectStandardError)
        $script:round++
        # Exclusive handles reproduce the original Windows redirect-file collision.
        $streams = @()
        try {
            foreach ($path in @($RedirectStandardInput, $RedirectStandardOutput, $RedirectStandardError)) {
                $streams += [IO.File]::Open($path, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            }
            if ($script:round -eq 1) {
                [IO.File]::WriteAllLines((Join-Path $Root "$Name.paths"), @($RedirectStandardInput, $RedirectStandardOutput, $RedirectStandardError))
                if ($Mode -eq 'parallel') {
                    [IO.File]::WriteAllText((Join-Path $Root "$Name.ready"), '')
                    $deadline = [DateTime]::UtcNow.AddSeconds(15)
                    while (@(Get-ChildItem -LiteralPath $Root -Filter '*.ready').Count -lt 2) {
                        if ([DateTime]::UtcNow -gt $deadline) { throw 'concurrent stream barrier timed out' }
                        Start-Sleep -Milliseconds 20
                    }
                }
            }
        } finally { foreach ($stream in $streams) { $stream.Dispose() } }
        if ($Mode -eq 'throw') { throw 'synthetic launch failure' }
        $text = if ($Mode -eq 'missing-provider') { 'Provider not found: opencode-go' }
                elseif ($Mode -eq 'missing-account' -and $script:round -eq 2) { 'No accounts found' }
                else { 'available' }
        [IO.File]::WriteAllText($RedirectStandardOutput, $text)
        [IO.File]::WriteAllText($RedirectStandardError, '')
        $process = [pscustomobject]@{ ExitCode = $(if ($Mode -eq 'missing-provider') { 1 } else { 0 }); TimedOut = ($Mode -eq 'timeout') }
        $process | Add-Member ScriptMethod WaitForExit { param($Milliseconds) return -not $this.TimedOut }
        return $process
    }
    $ok = Test-OpenCodeGoLogin -Cli ([pscustomobject]@{ Major=1; Exe='unused' }) -WorkDir $Root -Root $Root
    [pscustomobject]@{ Name=$Name; Mode=$Mode; Ok=$ok; Rounds=$script:round }
}
$jobs = @()
try {
    foreach ($name in @('first','second')) { $jobs += Start-Job -ScriptBlock $worker -ArgumentList $watcher,$scratch,$name,'parallel' }
    $results = @($jobs | Receive-Job -Wait -ErrorAction Stop)
    if ($results.Count -ne 2 -or @($results | Where-Object { -not $_.Ok -or $_.Rounds -ne 2 }).Count) { throw 'Concurrent probes did not both complete both commands' }
    $paths = @(Get-Content (Join-Path $scratch 'first.paths')) + @(Get-Content (Join-Path $scratch 'second.paths'))
    if (@($paths | Select-Object -Unique).Count -ne 6) { throw 'Concurrent probes shared stream paths' }
    foreach ($mode in @('missing-provider','missing-account','throw','timeout')) {
        $result = & $worker $watcher $scratch $mode $mode
        $expected = $mode -in @('throw','timeout')
        if ($result.Ok -ne $expected) { throw "Incorrect authentication policy for $mode" }
    }
    if (@(Get-ChildItem -LiteralPath $scratch -Filter 'go-login-check-*').Count) { throw 'Probe streams leaked after success, refusal, exception or timeout' }
    Write-Output 'GoLoginProbe: concurrent streams distinct; authentication policy and all cleanup paths PASS'
} finally {
    foreach ($job in $jobs) { Remove-Job -Job $job -Force -ErrorAction SilentlyContinue }
    # Only this test-created temporary directory is removed.
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
