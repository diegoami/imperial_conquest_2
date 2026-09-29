<#
.SYNOPSIS
    Asks Jev (TypeSafe AI's typed-decision model, through OpenRouter) named Choice, Score and
    yes-or-no (Noul) questions over one state or a directory of states, and prints each answer, its
    probability and a bucket (act, discard or claude) as JSON. Nothing is posted anywhere.

.DESCRIPTION
    The router of operating-guide.md §2.1. Delegate to it when a decision repeats over many items,
    the possible answers are known up front, and no written explanation is needed (§3's Jev
    preference). It never decides a review verdict, a merge or a label on its own.

    Reads the state (-StateFile) or one state per file (-ItemsDir), and the questions
    (-QuestionsFile: a JSON map of question name -> {type, instructions, criteria}, the shape of
    OpenRouter's System One request, see the wiki page "Jev"). For each state it sends ONE request
    with all the questions (POST <BaseUrl>/v1/systemone, model typesafe/jev-1.13), then:
      - a Noul answer is the probability p of yes. Bucket: act when p >= -ActAt (a confident yes),
        discard when p < -DiscardBelow (a confident no), claude in between.
      - a Choice answer is the chosen option and the probability of that option. Bucket: act when
        that probability >= -ActAt, otherwise claude (a low choice is never "discarded").
      - a Score answer is the probability-weighted level (0 = first criterion) and the answer's
        confidence as its probability. Bucket: act when confidence >= -ActAt, otherwise claude.
    "claude" means: hand this one to a Claude pass. The thresholds 0.9 and 0.1 are the split
    newscollection2027 settled on.

    Output: JSON on stdout and nothing else. With -StateFile, one object: question name ->
    {answer, probability, bucket}. With -ItemsDir, one JSON line per file, in file-name order:
    {item: <file name>, answers: {question name -> {answer, probability, bucket}}}. With -DryRun,
    the request that would be sent (URL, headers with the key redacted, body), in the same shapes,
    and no call is made and the key is not read. Diagnostics (cost, tokens, the model that
    answered) go to stderr.

    The key: the environment variable named by -KeyVariable (default OPENROUTER_API_KEY, the name
    newscollection2027 uses), read from the process environment first and, if empty there, from the
    Windows USER scope (a shell started before the variable was set does not have it). It is never
    read from a file, never printed and never logged.

    Every failure is one line on stderr, "jev-ask: <reason>", and exit 1: a missing key, a state
    over the size limit (checked for every item BEFORE the first call, so a batch never half-runs
    for that reason), a malformed questions file, a non-200 answer, a malformed response, a timeout.
    A network error (not an HTTP error status) is retried once; nothing else is retried.

    State: a .json file is sent as a JSON value (object, array or string); any other file is sent
    as one text string. The documented limit is 64k tokens for the state plus all questions and
    32k tokens for the state plus the longest question (docs.typesafe.ai/models); no tokenizer is
    documented, so the script estimates 4 characters per token and refuses a state whose
    characters plus the longest question exceed -MaxStateChars (default 96000, about 24k tokens).
    It does not truncate: shrink the state (filter first, as TypeSafe's "Jev 1.13 jaggedness"
    page advises) and ask again.

    Reused from newscollection2027 (PRs #85 and #87, src/nc/jevtrial.py and config/judge.yaml):
    the request body (model, state, questions with type and instructions), the response checks (a
    probability outside [0, 1], a boolean, or a missing answer is refused, never read as a no), the
    pinned model, the environment variable name, the 30-second timeout and the 0.9 / 0.1
    thresholds. The client itself is Python inside that repository and is not importable here, so
    it is re-written in PowerShell. Its retry policy (four attempts on 429, 5xx and timeouts) is
    deliberately narrower here, per the brief: one retry, on a network error only.

.PARAMETER StateFile
    One state: a text file, or a .json file. Exactly one of -StateFile and -ItemsDir is required.
.PARAMETER ItemsDir
    A directory; every file in it (not its subdirectories) is one state, asked the same questions.
.PARAMETER QuestionsFile
    A JSON map of named questions, e.g.
    {"fix or task": {"type": "choice", "instructions": "...", "criteria": {"fix": "...", "task": "..."}},
     "breaks play": {"type": "noul", "instructions": "..."}}
    Choice needs criteria (option -> description or null, at most 255 options); Score needs
    criteria (an array of 2 to 10 level descriptions); Noul's criteria ({true, false}) is optional.
.PARAMETER Model
    The model id (default typesafe/jev-1.13, pinned; ~typesafe/jev-latest moves).
.PARAMETER ActAt
    Probability at or above which the bucket is act (default 0.9).
.PARAMETER DiscardBelow
    Noul probability below which the bucket is discard (default 0.1).
.PARAMETER KeyVariable
    The name of the environment variable holding the key (default OPENROUTER_API_KEY).
.PARAMETER BaseUrl
    The API base (default https://openrouter.ai/api; the path /v1/systemone is appended). Must be
    https, or a loopback address for a local test server.
.PARAMETER TimeoutSec
    Seconds to wait for one response (default 30).
.PARAMETER MaxStateChars
    The state-size limit in characters (default 96000), see the description.
.PARAMETER DryRun
    Print the request(s) and call nothing.

.EXAMPLE
    pwsh scripts/jev-ask.ps1 -StateFile issue.txt -QuestionsFile questions.json -DryRun
.EXAMPLE
    pwsh scripts/jev-ask.ps1 -ItemsDir rendered\jev-trial\items -QuestionsFile rendered\jev-trial\questions.json > rendered\jev-trial\answers.jsonl
#>
[CmdletBinding()]
param(
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })] [string] $StateFile,
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })] [string] $ItemsDir,
    [Parameter(Mandatory)] [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })] [string] $QuestionsFile,
    [ValidateNotNullOrEmpty()] [string] $Model = 'typesafe/jev-1.13',
    [ValidateRange(0.0, 1.0)] [double] $ActAt = 0.9,
    [ValidateRange(0.0, 1.0)] [double] $DiscardBelow = 0.1,
    [ValidatePattern('^[A-Za-z_][A-Za-z0-9_]*$')] [string] $KeyVariable = 'OPENROUTER_API_KEY',
    [ValidatePattern('^(https://[^/]+.*|http://(localhost|127\.0\.0\.1|\[::1\])(:\d+)?.*)$')] [string] $BaseUrl = 'https://openrouter.ai/api',
    [ValidateRange(1, 600)] [int] $TimeoutSec = 30,
    [ValidateRange(100, 400000)] [int] $MaxStateChars = 96000,
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
# Redirected stdout must be UTF-8 (the request echo of -DryRun carries the states' non-ASCII characters).
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

