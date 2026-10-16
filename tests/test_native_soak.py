"""Explicit hour-profile regressions. Synthetic clocks/Win32 fakes are not a soak."""
import copy
import datetime as dt
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("native_soak_validation", ROOT / "tools/native_player_validation.py")
native = importlib.util.module_from_spec(spec); spec.loader.exec_module(native)
spec = importlib.util.spec_from_file_location("native_soak_fixture", ROOT / "tests/test_native_player_validation.py")
fixtures = importlib.util.module_from_spec(spec); spec.loader.exec_module(fixtures)


def valid_record():
    value = fixtures.record()
    parent = dict(pid=42, requested_parent=900, process_live_before=True, process_live_after=True,
                  matching_parent_observed=True, child_windows=[dict(owner_pid=42, window=100,
                  **{"class": "UnityWndClass"}, style=0x40000000, actual_parent=900)])
    value.update(schema_version=2, profile=native.SOAK_PROFILE, pid=42, observation_seconds_required=3600,
                 timeout_seconds=3720, seconds=3606, soak_state_observed=True, observed_snapshot_count=14401,
                 soak_limits=native.soak_limits(),
                 matching_fresh_snapshot_count=14401, ordinary_release_identity_verified=True,
                 player_mode_requested="embedded", parent_mode_requested="owned", graphics_mode_requested="legacy",
                 hidden_parent=dict(window=900, destroyed=True), parent_relation_verified=True,
                 parent_relation_observation=parent, parent_relation_attempts=[parent],
                 no_overlap_preflight=[dict(processes_examined=200, matching_processes=[],snapshot_close_verified=True) for _ in range(2)],
                 last_observed_state_checks=native.state_check(fixtures.state()),
                 soak_observation=dict(profile=native.SOAK_PROFILE, required_seconds=3600,
                     first_published_utc="2026-10-16T00:00:00.0000000Z", last_published_utc="2026-10-16T01:00:00.0000000Z",
                     published_span_100ns=36000000000, published_span_seconds=3600,
                     monotonic_span_seconds=3600, matching_fresh_snapshot_count=14401),
                 soak_budget=dict(checks=14403, volumes=["synthetic-volume"], minimum_free_bytes={"synthetic-volume":native.SOAK_RESERVE_BYTES},
                     maximum_evidence_bytes=100000, maximum_file_count=12, maximum_log_bytes={"player.log":1000},
                     samples=[dict(seconds=seconds,free_bytes={"synthetic-volume":native.SOAK_RESERVE_BYTES},
                         evidence_bytes=100000,file_count=12,log_bytes={"player.log":1000}) for seconds in range(0,3601,15)]),
                 graphics_threading_observations=[dict(mode="kGfxThreadingModeClientWorkerJobs")],
                 crash_reports=[], soak_logs_bounded=True,
                 log_receipts=[dict(path=name, bytes=100, sha256="a" * 64) for name in
                               ("player.log", "launcher-stdout.log", "launcher-stderr.log")])
    value["closure"].update(pid=42, other_processes_touched=False, wm_close=[dict(owner_pid=42, window=100, posted=True)])
    value.pop("startup_observation"); value.pop("startup_state_observed")
    return value


