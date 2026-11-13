"""Standalone fixture qualification regressions; no native process is launched."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("routes", ROOT / "tools/run_standalone_routes.py")
routes = importlib.util.module_from_spec(spec)
spec.loader.exec_module(routes)


def xml_result(tag="test-run"):
    root = ET.Element(tag, result="Passed", total="10", passed="10", failed="0", skipped="0",
                      inconclusive="0", duration="300.25")
    suite = ET.SubElement(root, "test-suite", result="Passed")
    for name in sorted(routes.EXPECTED):
        ET.SubElement(suite, "test-case", fullname=name, result="Passed")
    return root


def encoded(root):
    return ET.tostring(root, encoding="utf-8")


def events():
    rows = [dict(kind="run-started", utc="2026-10-14T10:00:00Z")]
    for name in sorted(routes.EXPECTED):
        common = dict(full_name=name, is_suite=False, utc="2026-10-14T10:00:01Z")
        rows.append(dict(common, kind="test-started"))
        rows.append(dict(common, kind="test-finished", result="Passed", passed=1,
                         failed=0, skipped=0, inconclusive=0))
    rows.append(dict(kind="run-finished", result="Passed", passed=10, failed=0, skipped=0,
                     inconclusive=0, utc="2026-10-14T10:05:00Z"))
    return rows


def event_bytes(rows):
    return ("\n".join(json.dumps(row) for row in rows) + "\n").encode()


def record():
    return dict(errors=[], results_verified=True, runtime_verified=True, events_verified=True,
                completion_verified=True, integrity_before_launch=dict(normal_integrity=True),
                child_integrity=dict(normal_integrity=True), closure=dict(mode="self_exit", exit_code=0, forced=False),
                cleanup_intervened=False, foreground_or_visible_window_violation=False,
                hidden_parent=dict(destroyed=True), power_request=dict(restored=True), identity_unchanged=True,
                parent_relation_verified=True, callback_failure_present=False,
                logs_scanned=True, native_fatal_markers=[], audio_failure_markers=[], crash_reports=[],
                graphics_threading_observations=[dict(mode="kGfxThreadingModeClientWorkerJobs")])


class StandaloneRouteTests(unittest.TestCase):
    def test_exact_catalog_accepts_both_actual_nunit_root_shapes(self):
        for tag in ("test-run", "test-suite"):
            with self.subTest(tag=tag):
                result = routes.validate_results(encoded(xml_result(tag)))
                self.assertEqual(result["case_names"], sorted(routes.EXPECTED))
                self.assertEqual(len(result["case_names"]), 10)

    def test_green_aggregate_cannot_hide_missing_duplicate_foreign_or_wrong_argument_case(self):
        for change in ("missing", "duplicate", "foreign", "argument"):
            root = xml_result()
            cases = list(root.iter("test-case"))
            if change == "missing":
                root.find("test-suite").remove(cases[0])
            elif change == "duplicate":
                cases[0].set("fullname", cases[1].get("fullname"))
            elif change == "foreign":
                cases[0].set("fullname", "ShadowsOfTheForsaken.Tests.PlayMode.BaselineSceneTests.Other")
            else:
                next(case for case in cases if "(False)" in case.get("fullname")).set(
                    "fullname", routes.PREFIX + "FullCastleRouteTests.FramingRaySelectsNearestSurfaceIncludingUnlabelledPlayer(false)")
            with self.subTest(change=change), self.assertRaises(ValueError):
                routes.validate_results(encoded(root))

    def test_green_root_cannot_hide_failed_case_failed_suite_skips_or_inconclusive(self):
        for target in ("case", "suite", "skip", "inconclusive"):
            root = xml_result()
            if target == "case":
                next(root.iter("test-case")).set("result", "Failed")
            elif target == "suite":
                root.find("test-suite").set("result", "Failed")
            elif target == "skip":
                root.set("skipped", "1")
            else:
                root.set("inconclusive", "1")
            with self.subTest(target=target), self.assertRaises(ValueError):
                routes.validate_results(encoded(root))

    def test_xml_entities_and_nonfinite_duration_are_rejected(self):
        with self.assertRaises(ValueError):
            routes.validate_results(b'<!DOCTYPE x [<!ENTITY secret SYSTEM "file:///secret">]>' + encoded(xml_result()))
        root = xml_result()
        root.set("duration", "NaN")
        with self.assertRaises(ValueError):
            routes.validate_results(encoded(root))

    def test_actual_runtime_identity_and_noneditor_nonbatch_metadata_are_required(self):
        output = Path("attempt").absolute()
        valid = dict(schema_version=1, is_editor=False, batch_mode=False, unity_version="6000.6.3f1",
                     platform="WindowsPlayer", graphics_api="Direct3D11", pid=42,
                     output_directory=str(output), expected_case_count=10,
                     test_input_source="Fixture-controlled InputSystem events", hardware=dict(gpu="Test GPU"))
        routes.validate_runtime(valid, 42, output)
        for changed in (dict(is_editor=True), dict(batch_mode=True), dict(pid=99), dict(unity_version="other"),
                        dict(graphics_api="Null"), dict(hardware={}), dict(output_directory=str(output / "other"))):
            with self.subTest(changed=changed), self.assertRaises(ValueError):
                routes.validate_runtime({**valid, **changed}, 42, output)

    def test_callback_evidence_requires_each_case_once_in_start_finish_order(self):
        routes.validate_events(event_bytes(events()))
        for change in ("missing", "duplicate", "foreign", "order", "run_boundary", "failed"):
            rows = events()
            if change == "missing":
                rows.pop(2)
            elif change == "duplicate":
                rows.insert(3, rows[2].copy())
            elif change == "foreign":
                rows[1]["full_name"] = "Other.Case"
            elif change == "order":
                rows[1], rows[2] = rows[2], rows[1]
            elif change == "run_boundary":
                rows.insert(1, rows[0].copy())
            else:
                rows[2]["result"] = "Failed"
            with self.subTest(change=change), self.assertRaises(ValueError):
                routes.validate_events(event_bytes(rows))

    def test_completion_must_bind_exact_case_names_and_the_actual_raw_artifacts(self):
        result = routes.validate_results(encoded(xml_result()))
        observed = routes.validate_events(event_bytes(events()))
        completion = dict(schema_version=1, total=10, passed=10, failed=0, skipped=0, inconclusive=0,
                          case_names=sorted(routes.EXPECTED), exit_code=0,
                          results_sha256=result["sha256"], events_sha256=observed["sha256"])
        routes.validate_completion(completion, result, observed)
        for change in (dict(exit_code=1), dict(case_names=["Other.Case"] * 10),
                       dict(results_sha256="0" * 64), dict(events_sha256="0" * 64), dict(skipped=1)):
            with self.subTest(change=change), self.assertRaises(ValueError):
                routes.validate_completion({**completion, **change}, result, observed)

    def test_report_must_describe_actual_disconnected_instrumented_windows_output(self):
        player = Path("TestPlayer.exe").absolute()
        report = dict(schema_version=1, result="Succeeded", target="StandaloneWindows64", errors=0,
                      include_test_assemblies=True, auto_run_player=False, connect_to_host=False,
                      development=True, bytes=123456, duration_seconds=12.5, output_path=str(player),
                      scenes=["Assets/InitTestScene7f595842-c402-4b75-853b-5d27405d0fc3.unity",
                              "Assets/Scenes/ForsakenCastle.unity"])
        routes.validate_build_report(report, player)
        for change in (dict(include_test_assemblies=False), dict(auto_run_player=True), dict(connect_to_host=True),
                       dict(development=False), dict(target="StandaloneWindows"), dict(errors=1), dict(scenes=[]),
                       dict(bytes=0), dict(duration_seconds=0.0), dict(duration_seconds=float("nan")),
                       dict(duration_seconds=float("inf")), dict(scenes=list(reversed(report["scenes"]))),
                       dict(scenes=report["scenes"] + ["Assets/Scenes/ForsakenRuntimeCastle.unity"])):
            with self.subTest(change=change), self.assertRaises(ValueError):
                routes.validate_build_report({**report, **change}, player)
        disconnected_field_missing = report.copy()
        disconnected_field_missing.pop("connect_to_host")
        with self.assertRaises(ValueError):
            routes.validate_build_report(disconnected_field_missing, player)

    def test_full_player_inventory_detects_changed_added_and_removed_bytes(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            player = root / "TestPlayer.exe"
            player.write_bytes(b"fixture executable identity")
            assembly = root / "TestPlayer_Data/Managed/Shadows.PlayMode.Tests.dll"
            assembly.parent.mkdir(parents=True)
            assembly.write_bytes(b"fixture test assembly identity")
            before = routes.snapshot_player(player)
            self.assertTrue(routes.compare_identity(before, routes.snapshot_player(player)))
            for change in ("changed", "added", "removed"):
                if change == "changed":
                    player.write_bytes(b"different bytes")
                elif change == "added":
                    (root / "extra.bin").write_bytes(b"new file")
                else:
                    (root / "extra.bin").unlink()
                    player.unlink()
                with self.subTest(change=change), self.assertRaises(ValueError):
                    routes.compare_identity(before, routes.native.manifest.file_tree(root))
                if change == "changed":
                    player.write_bytes(b"fixture executable identity")

    def test_successful_self_exit_never_posts_close_or_invokes_fallback(self):
        process, parent = mock.Mock(), mock.Mock()
        process.pid, process.poll.return_value = 42, 0
        evidence = record()
        with mock.patch.object(routes.native, "cleanup_owned_resources") as fallback:
            routes.finish_process(process, mock.Mock(), None, evidence, parent)
        fallback.assert_not_called()
        parent.close.assert_called_once_with(process)
        self.assertEqual(evidence["closure"]["mode"], "self_exit")
        self.assertEqual(evidence["closure"]["wm_close"], [])

    def test_fallback_even_after_green_results_cannot_qualify(self):
        process = mock.Mock()
        process.poll.return_value = None
        evidence = record()
        with mock.patch.object(routes.native, "cleanup_owned_resources") as fallback:
            routes.finish_process(process, mock.Mock(), None, evidence, mock.Mock())
        fallback.assert_called_once()
        self.assertFalse(routes.qualify(evidence))

    def test_critical_lifecycle_mutations_reject_otherwise_green_results(self):
        self.assertTrue(routes.qualify(record()))
        mutations = [dict(results_verified=False), dict(runtime_verified=False), dict(events_verified=False),
                     dict(completion_verified=False), dict(child_integrity={}), dict(logs_scanned=False),
                     dict(foreground_or_visible_window_violation=True), dict(identity_unchanged=False),
                     dict(hidden_parent=dict(destroyed=False)), dict(power_request=dict(restored=False)),
                     dict(parent_relation_verified=False), dict(callback_failure_present=True),
                     dict(native_fatal_markers=[dict(text="Native Crash Reporting")]),
                     dict(audio_failure_markers=[dict(text="FMOD output failed")]),
                     dict(crash_reports=[dict(path="crash.dmp")]), dict(errors=["unexpected failure"]),
                     dict(closure=dict(mode="self_exit", exit_code=1, forced=False)),
                     dict(closure=dict(mode="WM_CLOSE", exit_code=0, forced=False)),
                     dict(graphics_threading_observations=[dict(mode="kGfxThreadingModeNonThreaded")])]
        for mutation in mutations:
            with self.subTest(mutation=mutation):
                evidence = record()
                evidence.update(copy.deepcopy(mutation))
                self.assertFalse(routes.qualify(evidence))

    def test_actual_owned_render_window_parentage_is_required_not_launch_intent(self):
        user = mock.Mock()
        user.GetForegroundWindow.return_value = 0
        user.GetParent.side_effect = [0, 900]
        rows = [dict(window=100, owner_pid=42, **{"class": "UnityWndClass"}, style=0),
                dict(window=200, owner_pid=42, **{"class": "UnityWndClass"}, style=0x40000000)]
        with mock.patch.object(routes.native, "own_windows", return_value=[100, 200]), \
             mock.patch.object(routes.native, "describe_windows", return_value=copy.deepcopy(rows)):
            observation = routes.observe_parent(user, None, 42, 900)
        self.assertTrue(observation["matching_parent_observed"])
        user.GetParent.side_effect = [0, 901]
        with mock.patch.object(routes.native, "own_windows", return_value=[100, 200]), \
             mock.patch.object(routes.native, "describe_windows", return_value=copy.deepcopy(rows)):
            self.assertFalse(routes.observe_parent(user, None, 42, 900)["matching_parent_observed"])

    def test_child_environment_is_opt_in_and_cannot_inherit_the_shipping_observer(self):
        inherited = dict(SHADOWS_PLAYER_EVIDENCE_DIR="old", SHADOWS_CAPTURE_DIR="old", OTHER="preserved")
        output = Path("attempt").absolute()
        environment = routes.player_environment(output, inherited)
        self.assertNotIn("SHADOWS_PLAYER_EVIDENCE_DIR", environment)
        self.assertEqual(environment["SHADOWS_ROUTE_PLAYER_OUTPUT"], str(output))
        self.assertEqual(environment["SHADOWS_CAPTURE_DIR"], str(output / "captures"))
        self.assertEqual(environment["OTHER"], "preserved")
        self.assertEqual(inherited["SHADOWS_PLAYER_EVIDENCE_DIR"], "old")

    def test_timeout_and_disk_guards_bound_execution_without_relaxing_limits(self):
        initial = routes.RESERVE + 10 * 2**30
        self.assertIsNone(routes.stop_reason(1799, 1800, initial, initial))
        self.assertEqual(routes.stop_reason(1800, 1800, initial, initial), "timeout")
        self.assertEqual(routes.stop_reason(1, 1800, initial, routes.RESERVE - 1), "disk reserve")
        self.assertEqual(routes.stop_reason(1, 1800, initial, initial - routes.GROWTH_LIMIT - 1), "disk growth limit")


if __name__ == "__main__":
    unittest.main()
