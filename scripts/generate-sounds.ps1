#Requires -Version 5.0
<#
.SYNOPSIS
T149: drive scripts/generate-sounds.py to produce the sfx.* WAVs.

.DESCRIPTION
The .ps1 wrapper exists so the operator can run a single, familiar PowerShell command instead
of the python script directly. The documented flags ("--key <sfx key>", "--all", "--self-check")
are declared as parameters and forwarded unchanged; no argument triggers the dry run that prints
one line per key with its prompt and target duration and makes no network request.

The .py does the real work -- the ElevenLabs call, the WAV conversion, the high-pass and RMS
normalisation, the envelope check, the self-test. This .ps1 only locates python and forwards
the arguments.

The ElevenLabs key is read from $env:ELEVENLABS_API_KEY by the .py (process, then Windows
user scope, like OPENROUTER_API_KEY); this wrapper never sees the key, never logs it, and never
writes it to a file.

.PARAMETER Key
The single sfx.* key to generate (forwards "--key <value>").

.PARAMETER All
Generate every sfx.* key (forwards "--all").

.PARAMETER SelfCheck
Run the offline conversion self-test (forwards "--self-check").

.PARAMETER PostprocessOnly
Re-postprocess one key offline from the kept raw response or the committed WAV (forwards
"--postprocess-only <value>"); makes no API call and spends no credit.

 .PARAMETER PythonExe
 Optional override of the python executable. Defaults to the first python on PATH, then
 py.exe, then python3. The dry run, the headerless-PCM/WAV conversion and the self-test use
 only the standard library, so any modern Python 3 runs them without an install step; if the
 API returns an MP3 instead, the .py stops and names the packages it needs (numpy and
 soundfile) rather than silently requiring them.
#>

[CmdletBinding()]
param(
    [string]$Key,
    [switch]$All,
    [switch]$SelfCheck,
    [string]$PostprocessOnly,
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

# The documented flags, accepted as parameters and forwarded unchanged (Sol's review of
# PR #886, R1): a bare `pwsh scripts/generate-sounds.ps1 -SelfCheck` must reach the .py's
# --self-check, not die in PowerShell's parameter binding.
$forwarded = @()
if ($Key) {
    $forwarded += @("--key", $Key)
}
if ($All) {
    $forwarded += "--all"
}
if ($SelfCheck) {
    $forwarded += "--self-check"
}
if ($PostprocessOnly) {
    $forwarded += @("--postprocess-only", $PostprocessOnly)
}

& $PythonExe $pythonScript @forwarded
exit $LASTEXITCODE
