# Primary Windows player: owner-run playtest evidence

This is the handoff for DOCX §§2–4 and 6–8, timing issue #23 and performance issue #24. **On 2026-10-14, the owner lifted the previous foreground/OS-input deferral by requesting “start the human playtest now”.** The owner confirmed this is their first time playing (`human / first_attempt / basic`). That attempt is now closed and retained as **incomplete**. Completion, timing, audio, performance and human acceptance remain unverified.

The human kit is `artifacts/primary-player-playtest/attempt-6e914f6fd871408794a570e817ea79c7`. The player exited normally with code 0 at `2026-10-14T06:23:06.5409484Z`; verification before and after play matched all 199 engine files / 133,842,184 bytes. Final state was Running / Puzzle, `FirstEnemyDefeated` and `MainPuzzleSolved`, 176 HP and 210.978 s elapsed. This is an unfinished session, not a route completion. The owner supplied first-attempt familiarity but no interruption declaration; the assistant recorded `unknown`, with that provenance retained in `interruption-provenance.json`. The new `timing-review.json` rejected the missing uninterrupted-run declaration, returned 1 and kept `acceptance_passed: false`. Raw files and their hash index remain unchanged. The dated local recorder and original recorder are both retained.

The owner subsequently requested **“use computer use to autoplay and test”**. The separate setup kit `attempt-3093873a892e41b7bb75450b52086be4` exited normally without gameplay input; a later PTY extraction failed before launch and its partial kit is retained. The later d272 player has exited: reported code 0 and unchanged identity, but a native crash-report/fatal diagnostic and FMOD fallback with no sound. The agent sent no gameplay input; recorded late movement and the first fight have unqualified external provenance. A separate closure review and final hash index preserve the raw records without a qualifying timing manifest. Windows is now unlocked and no game window/process is active. The owner subsequently chose “Hidden Unity route test”; the [completed hidden editor run](validation/hidden-routes-2026-10-14.md) passed 10/10 cases, including six gameplay cases and four helpers. Its known-route basic/secret traces are separate from this human playtest procedure and do not qualify standalone OS-input or human pacing acceptance. The [standalone report](validation/standalone-playtest-2026-10-14.md) links the exact retained receipts. It is not human evidence. The human log's seven camera safety messages have no recorded location or duration and do not establish a lasting visible defect.

The delivered primary level is `ForsakenCastle`, source `5af8861c39bd5fbf4d324690e5a74b1d12aae333`, Unity `6000.6.3f1`. Its retained executable is `C:\Users\andrz\git\shadows-of-the-forsaken\artifacts\goal-20261008\committed-build\player\ShadowsOfTheForsaken.exe`. Use a fresh extraction of the verified ZIP for each attempt; leave that retained player and its evidence intact.

| Local input | Trusted SHA-256 |
| --- | --- |
| `Builds/ShadowsOfTheForsaken-Windows-x64-20261008.zip` | `3c328a9382826bce0c1090fb164f09e6b572d04d6c03fd408e2a520409b71984` |
| `artifacts/goal-20261008/committed-build/player/BUILD-INFO.json` | `aa3fbf3c43943579fafe956a8fba43c5348232d3d48ef22f81b8eaa0e6a93578` |
| `artifacts/goal-20261008/committed-build/identity/source.zip` | `fc953c3d4e8f7c4554fc4d14451891f3eec2cce3706e2dd84d9c481efb3ecb80` |

Paths in the table are relative to `C:\Users\andrz\git\shadows-of-the-forsaken`. The ZIP contains the complete player and supplementary receipt, not the source archive or reporting tools. The recipe copies the receipt and source archive into a new local evidence kit so the timing manifest can use confined relative references. It verifies every player file before and after play with the [official identity tool](player-build-identity.md).

Use PowerShell 7 and the installed Python 3.11 path below. Save **both blocks, in order, in one `.ps1` file outside any player directory**. Run it once per attempt, explicitly supplying `-InputOrigin` (`human` or `automation`), `-RouteFamiliarity` (`first_attempt`, `repeat` or `known_route`) and `-Route` (`basic` or `secret`). These describe the actual participant/run; the script neither infers them nor supplies input. The first block verifies and launches the player; the second preserves actual data and asks separately about interruptions. A missing or unknown answer is not an uninterrupted-run declaration.