function Stop-Jev([string] $Reason) {
    $line = ($Reason -replace '\s+', ' ').Trim()
    [Console]::Error.WriteLine("jev-ask: $line")
    exit 1
}

function Write-Diag([string] $Text) { [Console]::Error.WriteLine("jev-ask: $Text") }

function Read-StateFile([string] $Path) {
    $raw = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $Path).Path, [Text.UTF8Encoding]::new($false))
    if ([string]::IsNullOrWhiteSpace($raw)) { throw "state file $(Split-Path $Path -Leaf) is empty" }
    if ([IO.Path]::GetExtension($Path) -eq '.json') {
        try { $value = ConvertFrom-Json -InputObject $raw -NoEnumerate -Depth 50 }
        catch { throw "state file $(Split-Path $Path -Leaf) is not valid JSON" }
        return , $value
    }
    return , $raw.TrimEnd()
}

function Get-StateChars($State) {
    if ($State -is [string]) { return $State.Length }
    return (ConvertTo-Json -InputObject $State -Depth 50 -Compress).Length
}

function Read-Questions([string] $Path) {
    try { $questions = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Resolve-Path -LiteralPath $Path).Path)) -Depth 50 }
    catch { throw 'questions file is not valid JSON' }
    if ($questions -isnot [pscustomobject]) { throw 'questions file must be a JSON object mapping question names to questions' }
    $names = @($questions.PSObject.Properties.Name)
    if ($names.Count -eq 0) { throw 'questions file has no questions' }
    foreach ($p in $questions.PSObject.Properties) {
        $name = $p.Name
        $q = $p.Value
        if ($q -isnot [pscustomobject]) { throw "question '$name' must be an object" }
        $type = $q.type
        if ($type -notin 'noul', 'choice', 'score') { throw "question '$name': type must be noul, choice or score" }
        if ($null -eq $q.instructions -or ($q.instructions -is [string] -and [string]::IsNullOrWhiteSpace($q.instructions))) {
            throw "question '$name': instructions are required"
        }
        switch ($type) {
            'choice' {
                if ($q.criteria -isnot [pscustomobject]) { throw "question '$name': a choice needs criteria mapping each option to its description" }
                $n = @($q.criteria.PSObject.Properties).Count
                if ($n -lt 1 -or $n -gt 255) { throw "question '$name': a choice takes 1 to 255 options, got $n" }
            }
            'score' {
                $n = @($q.criteria).Count
                if ($q.criteria -isnot [array] -or $n -lt 2 -or $n -gt 10) { throw "question '$name': a score needs criteria, an array of 2 to 10 levels" }
            }
            'noul' {
                if ($null -ne $q.criteria -and $q.criteria -isnot [pscustomobject]) { throw "question '$name': a noul's criteria must be an object with true and false" }
            }
        }
    }
    return $questions
}