class NativeSoakTests(unittest.TestCase):
    def arguments(self, *extra):
        return native.parse_arguments(["--player", "ShadowsOfTheForsaken.exe", "--output", "evidence", "--receipt-sha256", "a" * 64, *extra])

    def test_startup_defaults_caps_and_original_final_verifier_remain_effective(self):
        args = self.arguments()
        self.assertEqual((args.profile, args.observe_seconds, args.timeout_seconds), ("startup", 3, 45))
        self.assertEqual((args.player_mode, args.parent_mode, args.graphics_mode), ("batch", "delayed", "default"))
        self.assertEqual(self.arguments("--observe-seconds", "120", "--timeout-seconds", "180").observe_seconds, 120)
        for extra in [("--observe-seconds", "121"), ("--timeout-seconds", "181"), ("--observe-seconds", "nan"),
                      ("--timeout-seconds", "inf"), ("--observe-seconds", "3600", "--timeout-seconds", "3720")]:
            with self.subTest(extra=extra), self.assertRaises(ValueError): self.arguments(*extra)
        self.assertTrue(native.finish_record(fixtures.record()))
        value = fixtures.record(); value["observation_seconds_required"] = 3600
        self.assertFalse(native.finish_record(value))

    def test_fixed_profile_has_no_duration_or_mode_override_escape(self):
        args = self.arguments("--profile", native.SOAK_PROFILE)
        self.assertEqual((args.observe_seconds, args.timeout_seconds), (3600, 3720))
        self.assertEqual((args.player_mode, args.parent_mode, args.graphics_mode), ("embedded", "owned", "legacy"))
        for extra in [("--observe-seconds", "3600"), ("--observe-seconds", "3"), ("--timeout-seconds", "3720"),
                      ("--timeout-seconds", "inf"), ("--player-mode", "batch"), ("--parent-mode", "delayed"),
                      ("--graphics-mode", "default")]:
            with self.subTest(extra=extra), self.assertRaises(ValueError): self.arguments("--profile", native.SOAK_PROFILE, *extra)

    def test_fixed_observer_requires_each_full_clock_and_exact_seventh_digit(self):
        for kind in ("published-short", "poll-short", "exact"):
            observer = native.SoakObservation()
            for index in range(3600):
                self.assertFalse(observer.accept(fixtures.state(index + 1, index), index))
            final = fixtures.state(3601, 3600)
            if kind == "published-short": final["utc"] = "2026-10-16T00:59:59.9999999Z"
            polling = 3599.9999999 if kind == "poll-short" else 3600
            self.assertEqual(observer.accept(final, polling), kind == "exact")
            self.assertEqual(observer.summary()["required_seconds"], 3600)

    def test_soak_retains_existing_freshness_state_and_session_guards(self):
        observer = native.SoakObservation(); observer.accept(fixtures.state(1, 0), 0)
        for changed, now in [(fixtures.state(2, 1), 2.1), (fixtures.state(0, 1), 1),
                             ({**fixtures.state(2, 1), "objectiveFlags":1}, 1),
                             ({**fixtures.state(2, 1), "sessionId":"changed"}, 1)]:
            with self.subTest(changed=changed), self.assertRaises(ValueError): observer.accept(changed, now)
        observer=native.SoakObservation()
        for index in range(native.SOAK_MAX_SNAPSHOTS):
            self.assertFalse(observer.accept(fixtures.state(index+1,index*.01),index*.01))
        with self.assertRaisesRegex(ValueError,"snapshot history"):
            observer.accept(fixtures.state(native.SOAK_MAX_SNAPSHOTS+1,164),164)

    def test_independent_final_verifier_recomputes_source_clocks_and_counts(self):
        self.assertTrue(native.finish_soak_record(valid_record()))
        changes = [dict(last_published_utc="2026-10-16T00:59:59.9999999Z"), dict(monotonic_span_seconds=3599.9),
                   dict(published_span_100ns=1), dict(published_span_seconds=float("nan")),
                   dict(matching_fresh_snapshot_count=True), dict(matching_fresh_snapshot_count=16385)]
        for change in changes:
            value=valid_record(); value["soak_observation"].update(change)
            with self.subTest(change=change): self.assertFalse(native.finish_soak_record(value))
        for change in [dict(schema_version=1), dict(profile="startup"), dict(observation_seconds_required=120),
                       dict(timeout_seconds=3721), dict(seconds=3720), dict(seconds=3720.001), dict(seconds=float("inf")),
                       dict(observed_snapshot_count=16385)]:
            with self.subTest(change=change): self.assertFalse(native.finish_soak_record({**valid_record(), **change}))
        for key in native.soak_limits():
            value=valid_record();value["soak_limits"][key]+=1
            with self.subTest(forged_limit=key):self.assertFalse(native.finish_soak_record(value))

    def test_each_existing_lifecycle_failure_still_rejects_the_hour_profile(self):
        changes=[dict(soak_state_observed=False), dict(build_identity_unchanged=False), dict(ordinary_release_identity_verified=False),
                 dict(integrity_before_launch={}), dict(child_integrity=dict(normal_integrity=False)), dict(power_request=dict(restored=False)),
                 dict(foreground_or_visible_window_violation=True), dict(hidden_parent=dict(window=900, destroyed=False)),
                 dict(native_fatal_markers=[dict(text="Native Crash Reporting")]), dict(audio_failure_markers=[dict(text="FMOD60")]),
                 dict(crash_reports=[dict(path="crash.dmp")]), dict(normal_exit_event_observed=False),
                 dict(graphics_threading_observations=[]), dict(log_receipts=[]), dict(parent_relation_verified=False),
                 dict(no_overlap_preflight=[dict(processes_examined=1, matching_processes=[dict(pid=9, name="Unity.exe")])]),
                 dict(errors=["prior failure"])]
        for change in changes:
            with self.subTest(change=change): self.assertFalse(native.finish_soak_record({**valid_record(), **change}))
        for change in (dict(mode="already_exited"), dict(forced=True), dict(exit_code=1), dict(other_processes_touched=True),
                       dict(pid=99), dict(wm_close=[]), dict(wm_close=[dict(owner_pid=99, posted=True)])):
            value=valid_record();value["closure"].update(change)
            with self.subTest(change=change):self.assertFalse(native.finish_soak_record(value))

    def test_active_parent_query_requires_owned_direct_child_and_liveness_on_both_sides(self):
        user=mock.Mock();user.GetForegroundWindow.return_value=0;user.GetParent.return_value=900
        parent=mock.Mock(handle=900)
        good=dict(window=100,owner_pid=42,style=0x40000000,**{"class":"UnityWndClass"})
        for changed,polls,expected in [({},[None,None],True),({},[None,0],False),
                                      (dict(style=0),[None,None],False),(dict(owner_pid=99),[None,None],False),
                                      ({"class":"Other"},[None,None],False)]:
            process=mock.Mock(pid=42);process.poll.side_effect=polls
            with mock.patch.object(native,"own_windows",return_value=[100]),mock.patch.object(native,"describe_windows",return_value=[{**good,**changed}]):
                result=native.observe_live_parent(process,user,None,parent)
            self.assertIs(result["matching_parent_observed"],expected)
        process=mock.Mock(pid=42);process.poll.return_value=0
        user.GetParent.reset_mock()
        self.assertIsNone(native.observe_live_parent(process,user,None,parent));user.GetParent.assert_not_called()

    def test_exact_process_inventory_is_read_only_bounded_and_closes_snapshot(self):
        kernel=mock.Mock();kernel.CreateToolhelp32Snapshot.return_value=900;kernel.GetLastError.return_value=18
        inventory=iter([(1,"UnityShaderCompiler.exe"),(2,"UNITY.exe"),(3,"PlayerWithTests.exe"),(4,"ShadowsOfTheForsaken.exe")])
        def next_entry(handle,target):
            try:pid,name=next(inventory)
            except StopIteration:return False
            target._obj.th32ProcessID=pid;target._obj.szExeFile=name;return True
        kernel.Process32FirstW.side_effect=next_entry;kernel.Process32NextW.side_effect=next_entry
        result=native.validation_processes(kernel)
        self.assertEqual([item["pid"] for item in result["matching_processes"]],[2,3,4])
        kernel.CloseHandle.assert_called_once_with(900)
        kernel.TerminateProcess.assert_not_called()
        overflow=mock.Mock();overflow.CreateToolhelp32Snapshot.return_value=901
        overflow.Process32FirstW.return_value=True;overflow.Process32NextW.return_value=True
        with self.assertRaisesRegex(ValueError,"inventory exceeds"):native.validation_processes(overflow)
        overflow.CloseHandle.assert_called_once_with(901)
        with mock.patch.object(native,"validation_processes",return_value=result),self.assertRaisesRegex(ValueError,"Another Unity"):
            native.no_validation_overlap(kernel)
        args=self.arguments("--profile",native.SOAK_PROFILE)
        with mock.patch.object(native,"parse_arguments",return_value=args),mock.patch.object(native.os,"name","nt"), \
             mock.patch.object(native.ctypes,"WinDLL",return_value=kernel,create=True), \
             mock.patch.object(native,"validation_processes",return_value=result), \
             mock.patch.object(native.manifest,"verify") as identity,mock.patch.object(native.subprocess,"Popen") as launch:
            with self.assertRaisesRegex(ValueError,"Another Unity"):native.main()
        identity.assert_not_called();launch.assert_not_called()

    def test_disk_growth_log_and_resource_history_are_bounded_without_creating_large_evidence(self):
        with tempfile.TemporaryDirectory() as temp:
            output=Path(temp);(output/"player.log").write_bytes(b"normal log")
            budget=native.SoakBudget(output,["synthetic"])
            with mock.patch.object(native.shutil,"disk_usage",return_value=mock.Mock(free=native.SOAK_RESERVE_BYTES)):
                budget.check(0);budget.check(14);budget.check(15)
            self.assertEqual(len(budget.samples),2);self.assertEqual(budget.checks,3)
            with mock.patch.object(native.shutil,"disk_usage",return_value=mock.Mock(free=native.SOAK_RESERVE_BYTES-1)):
                with self.assertRaisesRegex(ValueError,"disk reserve"):budget.check(16)
            with mock.patch.object(native.shutil,"disk_usage",return_value=mock.Mock(free=native.SOAK_RESERVE_BYTES)):
                with self.assertRaisesRegex(ValueError,"deadline"):budget.check(3720)
                original=Path.lstat
                def size(path,*args,**kwargs):
                    if path==output/"player.log":return mock.Mock(st_mode=0o100600,st_size=native.SOAK_LOG_BYTES+1,st_file_attributes=0)
                    return original(path,*args,**kwargs)
                with mock.patch.object(Path,"lstat",size),mock.patch.object(Path,"is_symlink",return_value=False):
                    with self.assertRaisesRegex(ValueError,"log/events"):budget.check(16)
                def growth(path,*args,**kwargs):
                    if path==output/"player.log":return mock.Mock(st_mode=0o100600,st_size=native.SOAK_GROWTH_BYTES+1,st_file_attributes=0)
                    return original(path,*args,**kwargs)
                with mock.patch.object(Path,"lstat",growth),mock.patch.object(Path,"is_symlink",return_value=False):
                    with self.assertRaisesRegex(ValueError,"growth bound"):budget.check(16)
                budget.samples=[{}]*256
                with self.assertRaisesRegex(ValueError,"sample history"):budget.check(30)
            with mock.patch.object(native,"shared_read",return_value=b"normal\n"*16385):
                with self.assertRaisesRegex(ValueError,"log line history"):native.scan_soak_player_logs(None,output)
            for change in (dict(maximum_evidence_bytes=native.SOAK_GROWTH_BYTES+1),dict(samples=[{}]*257),
                           dict(minimum_free_bytes={"synthetic-volume":0}),dict(maximum_log_bytes={"player.log":native.SOAK_LOG_BYTES+1})):
                value=valid_record();value["soak_budget"].update(change)
                with self.subTest(change=change):self.assertFalse(native.finish_soak_record(value))
            for change in (dict(seconds=0),dict(free_bytes={"synthetic-volume":0}),dict(evidence_bytes=-1),dict(log_bytes={"player.log":9999})):
                value=valid_record();value["soak_budget"]["samples"][1].update(change)
                with self.subTest(forged_resource_sample=change):self.assertFalse(native.finish_soak_record(value))

    def test_shared_owned_cleanup_never_converts_forced_or_foreign_close_to_a_soak_pass(self):
        value=valid_record();process=mock.Mock(pid=42);process.poll.side_effect=[None,None,None,0]
        process.returncode=0
        user=mock.Mock();user.PostMessageW.return_value=True
        with mock.patch.object(native,"own_windows",return_value=[100,200]),mock.patch.object(native,"window_pid",side_effect=[42,99]):
            native.close_child(process,user,None,value)
        user.PostMessageW.assert_called_once_with(100,0x0010,0,0)
        self.assertTrue(native.finish_soak_record(value))
        value=valid_record();process=mock.Mock(pid=42);process.poll.return_value=None
        process.wait.side_effect=native.subprocess.TimeoutExpired("synthetic",5)
        with mock.patch.object(native,"own_windows",return_value=[100]),mock.patch.object(native,"window_pid",return_value=42):
            native.close_child(process,user,None,value)
        process.terminate.assert_called_once_with();self.assertFalse(native.finish_soak_record(value))

    def test_profile_publication_cannot_consume_reserved_disk_floor(self):
        with tempfile.TemporaryDirectory() as temp:
            path=Path(temp)/"execution.json"
            with mock.patch.object(native.shutil,"disk_usage",return_value=mock.Mock(free=native.SOAK_RESERVE_BYTES)):
                with self.assertRaisesRegex(ValueError,"publication would cross"):native.write_soak_publication(path,{"synthetic":True})
            self.assertFalse(path.exists())
            with mock.patch.object(native.shutil,"disk_usage",return_value=mock.Mock(free=native.SOAK_RESERVE_BYTES+10000)):
                native.write_soak_publication(path,{"synthetic":True})
            self.assertEqual(json.loads(path.read_text()),{"synthetic":True})


if __name__ == "__main__":
    unittest.main()