```powershell
param(
    [Parameter(Mandatory=$true)][ValidateSet('human','automation')][string]$InputOrigin,
    [Parameter(Mandatory=$true)][ValidateSet('first_attempt','repeat','known_route')][string]$RouteFamiliarity,
    [Parameter(Mandatory=$true)][ValidateSet('basic','secret')][string]$Route
)
$ErrorActionPreference = 'Stop'
$repo = 'C:\Users\andrz\git\shadows-of-the-forsaken'
$python = 'C:\Users\andrz\AppData\Local\Programs\Python\Python311\python.exe'
$commit = '5af8861c39bd5fbf4d324690e5a74b1d12aae333'
$receiptHash = 'aa3fbf3c43943579fafe956a8fba43c5348232d3d48ef22f81b8eaa0e6a93578'
$zipHash = '3c328a9382826bce0c1090fb164f09e6b572d04d6c03fd408e2a520409b71984'
$sourceHash = 'fc953c3d4e8f7c4554fc4d14451891f3eec2cce3706e2dd84d9c481efb3ecb80'
$zip = Join-Path $repo 'Builds\ShadowsOfTheForsaken-Windows-x64-20261008.zip'
$source = Join-Path $repo 'artifacts\goal-20261008\committed-build\identity\source.zip'
$identityTool = Join-Path $repo 'tools\player_build_manifest.py'
function Sha([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Save-New([string]$path, [string]$text) {
    $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try { $bytes = [Text.UTF8Encoding]::new($false).GetBytes($text); $stream.Write($bytes,0,$bytes.Length) }
    finally { $stream.Dispose() }
}
if (!(Test-Path -LiteralPath $python -PathType Leaf)) { throw 'Python is missing.' }
if ((Sha $zip) -ne $zipHash -or (Sha $source) -ne $sourceHash) { throw 'Delivery/source hash mismatch.' }
$runId = 'attempt-' + [guid]::NewGuid().ToString('N')
$kit = Join-Path $repo ('artifacts\primary-player-playtest\' + $runId)
if (Test-Path -LiteralPath $kit) { throw 'Use a new evidence kit.' }
$player = Join-Path $kit 'player'
$attempt = Join-Path $kit 'attempt'
$identity = Join-Path $kit 'identity'
New-Item -ItemType Directory -Path $kit,$player,$attempt,$identity | Out-Null
Expand-Archive -LiteralPath $zip -DestinationPath $player
$receipt = Join-Path $player 'BUILD-INFO.json'
if ((Sha $receipt) -ne $receiptHash) { throw 'Extracted receipt hash mismatch.' }
Copy-Item -LiteralPath $receipt -Destination (Join-Path $identity 'BUILD-INFO.json')
Copy-Item -LiteralPath $source -Destination (Join-Path $identity 'source.zip')
$kitSource = Join-Path $identity 'source.zip'
function Verify-Player([string]$label) {
    $output = & $python $identityTool verify --player-dir $player --receipt-sha256 $receiptHash --source-archive $kitSource --commit $commit 2>&1
    $code = $LASTEXITCODE
    Save-New (Join-Path $attempt ($label + '.txt')) ($output | Out-String)
    return ($code -eq 0)
}
if (!(Verify-Player 'verify-before')) { throw 'Player identity refused before launch.' }
$exe = Join-Path $player 'ShadowsOfTheForsaken.exe'
$playerLog = Join-Path $attempt 'player.log'
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $exe
$start.WorkingDirectory = $player
$start.UseShellExecute = $false
$start.Environment['SHADOWS_PLAYER_EVIDENCE_DIR'] = $attempt
foreach ($argument in @('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-force-d3d11','-logFile',$playerLog)) {
    $start.ArgumentList.Add($argument)
}
$startedUtc = [DateTime]::UtcNow.ToString('o')
$process = [Diagnostics.Process]::Start($start)
$pidOwned = $process.Id
Write-Host "Evidence kit: $kit"
Write-Host 'Play this attempt normally. After completion, close with Alt+F4; do not press R.'
$process.WaitForExit() # The user closes the game; no input, focus changes or forced cleanup.
$exitCode = $process.ExitCode
$process.Dispose()
$afterValid = Verify-Player 'verify-after'
Save-New (Join-Path $attempt 'launch.json') (@{
    executable=$exe; arguments=@($start.ArgumentList); pid=$pidOwned
    started_utc=$startedUtc; ended_utc=[DateTime]::UtcNow.ToString('o'); exit_code=$exitCode
    receipt_sha256=$receiptHash; source_commit=$commit; player_identity_valid_after=$afterValid
} | ConvertTo-Json -Depth 5)
```

After the user closes the player, this continuation preserves the files and creates the manifest from the actual final status. A completed run needs normal shutdown; a death, reset or interruption remains separate evidence and cannot be stitched into a completion.