function Get-Bucket([string] $Type, [double] $P) {
    if ($P -ge $ActAt) { return 'act' }
    if ($Type -eq 'noul' -and $P -lt $DiscardBelow) { return 'discard' }
    return 'claude'
}

function Test-Probability($Value) {
    return ($Value -is [double] -or $Value -is [int] -or $Value -is [long] -or $Value -is [decimal]) -and $Value -ge 0 -and $Value -le 1
}

# One response -> [ordered] name -> {answer, probability, bucket}, or a throw.
function ConvertTo-Entries($Response, $Questions) {
    if ($Response -isnot [pscustomobject] -or $Response.answers -isnot [pscustomobject]) { throw 'malformed response: no answers object' }
    $entries = [ordered]@{}
    foreach ($qp in $Questions.PSObject.Properties) {
        $name = $qp.Name
        $type = $qp.Value.type
        $a = $Response.answers.PSObject.Properties[$name]
        if (-not $a) { throw "malformed response: no answer for '$name'" }
        $a = $a.Value
        if ($a -isnot [pscustomobject] -or $a.type -ne $type) { throw "malformed response: answer '$name' is not a $type" }
        switch ($type) {
            'noul' {
                if (-not (Test-Probability $a.noul)) { throw "malformed response: '$name' noul is not a probability" }
                $p = [double] $a.noul
                $entries[$name] = [ordered]@{ answer = ($p -ge 0.5); probability = $p; bucket = (Get-Bucket 'noul' $p) }
            }
            'choice' {
                if ($a.choice -isnot [string] -or $a.probabilities -isnot [pscustomobject]) { throw "malformed response: '$name' has no choice or probabilities" }
                $pp = $a.probabilities.PSObject.Properties[$a.choice]
                if (-not $pp -or -not (Test-Probability $pp.Value)) { throw "malformed response: '$name' choice has no probability" }
                $p = [double] $pp.Value
                $entries[$name] = [ordered]@{ answer = $a.choice; probability = $p; bucket = (Get-Bucket 'choice' $p) }
            }
            'score' {
                if ($a.score -isnot [double] -and $a.score -isnot [int]) { throw "malformed response: '$name' score is not a number" }
                if (-not (Test-Probability $a.confidence)) { throw "malformed response: '$name' score has no confidence" }
                $p = [double] $a.confidence
                $entries[$name] = [ordered]@{ answer = [double] $a.score; probability = $p; bucket = (Get-Bucket 'score' $p) }
            }
        }
    }
    return $entries
}

function Get-Key {
    $key = [Environment]::GetEnvironmentVariable($KeyVariable)
    if ([string]::IsNullOrEmpty($key)) { $key = [Environment]::GetEnvironmentVariable($KeyVariable, 'User') }
    if ([string]::IsNullOrEmpty($key)) {
        throw "no API key: environment variable $KeyVariable is empty in the process and at Windows user scope"
    }
    return $key
}

