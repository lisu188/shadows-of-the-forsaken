"""Exercise actual recorder functions without launching a player or changing power state.

PowerShell 7 is required for these Windows handoff regressions. Hosts without pwsh
report them skipped rather than replacing the implementation with a Python mirror.
"""
import base64
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
RECORDER = ROOT / 'tools' / 'Record-ForsakenPlaytest.ps1'
PWSH = shutil.which('pwsh')
if not PWSH:
    bundled = Path.home() / '.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'
    if bundled.is_file():
        PWSH = str(bundled)


def ps_literal(value):
    return "'" + str(value).replace("'", "''") + "'"


@unittest.skipUnless(PWSH, 'PowerShell 7 unavailable; actual recorder regressions unexecuted')
class PlaytestRecorderTests(unittest.TestCase):
    def invoke(self, body):
        # Parse the delivered script and import definitions only. Its top-level
        # verification, native calls and player launch never execute in this test.
        harness = f"""$ErrorActionPreference='Stop'
$tokens=$null; $parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile({ps_literal(RECORDER)},[ref]$tokens,[ref]$parseErrors)
if ($parseErrors.Count) {{ throw ($parseErrors | Out-String) }}
foreach($function in $ast.FindAll({{param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]}},$false)) {{
    Invoke-Expression $function.Extent.Text
}}
{body}
"""
        process = subprocess.run([PWSH, '-NoProfile', '-NonInteractive', '-Command', harness],
                                 capture_output=True, text=True, check=False, timeout=30)
        self.assertEqual(process.returncode, 0, process.stdout + process.stderr)
        return json.loads(process.stdout)

    def review(self, text, exit_code=0):
        encoded = base64.b64encode(text.encode()).decode()
        return self.invoke(f"$text=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encoded}')); "
                           f"Get-PlayerLogReview $text {exit_code} | ConvertTo-Json -Depth 8 -Compress")

    def test_complete_script_parses_without_executing_top_level(self):
        self.assertTrue(self.invoke("@{parsed=$true} | ConvertTo-Json -Compress")['parsed'])

    def test_exit_zero_cannot_hide_native_crash_excerpt(self):
        result = self.review('Cleanup current backend.\nNative Crash Reporting\n'
                             'Got a UNKNOWN while executing native code.\n'
                             'a fatal error in the mono runtime or one of the native libraries\n')
        self.assertEqual(result['exit_code'], 0)
        self.assertFalse(result['runtime_log_checks_passed'])
        self.assertEqual([row['line'] for row in result['native_fatal_markers']], [2, 3, 4])

    def test_each_audio_failure_rejects_exit_zero(self):
        for text in ['Default audio device was changed, but the audio system failed to initialize it.',
                     'FMOD failed to initialize the output device.: "Error initializing output device. " (60)',
                     'FMOD failed to initialize any audio devices, running on emulated software output with no sound.',
                     'FMOD initialized on nosound output.']:
            with self.subTest(text=text):
                result = self.review(text)
                self.assertFalse(result['runtime_log_checks_passed'])
                self.assertTrue(result['audio_failure_markers'])

    def test_optional_diagnostics_do_not_become_fatal_audio_markers(self):
        result = self.review('Failed to load plugin optional-vendor.dll\n'
                             '[Licensing] Error: token refresh deferred\n'
                             'DepthOfField pass is not supported\n'
                             'FMOD initialized successfully\n'
                             'GfxDevice: creating device; kGfxThreadingModeClientWorkerJobs\n'
                             'CodeReloadManager: destroyed\n')
        self.assertTrue(result['runtime_log_checks_passed'])
        self.assertFalse(result['audio_failure_markers'])
        self.assertFalse(result['native_fatal_markers'])
        self.assertEqual(len(result['diagnostic_candidates']), 2)
        self.assertEqual(result['graphics_threading_observations'][0]['mode'],
                         'kGfxThreadingModeClientWorkerJobs')

    def test_nonzero_exit_rejects_an_otherwise_normal_log(self):
        self.assertFalse(self.review('Normal player shutdown', 17)['runtime_log_checks_passed'])

    def test_truncated_native_crash_line_also_rejects_exit_zero(self):
        self.assertFalse(self.review('Got a UNKNOWN while executing native code.')['runtime_log_checks_passed'])

    def test_dump_only_failure_rejects_clean_exit_zero_and_retains_confined_hash(self):
        with tempfile.TemporaryDirectory() as temp:
            attempt = Path(temp) / 'attempt'
            crashes = attempt / 'crashes'
            crashes.mkdir(parents=True)
            empty = self.invoke(f'Get-CrashFolderReview {ps_literal(attempt)} | ConvertTo-Json -Depth 7 -Compress')
            self.assertTrue(empty['crash_checks_passed'])
            dump = crashes / 'Crash_1' / 'crash.dmp'
            dump.parent.mkdir()
            dump.write_bytes(b'synthetic native crash dump')
            result = self.invoke(f"$log=Get-PlayerLogReview 'Normal shutdown' 0; "
                                 f"$crash=Get-CrashFolderReview {ps_literal(attempt)}; "
                                 "@{runtime_log_checks_passed=$log.runtime_log_checks_passed; crash=$crash; "
                                 "qualification=($log.runtime_log_checks_passed -and $crash.crash_checks_passed)} | ConvertTo-Json -Depth 8 -Compress")
            self.assertTrue(result['runtime_log_checks_passed'])
            self.assertFalse(result['qualification'])
            self.assertTrue(result['crash']['confined'])
            self.assertTrue(result['crash']['crash_artifacts_present'])
            self.assertEqual(result['crash']['files'], [dict(path='crashes/Crash_1/crash.dmp',
                             bytes=dump.stat().st_size, sha256=hashlib.sha256(dump.read_bytes()).hexdigest())])

    def test_final_normal_exit_must_match_final_session_and_parse_completely(self):
        status = json.dumps(dict(sessionId='final-session', phase='Running', room='Puzzle'))
        for events, expected in [
            ([dict(kind='started', sessionId='final-session'), dict(kind='normal-exit', sessionId='final-session')], True),
            ([dict(kind='normal-exit', sessionId='old-session')], False),
            ([dict(kind='normal-exit', sessionId='final-session'), dict(kind='changed', sessionId='final-session')], False),
            ([], False),
        ]:
            with self.subTest(events=events):
                raw = '\n'.join(json.dumps(row) for row in events)
                result = self.invoke(f'Get-ExitEventReview {ps_literal(raw)} {ps_literal(status)} | ConvertTo-Json -Compress')
                self.assertEqual(result['normal_exit_event_observed'], expected)
        result = self.invoke(f"Get-ExitEventReview '{{broken' {ps_literal(status)} | ConvertTo-Json -Compress")
        self.assertFalse(result['normal_exit_event_observed'])
        self.assertIn('error', result)

    def test_integrity_threshold_rejects_low_and_accepts_medium(self):
        results = self.invoke('@(0,4096,8191,8192,12288 | ForEach-Object { Get-IntegrityReview $_ }) | ConvertTo-Json -Compress')
        self.assertEqual([row['normal_integrity'] for row in results], [False, False, False, True, True])
        self.assertEqual([row['label'] for row in results], ['untrusted', 'low', 'low', 'medium', 'high'])

    def test_missing_operator_input_refuses_manifest_instead_of_looping(self):
        result = self.invoke("function Read-Host { return $null }; $refused=$false; "
                             "try { Read-RequiredChoice 'test' @('observed','unknown') | Out-Null } "
                             "catch { $refused=$_.Exception.Message -like 'No explicit operator declaration*' }; "
                             "@{refused=$refused} | ConvertTo-Json -Compress")
        self.assertTrue(result['refused'])

    def test_power_lease_restores_previous_flags_on_exception_on_same_thread(self):
        result = self.invoke("""Initialize-RecorderNative
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Threading;
public static class SyntheticLeaseTest {
    public static List<uint> Flags = new List<uint>();
    public static List<int> Threads = new List<int>();
    public static uint SetState(uint value) {
        Flags.Add(value); Threads.Add(Thread.CurrentThread.ManagedThreadId);
        return Flags.Count == 1 ? 0x80000001u : 0x80000003u;
    }
    public static uint ThreadId() { return 123u; }
}
'@
$setter=[SyntheticLeaseTest].GetMethod('SetState').CreateDelegate([Func[uint32,uint32]])
$threadId=[SyntheticLeaseTest].GetMethod('ThreadId').CreateDelegate([Func[uint32]])
$lease=[ForsakenRecorder.PowerLease]::new($setter,$threadId)
try { throw 'synthetic failure' } catch { } finally { $lease.Dispose(); $lease.Dispose() }
@{ flags=[SyntheticLeaseTest]::Flags.ToArray(); same_thread=([SyntheticLeaseTest]::Threads[0] -eq [SyntheticLeaseTest]::Threads[1]); restored=$lease.Restored; owner=$lease.OwnerThreadId } | ConvertTo-Json -Compress
""")
        self.assertEqual(result['flags'], [0x80000003, 0x80000001])
        self.assertTrue(result['same_thread'])
        self.assertTrue(result['restored'])
        self.assertEqual(result['owner'], 123)

    def test_save_new_cannot_replace_existing_evidence(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / 'retained.txt'
            result = self.invoke(f"$path={ps_literal(path)}; Save-New $path 'original'; "
                                 "$refused=$false; try { Save-New $path 'replacement' } catch { $refused=$true }; "
                                 "@{refused=$refused; retained=[IO.File]::ReadAllText($path)} | ConvertTo-Json -Compress")
        self.assertTrue(result['refused'])
        self.assertEqual(result['retained'], 'original')

    def test_failed_power_acquisition_and_restoration_remain_failures(self):
        result = self.invoke("""Initialize-RecorderNative
Add-Type -TypeDefinition @'
using System;
public static class FailingLeaseTest {
    public static int Calls;
    public static bool FailAcquire;
    public static uint SetState(uint flags) {
        Calls++;
        return FailAcquire || Calls > 1 ? 0u : 0x80000000u;
    }
    public static uint ThreadId() { return 7u; }
}
'@
$setter=[FailingLeaseTest].GetMethod('SetState').CreateDelegate([Func[uint32,uint32]])
$threadId=[FailingLeaseTest].GetMethod('ThreadId').CreateDelegate([Func[uint32]])
[FailingLeaseTest]::FailAcquire=$true
$first=[ForsakenRecorder.PowerLease]::new($setter,$threadId); $first.Dispose()
$acquire=@{ acquired=$first.Acquired; restored=$first.Restored; error=$first.Error; calls=[FailingLeaseTest]::Calls }
[FailingLeaseTest]::FailAcquire=$false; [FailingLeaseTest]::Calls=0
$second=[ForsakenRecorder.PowerLease]::new($setter,$threadId); $second.Dispose()
@{ acquisition=$acquire; restoration=@{ acquired=$second.Acquired; restored=$second.Restored; error=$second.Error; calls=[FailingLeaseTest]::Calls } } | ConvertTo-Json -Depth 6 -Compress
""")
        self.assertFalse(result['acquisition']['acquired'])
        self.assertFalse(result['acquisition']['restored'])
        self.assertEqual(result['acquisition']['calls'], 1)
        self.assertIn('acquisition failed', result['acquisition']['error'])
        self.assertTrue(result['restoration']['acquired'])
        self.assertFalse(result['restoration']['restored'])
        self.assertEqual(result['restoration']['calls'], 2)
        self.assertIn('restoration failed', result['restoration']['error'])


if __name__ == '__main__':
    unittest.main()