```powershell
do {
    $interruption = Read-Host 'Interruptions during this attempt: none-observed / observed / unknown'
} until ($interruption -in @('none-observed','observed','unknown'))
$interruptionsObserved = switch ($interruption) { 'none-observed' { $false }; 'observed' { $true }; 'unknown' { $null } }
Save-New (Join-Path $attempt 'operator-declaration.json') (@{
    input_origin=$InputOrigin; route_familiarity=$RouteFamiliarity; route=$Route
    interruptions=$interruption; recorded_utc=[DateTime]::UtcNow.ToString('o')
} | ConvertTo-Json)
$names = @('events.jsonl','performance.jsonl','room-performance.jsonl','status.json','player.log')
$missing = @($names | Where-Object { !(Test-Path -LiteralPath (Join-Path $attempt $_) -PathType Leaf) })
$index = @(Get-ChildItem -LiteralPath $attempt,$identity -File | ForEach-Object {
    @{ path=[IO.Path]::GetRelativePath($kit,$_.FullName).Replace('\','/'); bytes=$_.Length; sha256=(Sha $_.FullName) }
})
Save-New (Join-Path $kit 'hash-index.json') (@{ schema_version=1; files=$index; missing_attempt_files=$missing } | ConvertTo-Json -Depth 6)
if (!$afterValid -or $exitCode -ne 0 -or $missing.Count) { throw "Attempt needs review; preserve the kit at $kit." }
if ($missing.Count -eq 0) {
    $status = Get-Content -LiteralPath (Join-Path $attempt 'status.json') -Raw | ConvertFrom-Json
    $references = @{}
    foreach ($name in @('events','performance','status')) {
        $file = if ($name -eq 'status') { 'status.json' } else { $name + '.jsonl' }
        $references[$name] = @{ path=('attempt/' + $file); sha256=(Sha (Join-Path $attempt $file)) }
    }
    Save-New (Join-Path $kit 'run.json') (@{
        schema_version=1; run_id=$runId; session_id=$status.sessionId; revision=$commit
        input_origin=$InputOrigin; route_familiarity=$RouteFamiliarity; route=$Route
        interruptions_observed=$interruptionsObserved
        build_receipt=@{ path='identity/BUILD-INFO.json'; sha256=(Sha (Join-Path $identity 'BUILD-INFO.json')) }
        source_archive=@{ path='identity/source.zip'; sha256=(Sha $kitSource) }; files=$references
    } | ConvertTo-Json -Depth 8)
}
Write-Host "Preserved $kit. run.json is a declared review input, not an acceptance result."
```

The environment variable belongs only to the launched process; the parent environment is unchanged. The launch is interactive and windowed, with D3D11 and 1280×720 requested; no batch/headless or audio-disabling flag is used. Actual display/hardware settings in the observer must still be checked. The script never sends input, activates a window, changes progression/time/quality or stops a process. It waits until the user closes this player.

Play using W/S, A/D, Space, LMB and E. On reaching the terminal completion screen, use **Alt+F4 without pressing R** so completion and normal-exit remain in one attempt. A death, reset, interruption or incomplete attempt is retained separately; do not alter its files, relabel it as completed or splice sessions. Test death/restart in separate untimed attempts; their evidence remains useful even though the timing reader rejects them.

Preserve `events.jsonl`, `performance.jsonl`, **`room-performance.jsonl`**, final `status.json`, `player.log`, launch/operator records, both identity checks and `hash-index.json`. The timing manifest's `files` schema remains exactly `events`, `performance`, `status`; room-performance and the player log are retained and hashed alongside it for separate performance/log review. Missing/capped records or failed identity checks require review, not replacement data. An early script error may leave a partial kit; preserve it. Missing output is not a pass, and a failed post-play identity check, nonzero player exit or missing raw file prevents creating `run.json`.

For a completed attempt, pass its actual generated `run.json` path to `tools/report_route_timing.py` as described in [timing validation](timing-validation.md). Its references stay under that kit. Combine **three distinct basic attempts and one secret attempt** from this same build; declare each participant's actual familiarity. The reader rejects incomplete/interrupted evidence and always reports `acceptance_passed: false`. Keep the kits and a new report; do not overwrite earlier attempts. No example here is a captured result.

For hardware review, retain every room visit, including empty/short visits, warmup, exclusions and unobserved tails. Check actual resolution, hardware and configuration stability, full-route coverage and nonempty measurements against the [provisional performance criteria](docx-acceptance.md#opt-in-performance-evidence). Room frame intervals and managed/Unity allocator counters do not attribute GPU, light or shadow cost; that remaining requirement needs separate profiling and relevant route repeats after a demonstrated optimization. First-attempt wayfinding, combat/clue readability, atmosphere, audible output and the DOCX 2–3 minute basic-route target still require human review.
