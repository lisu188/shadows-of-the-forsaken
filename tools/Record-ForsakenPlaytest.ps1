param(
    [Parameter(Mandatory=$true)][ValidateSet('human','automation')][string]$InputOrigin,
    [ValidateSet('first_attempt','repeat','known_route')][string]$RouteFamiliarity,
    [Parameter(Mandatory=$true)][ValidateSet('basic','secret')][string]$Route
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = Split-Path $PSScriptRoot -Parent
$python = 'C:\Users\andrz\AppData\Local\Programs\Python\Python311\python.exe'
$commit = '08b26973d36fd22b0864649073b3ffe334eec62b'
$receiptHash = '83ffdc101ad51227e5ae32de2332bfbbd2fdc92a47dca055772d4eda7cd2c371'
$zipHash = 'c507664ae0be9fd5ac465afcb44bf33e4677537859a731c625ab3b62d0f5b372'
$sourceHash = 'ec101e80f4b748dce69387996f5cda44a61fb80a19a971d2157a5c017d26c566'
$zip = Join-Path $repo 'Builds\ShadowsOfTheForsaken-Windows-x64-20261014-08b2697.zip'
$source = Join-Path $repo 'artifacts\goal-20261014\release-build\source.zip'
$identityTool = Join-Path $repo 'tools\player_build_manifest.py'
function Sha([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Save-New([string]$path, [string]$text) {
    $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try { $bytes = [Text.UTF8Encoding]::new($false).GetBytes($text); $stream.Write($bytes,0,$bytes.Length) }
    finally { $stream.Dispose() }
}
function Initialize-RecorderNative {
    if ('ForsakenRecorder.Native' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
namespace ForsakenRecorder {
    public static class Native {
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError=true)] static extern bool CloseHandle(IntPtr handle);
        [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError=true)] static extern bool GetTokenInformation(IntPtr token, int info, IntPtr buffer, uint size, out uint needed);
        [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern uint SetThreadExecutionState(uint flags);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        public static uint IntegrityRid() { return IntegrityRid(GetCurrentProcess()); }
        public static uint IntegrityRid(IntPtr process) {
            IntPtr token = IntPtr.Zero, buffer = IntPtr.Zero;
            try {
                if (!OpenProcessToken(process, 8, out token)) throw new Win32Exception();
                uint size;
                GetTokenInformation(token, 25, IntPtr.Zero, 0, out size);
                if (size < IntPtr.Size + 4) throw new InvalidOperationException("Missing integrity label");
                buffer = Marshal.AllocHGlobal((int)size);
                if (!GetTokenInformation(token, 25, buffer, size, out size)) throw new Win32Exception();
                IntPtr sid = Marshal.ReadIntPtr(buffer);
                byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                if (count == 0) throw new InvalidOperationException("Empty integrity SID");
                return (uint)Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
            } finally {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (token != IntPtr.Zero) CloseHandle(token);
            }
        }
    }
    // A dedicated thread owns both calls: PowerShell may move between runspace threads.
    // The injectable delegate is used only by synthetic tests, without any OS power request.
    public sealed class PowerLease : IDisposable {
        readonly ManualResetEventSlim ready = new ManualResetEventSlim(false);
        readonly ManualResetEventSlim release = new ManualResetEventSlim(false);
        readonly Thread thread;
        int disposed;
        public uint Flags { get { return 0x80000003u; } }
        public uint PreviousFlags { get; private set; }
        public uint OwnerThreadId { get; private set; }
        public bool Acquired { get { return PreviousFlags != 0; } }
        public bool Restored { get; private set; }
        public string RequestedUtc { get; private set; }
        public string ReleasedUtc { get; private set; }
        public string Error { get; private set; }
        public PowerLease() : this(Native.SetThreadExecutionState, Native.GetCurrentThreadId) { }
        public PowerLease(Func<uint,uint> setState, Func<uint> threadId) {
            thread = new Thread(() => {
                try {
                    OwnerThreadId = threadId();
                    RequestedUtc = DateTime.UtcNow.ToString("o");
                    PreviousFlags = setState(Flags);
                    if (!Acquired) { Error = "SetThreadExecutionState acquisition failed"; return; }
                    ready.Set();
                    release.Wait();
                } catch (Exception ex) { Error = ex.ToString(); }
                finally {
                    ready.Set();
                    if (Acquired) {
                        try {
                            Restored = setState(PreviousFlags | 0x80000000u) != 0;
                            if (!Restored) Error = "SetThreadExecutionState restoration failed";
                        } catch (Exception ex) { Error = ex.ToString(); }
                        ReleasedUtc = DateTime.UtcNow.ToString("o");
                    }
                }
            });
            thread.IsBackground = true;
            thread.Start();
            ready.Wait();
        }
        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            release.Set();
            thread.Join();
            ready.Dispose();
            release.Dispose();
        }
    }
}
'@
}
function Get-IntegrityReview([uint32]$Rid) {
    $label = if ($Rid -lt 0x1000) { 'untrusted' } elseif ($Rid -lt 0x2000) { 'low' } elseif ($Rid -lt 0x3000) { 'medium' } elseif ($Rid -lt 0x4000) { 'high' } else { 'system_or_higher' }
    return @{ pid=$PID; rid=$Rid; label=$label; normal_integrity=($Rid -ge 0x2000); captured_utc=[DateTime]::UtcNow.ToString('o') }
}
function Get-PlayerLogReview([string]$Text, [int]$ExitCode) {
    $fatal = 'Native Crash Reporting|Got a UNKNOWN while executing native code|\ba fatal error in (?:the )?(?:mono|runtime)|\b(?:SIGSEGV|SIGABRT|EXCEPTION_ACCESS_VIOLATION)\b|\b(?:fatal (?:error|signal)|fatal-runtime|FATAL UNHANDLED EXCEPTION)\b|Crash!!!'
    $audio = 'FMOD[^\r\n]*(?:failed|nosound|no sound)|default audio device[^\r\n]*failed|audio system failed to initialize|Error initializing output device'
    $candidate = '(?:\b(?:exception|fatal|crash)\b|\berror\s*:|\[error\]|Failed to|evidence disabled)'
    $result = @{ native_fatal_markers=@(); audio_failure_markers=@(); diagnostic_candidates=@(); graphics_threading_observations=@(); exit_code=$ExitCode }
    $number = 0
    foreach ($line in ($Text -split '\r?\n')) {
        $number++
        $row = @{ line=$number; text=$line }
        if ($line -match $fatal) { $result.native_fatal_markers += $row }
        if ($line -match $audio) { $result.audio_failure_markers += $row }
        if ($line -match $candidate) { $result.diagnostic_candidates += $row }
        if ($line -match '\bkGfxThreadingMode\w+') { $result.graphics_threading_observations += @{ line=$number; text=$line; mode=$Matches[0] } }
    }
    $result.runtime_log_checks_passed = ($ExitCode -eq 0 -and $result.native_fatal_markers.Count -eq 0 -and $result.audio_failure_markers.Count -eq 0)
    return $result
}
function Read-RequiredChoice([string]$Prompt, [string[]]$Choices) {
    do {
        $value = Read-Host $Prompt
        if ([string]::IsNullOrWhiteSpace($value)) { throw 'No explicit operator declaration was supplied; retain the kit without run.json.' }
    } until ($value -in $Choices)
    return $value
}
function Get-ExitEventReview([string]$EventsText, [string]$StatusText) {
    try {
        $status = $StatusText | ConvertFrom-Json -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($status.sessionId)) { throw 'Final status has no session ID.' }
        $events = @($EventsText -split '\r?\n' | Where-Object { ![string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_ | ConvertFrom-Json -ErrorAction Stop })
        if (!$events.Count) { throw 'No observer events.' }
        $last = $events[-1]
        return @{ normal_exit_event_observed=($last.kind -eq 'normal-exit' -and $last.sessionId -eq $status.sessionId)
            session_id=$status.sessionId; event_count=$events.Count; final_event_kind=$last.kind; final_event_session_id=$last.sessionId }
    } catch { return @{ normal_exit_event_observed=$false; error=($_ | Out-String).Trim() } }
}
function Get-CrashFolderReview([string]$AttemptDirectory) {
    $root = [IO.Path]::GetFullPath($AttemptDirectory)
    $directory = Join-Path $root 'crashes'
    $review = @{ requested_directory=$directory; captured_utc=[DateTime]::UtcNow.ToString('o')
        confined=$false; files=@(); errors=@(); crash_artifacts_present=$false; crash_checks_passed=$false }
    try {
        foreach ($path in @($root,$directory)) {
            $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
            if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Crash inventory refuses a linked or non-directory path: $path" }
        }
        $prefix = $directory.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $pending = [Collections.Generic.Stack[string]]::new()
        $pending.Push($directory)
        while ($pending.Count) {
            foreach ($item in (Get-ChildItem -LiteralPath $pending.Pop() -Force -ErrorAction Stop)) {
                if (!$item.FullName.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Crash inventory refuses a linked or unconfined entry: $($item.FullName)" }
                if ($item.PSIsContainer) { $pending.Push($item.FullName); continue }
                $bytes = $item.Length; $modified = $item.LastWriteTimeUtc
                $hash = Sha $item.FullName
                $item.Refresh()
                if ($item.Length -ne $bytes -or $item.LastWriteTimeUtc -ne $modified) { throw "Crash artifact changed while hashing: $($item.FullName)" }
                $review.files += @{ path=[IO.Path]::GetRelativePath($root,$item.FullName).Replace('\','/'); bytes=$bytes; sha256=$hash }
            }
        }
        $review.confined = $true
    } catch { $review.errors += ($_ | Out-String).Trim() }
    $review.crash_artifacts_present = ($review.files.Count -gt 0)
    $review.crash_checks_passed = ($review.confined -and !$review.crash_artifacts_present -and $review.errors.Count -eq 0)
    return $review
}
function Save-HashIndex {
    $names = @('events.jsonl','performance.jsonl','room-performance.jsonl','status.json','player.log')
    $missing = @($names | Where-Object { !(Test-Path -LiteralPath (Join-Path $attempt $_) -PathType Leaf) })
    $index = @(Get-ChildItem -LiteralPath $attempt,$identity -File | ForEach-Object {
        @{ path=[IO.Path]::GetRelativePath($kit,$_.FullName).Replace('\','/'); bytes=$_.Length; sha256=(Sha $_.FullName) }
    })
    # Only inventory-confirmed confined crash files may enter the index; never follow links.
    if ($null -ne $crashReview) {
        foreach ($row in $crashReview.files) { $index += @{ path=('attempt/' + $row.path); bytes=$row.bytes; sha256=$row.sha256 } }
    }
    Save-New (Join-Path $kit 'hash-index.json') (@{ schema_version=1; files=$index; missing_attempt_files=$missing } | ConvertTo-Json -Depth 6)
}
function Verify-Player([string]$label) {
    $output = & $python $identityTool verify --player-dir $player --receipt-sha256 $receiptHash --source-archive $kitSource --commit $commit 2>&1
    $code = $LASTEXITCODE
    Save-New (Join-Path $attempt ($label + '.txt')) ($output | Out-String)
    return ($code -eq 0)
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
$errors = [Collections.Generic.List[string]]::new()
$process = $null; $power = $null; $pidOwned = $null; $exitCode = $null
$startedUtc = $null; $endedUtc = $null; $afterValid = $false; $logReview = $null
$integrityReview = $null; $childIntegrity = $null; $exitEventReview = $null; $crashReview = $null
$start = $null; $exe = Join-Path $player 'ShadowsOfTheForsaken.exe'
$playerLog = Join-Path $attempt 'player.log'
$crashFolder = Join-Path $attempt 'crashes'
$kitSource = Join-Path $identity 'source.zip'
$recorderSha = Sha $PSCommandPath
$identityToolSource = $identityTool
$identityToolSha = Sha $identityToolSource
$identityTool = Join-Path $identity 'player_build_manifest.py'
$repoHead = $null
try {
    $repoHead = (& git -C $repo rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot record recorder repository HEAD.' }
    Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $identity 'recorder.ps1')
    if ((Sha (Join-Path $identity 'recorder.ps1')) -ne $recorderSha) { throw 'Recorder copy hash mismatch.' }
    Copy-Item -LiteralPath $identityToolSource -Destination $identityTool
    if ((Sha $identityTool) -ne $identityToolSha) { throw 'Identity tool copy hash mismatch.' }
    Initialize-RecorderNative
    try { $integrityReview = Get-IntegrityReview ([ForsakenRecorder.Native]::IntegrityRid()) }
    catch {
        Save-New (Join-Path $attempt 'integrity.json') (@{ pid=$PID; label='unknown'; normal_integrity=$false; error=($_ | Out-String); captured_utc=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json)
        throw
    }
    Save-New (Join-Path $attempt 'integrity.json') ($integrityReview | ConvertTo-Json)
    if (!$integrityReview.normal_integrity) { throw 'Recorder integrity is below medium; launch refused. Use an ordinary interactive Windows terminal.' }
    Expand-Archive -LiteralPath $zip -DestinationPath $player
    $receipt = Join-Path $player 'BUILD-INFO.json'
    if ((Sha $receipt) -ne $receiptHash) { throw 'Extracted receipt hash mismatch.' }
    Copy-Item -LiteralPath $receipt -Destination (Join-Path $identity 'BUILD-INFO.json')
    Copy-Item -LiteralPath $source -Destination $kitSource
    if (!(Verify-Player 'verify-before')) { throw 'Player identity refused before launch.' }
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $exe; $start.WorkingDirectory = $player; $start.UseShellExecute = $false
    $start.Environment['SHADOWS_PLAYER_EVIDENCE_DIR'] = $attempt
    New-Item -ItemType Directory -Path $crashFolder | Out-Null
    # Capture requested crash output separately; only files actually produced are evidence.
    foreach ($argument in @('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-force-d3d11','-logFile',$playerLog,'-crash-report-folder',$crashFolder)) { $start.ArgumentList.Add($argument) }
    $power = [ForsakenRecorder.PowerLease]::new()
    if (!$power.Acquired) { throw $power.Error }
    $startedUtc = [DateTime]::UtcNow.ToString('o')
    $process = [Diagnostics.Process]::Start($start)
    $pidOwned = $process.Id
    try {
        $childIntegrity = Get-IntegrityReview ([ForsakenRecorder.Native]::IntegrityRid($process.Handle))
        $childIntegrity.pid = $pidOwned
    } catch { $childIntegrity = @{ pid=$pidOwned; label='unknown'; normal_integrity=$false; error=($_ | Out-String).Trim(); captured_utc=[DateTime]::UtcNow.ToString('o') } }
    Save-New (Join-Path $attempt 'child-integrity.json') ($childIntegrity | ConvertTo-Json)
    if (!$childIntegrity.normal_integrity) { throw 'Child integrity is unknown or below medium; this attempt cannot qualify. Close the owned player normally.' }
    Write-Host "Evidence kit: $kit; player PID: $pidOwned"
    Write-Host 'Play normally. After completion, close with Alt+F4; do not press R.'
    Save-New (Join-Path $attempt 'launch-started.json') (@{
        executable=$exe; arguments=@($start.ArgumentList); pid=$pidOwned; started_utc=$startedUtc
        receipt_sha256=$receiptHash; source_commit=$commit; recorder_sha256=$recorderSha; recorder_repo_head=$repoHead; identity_tool_sha256=$identityToolSha
        input_origin=$InputOrigin; route=$Route; route_familiarity=$RouteFamiliarity
    } | ConvertTo-Json -Depth 5)
    $process.WaitForExit() # No input, focus changes or forced child cleanup.
} catch { $errors.Add(($_ | Out-String).Trim()) }
finally {
    # Even if saving the launch receipt failed, retain the owned process and wait for its normal closure.
    try {
        if ($null -ne $process) {
            if (!$process.HasExited) { Write-Warning 'Recorder error; close the owned player normally to finish preserving evidence.'; $process.WaitForExit() }
            $exitCode = $process.ExitCode
            $endedUtc = [DateTime]::UtcNow.ToString('o')
        }
    } catch { $errors.Add(($_ | Out-String).Trim()) }
    finally {
        if ($null -ne $process) { $process.Dispose() }
        if ($null -ne $power) {
            $power.Dispose()
            Save-New (Join-Path $attempt 'power-request.json') (@{
                flags=$power.Flags; previous_flags=$power.PreviousFlags; owner_thread_id=$power.OwnerThreadId
                requested_utc=$power.RequestedUtc; released_utc=$power.ReleasedUtc; acquired=$power.Acquired
                restored=$power.Restored; error=$power.Error; scope='Dedicated thread; this player lifetime only; no OS plan changes'
            } | ConvertTo-Json)
            if (!$power.Acquired -or !$power.Restored) { $errors.Add('Temporary power request acquisition/restoration failed: ' + $power.Error) }
        }
    }
    if ($null -ne $pidOwned) {
        try { $afterValid = Verify-Player 'verify-after' } catch { $errors.Add(($_ | Out-String).Trim()) }
        $crashReview = Get-CrashFolderReview $attempt
        Save-New (Join-Path $attempt 'crash-review.json') ($crashReview | ConvertTo-Json -Depth 7)
        if (!$crashReview.crash_checks_passed) { $errors.Add('Crash artifacts or an unconfined/unreadable crash inventory reject this attempt even with exit code 0.') }
        if (Test-Path -LiteralPath $playerLog -PathType Leaf) {
            $logReview = Get-PlayerLogReview (Get-Content -LiteralPath $playerLog -Raw) ([int]$exitCode)
            $logReview.player_log_sha256 = Sha $playerLog
            Save-New (Join-Path $attempt 'log-review.json') ($logReview | ConvertTo-Json -Depth 8)
            if (!$logReview.runtime_log_checks_passed) { $errors.Add('Player exit/log checks failed; FMOD/native fatal evidence remains authoritative even with exit code 0.') }
        } else { $errors.Add('Player log is missing.') }
        Save-New (Join-Path $attempt 'launch.json') (@{
            executable=$exe; arguments=@($start.ArgumentList); pid=$pidOwned; started_utc=$startedUtc; ended_utc=$endedUtc; exit_code=$exitCode
            receipt_sha256=$receiptHash; source_commit=$commit; recorder_sha256=$recorderSha; recorder_repo_head=$repoHead; identity_tool_sha256=$identityToolSha
            crash_report_folder_requested=$crashFolder
            crash_checks_passed=$crashReview.crash_checks_passed
            player_identity_valid_after=$afterValid; runtime_log_checks_passed=($null -ne $logReview -and $logReview.runtime_log_checks_passed)
        } | ConvertTo-Json -Depth 5)
        if (!$afterValid) { $errors.Add('Player identity refused after exit.') }
    }
    $names = @('events.jsonl','performance.jsonl','room-performance.jsonl','status.json','player.log')
    $missing = @($names | Where-Object { !(Test-Path -LiteralPath (Join-Path $attempt $_) -PathType Leaf) })
    if ($missing.Count) { $errors.Add('Missing attempt files: ' + ($missing -join ', ')) }
    if ((Test-Path -LiteralPath (Join-Path $attempt 'events.jsonl') -PathType Leaf) -and (Test-Path -LiteralPath (Join-Path $attempt 'status.json') -PathType Leaf)) {
        $exitEventReview = Get-ExitEventReview (Get-Content -LiteralPath (Join-Path $attempt 'events.jsonl') -Raw) (Get-Content -LiteralPath (Join-Path $attempt 'status.json') -Raw)
        Save-New (Join-Path $attempt 'exit-event-review.json') ($exitEventReview | ConvertTo-Json -Depth 5)
        if (!$exitEventReview.normal_exit_event_observed) { $errors.Add('Final observer normal-exit event missing, malformed or from a different final session.') }
    }
    Save-New (Join-Path $attempt 'recorder-result.json') (@{
        schema_version=1; recorded_utc=[DateTime]::UtcNow.ToString('o'); recorder_sha256=$recorderSha; recorder_repo_head=$repoHead; identity_tool_sha256=$identityToolSha
        source_commit=$commit; player_started=($null -ne $pidOwned); runtime_checks_passed=($errors.Count -eq 0)
        acceptance_claimed=$false; errors=$errors.ToArray(); operator_declaration_pending_at_lifecycle_capture=$true
    } | ConvertTo-Json -Depth 6)
    if ($errors.Count) { Save-HashIndex }
}
if ($errors.Count) { throw "Attempt needs review; preserve the kit at $kit. $($errors -join ' ')" }
try {
    if ([string]::IsNullOrWhiteSpace($RouteFamiliarity)) {
        $RouteFamiliarity = Read-RequiredChoice 'Actual route familiarity: first_attempt / repeat / known_route' @('first_attempt','repeat','known_route')
    }
    $interruption = Read-RequiredChoice 'Interruptions during this attempt: none-observed / observed / unknown' @('none-observed','observed','unknown')
    $interruptionsObserved = switch ($interruption) { 'none-observed' { $false }; 'observed' { $true }; 'unknown' { $null } }
    Save-New (Join-Path $attempt 'operator-declaration.json') (@{
        input_origin=$InputOrigin; route_familiarity=$RouteFamiliarity; route=$Route
        interruptions=$interruption; recorded_utc=[DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json)
    $status = Get-Content -LiteralPath (Join-Path $attempt 'status.json') -Raw | ConvertFrom-Json
    # Check again after operator prompts in case the separate crash handler produced delayed files.
    $crashReview = Get-CrashFolderReview $attempt
    Save-New (Join-Path $attempt 'crash-review-before-manifest.json') ($crashReview | ConvertTo-Json -Depth 7)
    if (!$crashReview.crash_checks_passed) { throw 'Crash artifacts/inventory reject run.json; preserve the kit for review.' }
    $references = @{}
    foreach ($name in @('events','performance','status')) {
        $file = if ($name -eq 'status') { 'status.json' } else { $name + '.jsonl' }
        $references[$name] = @{ path=('attempt/' + $file); sha256=(Sha (Join-Path $attempt $file)) }
    }
    Save-New (Join-Path $kit 'run.json') (@{
        schema_version=1; run_id=$runId; session_id=$status.sessionId; revision=$commit
        input_origin=$InputOrigin; route_familiarity=$RouteFamiliarity; route=$Route; interruptions_observed=$interruptionsObserved
        build_receipt=@{ path='identity/BUILD-INFO.json'; sha256=(Sha (Join-Path $identity 'BUILD-INFO.json')) }
        source_archive=@{ path='identity/source.zip'; sha256=(Sha $kitSource) }; files=$references
    } | ConvertTo-Json -Depth 8)
} finally { Save-HashIndex }
Write-Host "Preserved $kit. run.json is a declared review input, not an acceptance result."
