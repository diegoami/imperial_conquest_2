#Requires -Version 5.0
<#
.SYNOPSIS
T149: drive scripts/generate-sounds.py to produce the sfx.* WAVs.

.DESCRIPTION
The .ps1 wrapper exists so the operator can run a single, familiar PowerShell command instead
of the python script directly. Every argument the python script accepts is forwarded unchanged
("--key <sfx key>", "--all", "--self-check"); no argument triggers the dry run that prints
one line per key with its prompt and target duration and makes no network request.

The .py does the real work -- the ElevenLabs call, the WAV conversion, the envelope check, the
self-test. This .ps1 only locates python and forwards the arguments.

The ElevenLabs key is read from $env:ELEVENLABS_API_KEY by the .py (process, then Windows
user scope, like OPENROUTER_API_KEY); this wrapper never sees the key, never logs it, and never
writes it to a file.

.PARAMETER PythonExe
Optional override of the python executable. Defaults to the first python on PATH, then
py.exe, then python3. The check for the existence of urllib.request is the standard library,
so any modern Python 3 will run the .py without an install step.
#>

[CmdletBinding()]
param(
    [string]$PythonExe
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$pythonScript = Join-Path -Path $scriptDir -ChildPath "generate-sounds.py"

if (-not (Test-Path $pythonScript)) {
    throw "generate-sounds.py not found at $pythonScript"
}

if (-not $PythonExe) {
    $candidates = @("python", "python3", "py")
    foreach ($candidate in $candidates) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($command) {
            $PythonExe = $command.Source
            break
        }
    }
}

if (-not $PythonExe) {
    throw "No python executable found on PATH (looked for: python, python3, py). " +
          "Install Python 3 and try again."
}

$forwarded = @()
if ($args.Count -gt 0) {
    $forwarded = @($args)
}

& $PythonExe $pythonScript @forwarded
exit $LASTEXITCODE
