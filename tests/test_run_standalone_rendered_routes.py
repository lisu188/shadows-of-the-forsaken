import copy
import importlib.util
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("rendered_runner_under_test", ROOT / "tools/run_standalone_rendered_routes.py")
r = importlib.util.module_from_spec(spec)
spec.loader.exec_module(r)


class RenderedRunnerTests(unittest.TestCase):
    def marker_fixture(self):
        """Actual reader/validator code with an in-memory read map, never Win32."""
        stamp, upper = "2026-10-16T00:00:00Z", "2026-10-16T00:01:00Z"
        output = Path("fake-rendered-route-output").absolute()
        runtime = dict(schema_version=1, test_profile=r.evidence.PROFILE, pid=1234, unity_version="6000.6.3f1",
            platform="WindowsPlayer", graphics_api="Direct3D11", is_editor=False, batch_mode=False, development=True,
            output_directory=str(output), expected_case_count=2, expected_cases=sorted(r.evidence.EXPECTED),
            test_input_source=r.evidence.INPUT, command=["FakePlayer.exe"], recorded_utc=stamp,
            scope="Concealed Development diagnostic", hardware=dict(cpu="fake", gpu="fake", graphics_vendor="fake", os="fake",
                logical_processors=1, system_memory_mb=1, graphics_memory_mb=1, display_width=1280, display_height=720,
                screen_width=1280, screen_height=720))
        events = [dict(kind="run-started", utc=stamp, is_suite=True)]
        for name in r.evidence.CASES.values():
            events += [dict(kind="test-started", utc=stamp, is_suite=False, full_name=name),
                       dict(kind="test-finished", utc=stamp, is_suite=False, full_name=name,
                            result="Passed", passed=1, failed=0, skipped=0, inconclusive=0)]
        ready = dict(schema_version=1, pid=1234, owned_camera_count=1, test_profile=r.evidence.PROFILE,
            recorded_utc=stamp, case_full_name=r.evidence.CASES["main"], case_key="main", output_directory=str(output),
            scene_path=r.evidence.SCENE, scene_name="ForsakenCastle", camera_name="Main Camera",
            camera_scene_path=r.evidence.SCENE, owned_scene_prepared=True, camera_post_processing=False,
            pipeline_at_preparation=False, pipeline_after_preparation=False, pipeline_at_publication=False,
            scene_handle=1, camera_entity_id=22, frame=1, session_id="11111111-2222-4333-8444-555555555555", realtime_seconds=0)
        files = {"runtime.json": json.dumps(runtime).encode()}
        reader = r.MarkerReader(None, output, dict(errors=[], started_utc=stamp))
        return reader, files, events, ready, upper

    @staticmethod
    def callback_bytes(events):
        return b"".join(json.dumps(event).encode() + b"\n" for event in events)

    def test_marker_reader_first_ready_remains_immutable_through_second_case(self):
        reader, files, events, ready, upper = self.marker_fixture()
        with patch.object(reader, "read", side_effect=lambda name, *args, **kwargs: files.get(name)), \
             patch.object(r.native, "utc", return_value=upper):
            files["events.jsonl"] = self.callback_bytes(events[:1])
            self.assertEqual(reader.observe(1234), (None, None, None))
            files["events.jsonl"] = self.callback_bytes(events[:2])
            marker, accepted_ready, digest = reader.observe(1234)
            self.assertEqual(marker["active_case"], r.evidence.CASES["main"])
            self.assertIsNone(accepted_ready); self.assertIsNone(digest)
            files["case-main-ready.json"] = json.dumps(ready).encode()
            marker, accepted_ready, digest = reader.observe(1234)
            self.assertEqual(marker["active_case"], accepted_ready["case_full_name"])
            self.assertEqual(digest, r.evidence.sha(files["case-main-ready.json"]))
            files["events.jsonl"] = self.callback_bytes(events[:4])
            marker, continued, continued_digest = reader.observe(1234)
            self.assertEqual(marker["active_case"], r.evidence.CASES["secret"])
            self.assertEqual(continued, accepted_ready)
            self.assertEqual(continued_digest, digest)

    def test_marker_reader_rejects_previously_observed_callback_history_shrinking(self):
        reader, files, events, ready, upper = self.marker_fixture()
        files.update({"events.jsonl": self.callback_bytes(events[:4]), "case-main-ready.json": json.dumps(ready).encode()})
        with patch.object(reader, "read", side_effect=lambda name, *args, **kwargs: files.get(name)), \
             patch.object(r.native, "utc", return_value=upper):
            self.assertEqual(reader.observe(1234)[0]["active_case"], r.evidence.CASES["secret"])
            files["events.jsonl"] = self.callback_bytes(events[:2])
            with self.assertRaisesRegex(ValueError, "changed or shrank"):
                reader.observe(1234)

    def test_owned_wm_close_cleanup_calls_common_guard_and_never_old_hidden_validator(self):
        process, parent, user = Mock(), Mock(), Mock()
        process.pid = 1234; process.poll.return_value = None
        parent.handle = 100
        parent.pump.side_effect = lambda: setattr(process.poll, "return_value", 0)
        record = dict(errors=[], cleanup_intervened=False)
        with patch.object(r.native, "own_windows", return_value=[200, 201]), \
             patch.object(r.native, "window_pid", side_effect=lambda user, hwnd: 1234 if hwnd == 200 else 999), \
             patch.object(r.native, "check_hidden_windows", side_effect=AssertionError("old validator called")), \
             patch.object(r.concealed, "check_concealed_windows", return_value=True) as check, \
             patch.object(r.time, "sleep"):
            r.finish_process(process, user, None, record, parent)
        user.PostMessageW.assert_called_once_with(200, 0x0010, 0, 0)
        self.assertIs(check.call_args.kwargs["cleanup"], True)
        self.assertTrue(record["cleanup_intervened"])
        self.assertEqual(record["closure"]["mode"], "WM_CLOSE")
        process.terminate.assert_not_called()
        parent.close.assert_called_once_with(process)

    def test_owned_cleanup_deadline_terminates_only_spawned_handle_and_cannot_qualify(self):
        process, parent, user = Mock(), Mock(), Mock()
        process.pid = 1234; process.poll.return_value = None
        process.terminate.side_effect = lambda: setattr(process.poll, "return_value", 0)
        parent.handle = 100
        record = dict(errors=[], cleanup_intervened=False)
        with patch.object(r.native, "own_windows", return_value=[]), \
             patch.object(r.native, "check_hidden_windows", side_effect=AssertionError("old validator called")), \
             patch.object(r.time, "monotonic", side_effect=[10, 16]), patch.object(r.time, "sleep"):
            r.finish_process(process, user, None, record, parent)
        process.terminate.assert_called_once_with()
        process.wait.assert_called_once_with(timeout=5)
        parent.close.assert_called_once_with(process)
        self.assertTrue(record["closure"]["forced"])
        self.assertEqual(record["closure"]["mode"], "TerminateProcess_only_spawned_handle")
        self.assertFalse(r.qualify(record))

    def test_environment_isolated_from_all_other_profiles(self):
        output = Path("some-output").absolute()
        inherited = {name: "unexpected" for name in r.CONFLICTS}
        inherited["KEEP"] = "yes"
        actual = r.player_environment(output, inherited)
        self.assertEqual(actual, {"SHADOWS_RENDERED_ROUTE_OUTPUT": str(output),
            "SHADOWS_CAPTURE_DIR": str(output / "captures"), "KEEP": "yes"})
        self.assertEqual(inherited["SHADOWS_AUDIO_PLAYER_OUTPUT"], "unexpected")

    def test_existing_unlinked_output_parent_accepted(self):
        with tempfile.TemporaryDirectory() as temporary:
            r.guard_output_parent(Path(temporary).absolute())

    def test_audio_lifecycle_opt_in_uses_only_the_exact_owned_output(self):
        output = Path("owned-output").absolute()
        inherited = {name: "unexpected" for name in r.CONFLICTS}
        inherited["KEEP"] = "yes"
        actual = r.player_environment(output, inherited, observe_audio_lifecycle=True)
        self.assertEqual(actual, {"SHADOWS_RENDERED_ROUTE_OUTPUT": str(output),
            "SHADOWS_CAPTURE_DIR": str(output / "captures"),
            "SHADOWS_AUDIO_RUN_LIFECYCLE_OUTPUT": str(output), "KEEP": "yes"})
        self.assertEqual(inherited["SHADOWS_AUDIO_RUN_LIFECYCLE_OUTPUT"], "unexpected")

    def test_nonexistent_or_file_output_parent_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            file = root / "file"; file.write_text("test")
            for path in (root / "absent", file):
                with self.subTest(path=path), self.assertRaises(ValueError): r.guard_output_parent(path)

    def test_main_reaches_parent_guard_before_native_api_or_receipt_access(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).absolute()
            with patch.object(r, "os", SimpleNamespace(name="nt")), \
                 patch.object(r, "guard_output_parent", side_effect=ValueError("parent guard sentinel")) as guard, \
                 patch.object(r.native, "windows_api") as api, patch.object(r.native.manifest, "digest") as digest:
                with self.assertRaisesRegex(ValueError, "parent guard sentinel"):
                    r.main(["--player", str(root / "player/game.exe"), "--output", str(root / "evidence"),
                            "--build-receipt", str(root / "build.json"), "--build-receipt-sha256", "a"*64])
                guard.assert_called_once_with(root)
                api.assert_not_called(); digest.assert_not_called()

    def test_failed_qualification_cannot_be_promoted_by_missing_fields(self):
        record = {"errors": []}
        self.assertFalse(r.qualify(record))
        self.assertTrue(record["errors"])
        self.assertEqual(record["status"], "concealed_rendered_routes_failed")

    def test_zero_exit_cleanup_does_not_count_as_self_exit(self):
        record = {"errors": [], "closure": {"mode": "WM_CLOSE", "exit_code": 0, "forced": False}, "cleanup_intervened": True}
        self.assertFalse(r.qualify(record))
        self.assertFalse(record["qualification_checks"]["self_exit_zero"])
        self.assertFalse(record["qualification_checks"]["no_intervention"])

    def test_self_exited_child_only_closes_owned_host(self):
        process, parent, record = Mock(), Mock(), {"errors": [], "cleanup_intervened": False}
        process.pid = 123; process.poll.return_value = 0
        r.finish_process(process, Mock(), Mock(), record, parent)
        parent.close.assert_called_once_with(process)
        process.terminate.assert_not_called()
        self.assertEqual(record["closure"]["mode"], "self_exit")
        self.assertFalse(record["cleanup_intervened"])

    def test_initial_file_sharing_error_is_retained_without_authorizing_show(self):
        with tempfile.TemporaryDirectory() as temporary:
            record = {"errors": []}
            reader = r.MarkerReader(None, Path(temporary), record)
            error = OSError("writer has not closed"); error.winerror = 32
            with patch.object(r.native, "shared_read", side_effect=error):
                self.assertIsNone(reader.read("runtime.json", 100))
            self.assertIsNone(reader.marker)
            self.assertEqual(record["protocol_publication_transients"]["count"], 1)

    def test_trusted_or_unknown_file_error_is_fail_closed(self):
        with tempfile.TemporaryDirectory() as temporary:
            reader = r.MarkerReader(None, Path(temporary), {"errors": []})
            for trusted, code in ((True, 32), (False, 5)):
                error = OSError("denied"); error.winerror = code
                with self.subTest(trusted=trusted, code=code), patch.object(r.native, "shared_read", side_effect=error), self.assertRaises(OSError):
                    reader.read("runtime.json", 100, trusted=trusted)

    def test_snapshot_requires_separate_support_assembly(self):
        rows = [{"path": "Game_Data/Managed/Shadows.PlayMode.Tests.dll"}]
        with patch.object(r.native.manifest, "regular"), patch.object(r.native.manifest, "file_tree", return_value=rows):
            with self.assertRaisesRegex(ValueError, "Missing diagnostic assembly"):
                r.snapshot_player(Path("Game.exe"))

    def passing_record(self):
        return dict(errors=[], pid=123, results_verified=True, runtime_verified=True, events_verified=True,
            completion_verified=True, rendered_routes_verified=True, captures_verified=True,
            integrity_before_launch=dict(normal_integrity=True), child_integrity=dict(normal_integrity=True),
            closure=dict(mode="self_exit", exit_code=0, forced=False), cleanup_intervened=False,
            foreground_or_visually_exposed_window_violation=False,
            transparent_offscreen_guard=dict(shown_after_child_verified=True, samples=1),
            diagnostics_readiness_guard=dict(verified=True, receipt=dict(pid=123), verified_utc="2026-10-16T00:00:01Z"),
            child_visibility_guard=dict(visibility_established=True, visibility_lost=False,
                actual_utf_started_marker=dict(runtime_pid=123), case_visibility_observed=sorted(r.evidence.EXPECTED),
                history_limit_reached=False, dropped_samples=0, samples=1, show_requests=[],
                history=[dict(utc="2026-10-16T00:00:00Z", host=dict(visible=False), visible_owned_descendants=[])]),
            parent_relation_verified=True, hidden_parent=dict(destroyed=True), power_request=dict(restored=True),
            identity_unchanged=True, resource_budget=dict(checks=1), final_resource_check=True,
            logs_scanned=True, graphics_threading_observations=[dict(mode="kGfxThreadingModeClientWorkerJobs")],
            native_fatal_markers=[], audio_failure_markers=[], crash_reports=[], callback_failure_present=False)

    def test_already_visible_contained_child_needs_no_repeated_show(self):
        record = self.passing_record()
        self.assertTrue(r.qualify(record))

    def test_passive_audio_gate_requires_closed_evidence_only_when_requested(self):
        original = self.passing_record()
        self.assertTrue(r.qualify(original))
        self.assertEqual(len(original["qualification_checks"]), 23)
        for verified in (None, False, True):
            with self.subTest(verified=verified):
                record = self.passing_record()
                record["audio_lifecycle_observation_requested"] = True
                if verified is not None:
                    record["audio_lifecycle_verified"] = verified
                self.assertEqual(r.qualify(record), verified is True)
                self.assertEqual(len(record["qualification_checks"]), 24)
                self.assertEqual(record["qualification_checks"]["passive_audio_lifecycle_verified"], verified is True)

    def test_malformed_audio_request_cannot_silently_disable_the_extra_gate(self):
        for requested in ("true", 1, None):
            with self.subTest(requested=requested):
                record = self.passing_record()
                record["audio_lifecycle_observation_requested"] = requested
                self.assertFalse(r.qualify(record))
                self.assertIn("Audio lifecycle observation request must be boolean", record["errors"])

    def test_repeated_child_show_or_missing_final_resource_check_rejected(self):
        for field in ("show", "resource", "audio", "capture"):
            record = self.passing_record()
            if field == "show": record["child_visibility_guard"]["show_requests"] = [dict(queued=True, observed_visible=True)] * 2
            if field == "resource": record["final_resource_check"] = False
            if field == "audio": record["audio_failure_markers"] = ["FMOD failure"]
            if field == "capture": record["captures_verified"] = False
            with self.subTest(field=field): self.assertFalse(r.qualify(record))


if __name__ == "__main__":
    unittest.main()