# One POST. Retries once, on a network error only. Returns the parsed JSON response.
function Invoke-Jev([string] $Url, [string] $Key, [string] $Json) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Json)
    $headers = @{ Authorization = "Bearer $Key" }
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        try {
            $r = Invoke-WebRequest -Uri $Url -Method Post -Headers $headers -ContentType 'application/json' `
                -Body $bytes -TimeoutSec $TimeoutSec -SkipHttpErrorCheck
            break
        }
        catch {
            if ($attempt -eq 2) { throw "network error (retried once): $(($_.Exception.Message -split "`n")[0])" }
            Write-Diag 'network error, retrying once'
            Start-Sleep -Seconds 1
        }
    }
    # Content is a byte[] when the server sends no text content type.
    $text = if ($r.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($r.Content) } else { "$($r.Content)" }
    if ($r.StatusCode -ne 200) {
        $msg = ''
        try { $msg = (ConvertFrom-Json -InputObject $text).error.message } catch { }
        if (-not $msg) { $msg = $text.Substring(0, [Math]::Min(120, $text.Length)) }
        throw "HTTP $($r.StatusCode) from the API: $msg"
    }
    try { return ConvertFrom-Json -InputObject $text -Depth 50 }
    catch { throw 'malformed response: the body is not JSON' }
}

try {
    if ([bool]$StateFile -eq [bool]$ItemsDir) { throw 'give exactly one of -StateFile and -ItemsDir' }
    if ($DiscardBelow -ge $ActAt) { throw '-DiscardBelow must be lower than -ActAt' }
    $questions = Read-Questions $QuestionsFile
    $longestQuestion = ($questions.PSObject.Properties | ForEach-Object { (ConvertTo-Json -InputObject $_.Value -Depth 50 -Compress).Length } | Measure-Object -Maximum).Maximum
    $totalQuestions = ($questions.PSObject.Properties | ForEach-Object { (ConvertTo-Json -InputObject $_.Value -Depth 50 -Compress).Length } | Measure-Object -Sum).Sum

    if ($StateFile) { $files = @(Get-Item -LiteralPath $StateFile) }
    else {
        $files = @(Get-ChildItem -LiteralPath $ItemsDir -File | Sort-Object Name)
        if ($files.Count -eq 0) { throw "no files in $ItemsDir" }
    }

    # Read and size-check every state before the first call.
    $items = foreach ($f in $files) {
        $state = Read-StateFile $f.FullName
        $chars = Get-StateChars $state
        if ($chars + $longestQuestion -gt $MaxStateChars -or $chars + $totalQuestions -gt 2 * $MaxStateChars) {
            throw "state $($f.Name) is $chars characters, over the limit ($MaxStateChars characters for the state plus the longest question, about $([int]($MaxStateChars / 4)) tokens); shrink it, nothing is truncated"
        }
        [pscustomobject]@{ Name = $f.Name; State = $state }
    }

    $url = $BaseUrl.TrimEnd('/') + '/v1/systemone'
    $key = if ($DryRun) { $null } else { Get-Key }

    foreach ($item in $items) {
        $body = [ordered]@{ model = $Model; state = $item.State; questions = $questions }
        $json = ConvertTo-Json -InputObject $body -Depth 50 -Compress
        if ($DryRun) {
            $request = [ordered]@{
                method = 'POST'; url = $url
                headers = [ordered]@{ Authorization = 'Bearer <redacted>'; 'Content-Type' = 'application/json' }
                body = $body
            }
            $out = if ($ItemsDir) { [ordered]@{ item = $item.Name; request = $request } } else { $request }
            [Console]::Out.WriteLine((ConvertTo-Json -InputObject $out -Depth 60 -Compress))
            continue
        }
        $response = Invoke-Jev $url $key $json
        $entries = ConvertTo-Entries $response $questions
        $usage = $response.usage
        if ($usage) { Write-Diag "$($item.Name): model $($response.model), $($usage.input_tokens) input tokens, cost $($usage.cost)" }
        $out = if ($ItemsDir) { [ordered]@{ item = $item.Name; answers = $entries } } else { $entries }
        [Console]::Out.WriteLine((ConvertTo-Json -InputObject $out -Depth 10 -Compress))
    }
}
catch {
    Stop-Jev $_.Exception.Message
}
