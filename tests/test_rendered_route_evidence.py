import copy
import importlib.util
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("rendered_evidence_under_test", ROOT / "tools/rendered_route_evidence.py")
e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e)
STAMP = "2026-10-16T00:00:00+00:00"
END = "2026-10-16T00:01:00+00:00"
SESSION = "11111111-2222-4333-8444-555555555555"
OUTPUT = (ROOT / "artifacts/example-rendered-output").absolute()


def histogram(frames=6):
    bins = [0] * 2049
    bins[-1] = frames
    return dict(measured_frames=frames, measured_seconds=float(frames), mean_milliseconds=1000.0 if frames else 0,
                p95_upper_milliseconds=1000.0 if frames else 0, maximum_milliseconds=1000.0 if frames else 0,
                frames_over_60hz=frames, frames_over_33_3ms=frames, histogram_overflow_frames=frames,
                histogram_bucket_milliseconds=.25, histogram_finite_buckets=2048, histogram=bins,
                has_samples=frames > 0, within_provisional_budget=False)


def configuration():
    return dict(screen_width=1280, screen_height=720, pixel_width=1280, pixel_height=720,
                display_render_width=1280, display_render_height=720, display_system_width=1920, display_system_height=1080,
                quality_index=0, target_display=0, msaa_samples=1, vsync_count=0, target_frame_rate=-1, render_frame_interval=1,
                fullscreen_mode="Windowed", quality_name="High", camera_type="Game", pipeline_type="URP", pipeline_asset="URP",
                target_is_null=True, camera_active=True, camera_post_processing=False, hdr_supported=False, display_active=True,
                on_demand_will_render=True, run_in_background=True, aspect=1280/720, render_scale=1,
                pixel_rect=dict(x=0, y=0, width=1280, height=720))


def ready(key="main"):
    result = dict(schema_version=1, pid=1234, owned_camera_count=1, test_profile=e.PROFILE, recorded_utc=STAMP,
                  case_full_name=e.CASES[key], case_key=key, output_directory=str(OUTPUT), scene_path=e.SCENE,
                  scene_name="ForsakenCastle", camera_name="Main Camera", camera_scene_path=e.SCENE,
                  owned_scene_prepared=True, camera_post_processing=False, pipeline_at_preparation=False,
                  pipeline_after_preparation=False, pipeline_at_publication=False, scene_handle=1, camera_entity_id=22,
                  frame=1, session_id=SESSION, realtime_seconds=0)
    return result


def case_value(key="main"):
    rooms = list(e.ROOMS)
    rooms.remove("Exit")
    if key == "main":
        rooms.remove("BonusRoom")
    count = len(rooms) * 7
    counter = lambda n: {field: n for field in e.COUNTERS}
    capture_count = 11 if key == "main" else 12
    result = dict(schema_version=1, pid=1234, test_profile=e.PROFILE, case_key=key, case_full_name=e.CASES[key],
        output_directory=str(OUTPUT), scene_path=e.SCENE, scene_handle=1, camera_entity_id=22,
        disposed=True, completed=True, errors=[], final_session_id=SESSION, final_objective_flags=79 if key == "main" else 127,
        started_utc=STAMP, ended_utc=END, start_realtime=0, end_realtime=count,
        measurement_ended_utc=END, measurement_end_realtime=count, completion_frame=100, post_completion_contexts=0,
        limits=dict(visits=256, state_samples=1024, callback_details=256, worst_intervals=64, error_details=16,
                    profiler_metrics=6, histogram_buckets=2049, sample_interval_seconds=1, warmup_eligible_seconds=5),
        configuration_at_start=configuration(), configuration_at_end=configuration(), ordinary_cycles=count, rejected_cycles=0,
        all_callbacks=counter(count + capture_count * 2), ordinary_unbound_callbacks=counter(count),
        explicit_capture_callbacks=counter(capture_count * 2), outside_callbacks=counter(0),
        capture_begins=capture_count, capture_ends=capture_count, active_capture_depth=0,
        callback_details=[dict(kind="context-begin", classification="ordinary-unbound-state", frame=1, realtime=0,
            capture_depth=0, context_camera_count=1, owned_camera_callback=False, owned_camera_in_context=True, target_is_null=True)],
        callback_details_dropped=(count + capture_count * 2) * 4 - 1, visits=[], scope="Concealed Development boundary evidence")
    for index, room in enumerate(rooms):
        start = index * 7
        result["visits"].append(dict(session_id=SESSION, room=room, room_id=e.ROOMS[room], visit=index + 1,
            started_utc=STAMP, ended_utc=END, reason="room-or-session-boundary", configuration_at_start=configuration(),
            configuration_at_end=configuration(), configuration_changed=False, memory_samples=0,
            peak_sampled_managed_bytes=0, peak_sampled_unity_used_bytes=0,
            intervals=dict(session_id=SESSION, room=room, visit=index + 1, start_realtime=start, end_realtime=start+7,
                duration_seconds=7, unobserved_tail_seconds=0, observed_intervals=7, pending_boundary_exclusion="none",
                warmup_required_eligible_seconds=5, warmup_observed_eligible_seconds=5,
                raw_cold_inclusive=histogram(), warm=histogram(1),
                exclusions=[dict(reason="transition", intervals=1, seconds=1), dict(reason="warmup", intervals=5, seconds=5)])))
    result["samples"] = [dict(utc=STAMP, realtime=0, frame=1, session_id=SESSION, room="Courtyard", capture_depth=0,
        time_scale=1, running=True, application_focused=False, paused=False, configuration=configuration(),
        memory=dict(managed_estimated_bytes=0, unity_used_bytes=0, unity_reserved_bytes=0, unity_unused_reserved_bytes=0,
                    unity_allocator_available=False, meaning="Sampled allocator estimate"),
        profiler=[dict(name=name, category="unavailable", unit="unavailable", attribution="Origin unassigned",
                       available=False, valid=False, count=0, value=0) for name in sorted(e.METRICS)])]
    result["worst_intervals"] = [dict(session_id=SESSION, room="Courtyard", visit=1, frame=2, realtime=2, milliseconds=1000, warm=False)]
    return result


def callbacks():
    result = [dict(kind="run-started", utc=STAMP, is_suite=True)]
    for name in e.CASES.values():
        result.append(dict(kind="test-started", utc=STAMP, is_suite=False, full_name=name))
        result.append(dict(kind="test-finished", utc=STAMP, is_suite=False, full_name=name,
                           result="Passed", passed=1, failed=0, skipped=0, inconclusive=0))
    result.append(dict(kind="run-finished", utc=END, is_suite=True, result="Passed", passed=2, failed=0, skipped=0, inconclusive=0))
    return result


def encoded(rows):
    return b"".join(json.dumps(row).encode() + b"\n" for row in rows)


class RenderedEvidenceTests(unittest.TestCase):
    def test_histogram_empty_is_not_budget_pass(self):
        self.assertFalse(e.validate_histogram(histogram(0))["within_provisional_budget"])

    def test_large_valid_frame_interval_is_retained(self):
        self.assertEqual(e.validate_histogram(histogram())["maximum_milliseconds"], 1000)

    def test_histogram_count_tampering_fails(self):
        value = histogram()
        value["histogram"][-1] -= 1
        with self.assertRaises(ValueError): e.validate_histogram(value)

    def test_histogram_p95_tampering_fails(self):
        value = histogram()
        value["p95_upper_milliseconds"] = 999
        with self.assertRaises(ValueError): e.validate_histogram(value)

    def test_nonfinite_and_duplicate_json_rejected(self):
        for raw in (b'{"x":1,"x":2}', b'{"x":NaN}', b'{"x":Infinity}'):
            with self.subTest(raw=raw), self.assertRaises(ValueError): e.parse_json(raw)

    def test_exact_case_selection_rejects_duplicates_and_foreign_case(self):
        for names in ([e.CASES["main"]] * 2, [e.CASES["main"], "OtherCase"], list(e.EXPECTED) + ["extra"]):
            with self.subTest(names=names), self.assertRaises(ValueError): e.exact_cases(names)

    def test_normal_callback_sequence_and_equal_timestamps(self):
        self.assertEqual(e.event_history(encoded(callbacks()), STAMP, END, final=True)["count"], 6)

    def test_partial_callback_suffix_cannot_create_a_boundary(self):
        raw = encoded(callbacks()[:2]) + b'{"kind":"test-finished"'
        history = e.event_history(raw, STAMP, END)
        self.assertEqual(history["active_case"], e.CASES["main"])
        self.assertFalse(history["run_finished"])

    def test_overlapping_routes_rejected(self):
        rows = callbacks()
        rows[2], rows[3] = rows[3], rows[2]
        with self.assertRaises(ValueError): e.event_history(encoded(rows), STAMP, END)

    def test_failed_callbacks_cannot_qualify(self):
        rows = callbacks()
        rows[-1].update(result="Failed", passed=1, failed=1)
        with self.assertRaises(ValueError): e.event_history(encoded(rows), STAMP, END, final=True)

    def test_stale_callback_rejected(self):
        rows = callbacks()
        rows[1]["utc"] = "2026-10-15T00:00:00Z"
        with self.assertRaises(ValueError): e.event_history(encoded(rows), STAMP, END)

    def test_complete_readiness_has_immutable_owned_identifiers(self):
        self.assertEqual(e.validate_ready(ready(), "main", 1234, OUTPUT, STAMP, END)["camera_entity_id"], 22)

    def test_readiness_wrong_pid_scene_session_or_profile_rejected(self):
        for field, replacement in (("pid", 5), ("scene_path", "OtherScene"), ("session_id", ""), ("test_profile", "presentation")):
            value = ready(); value[field] = replacement
            with self.subTest(field=field), self.assertRaises(ValueError):
                e.validate_ready(value, "main", 1234, OUTPUT, STAMP, END)

    def test_both_complete_case_schemas_with_unavailable_optional_metrics(self):
        for key in e.CASES:
            value = case_value(key)
            result = e.validate_case(value, key, ready(key), 1234, OUTPUT, {"utc": STAMP}, {"utc": END})
            self.assertEqual(result["capture_brackets"], 11 if key == "main" else 12)
            self.assertGreater(result["raw_frames"], result["warm_frames"])

    def test_case_mixed_session_wrong_capture_and_unbalanced_callback_rejected(self):
        for mutation in (lambda v: v.update(final_session_id="foreign"), lambda v: v.update(capture_begins=10),
                         lambda v: v["all_callbacks"].update(context_end=0)):
            value = case_value(); mutation(value)
            with self.subTest(value=value.get("capture_begins")), self.assertRaises(ValueError):
                e.validate_case(value, "main", ready(), 1234, OUTPUT, {"utc": STAMP}, {"utc": END})

    def test_visit_measured_excluded_tail_accounting_rejected_when_forged(self):
        value = case_value()
        value["visits"][0]["intervals"]["unobserved_tail_seconds"] = 1
        with self.assertRaises(ValueError): e.validate_case(value, "main", ready(), 1234, OUTPUT, {"utc": STAMP}, {"utc": END})

    def test_missing_mandatory_room_rejected(self):
        value = case_value()
        value["visits"][0]["room"] = "ThroneRoom"
        with self.assertRaises(ValueError): e.validate_case(value, "main", ready(), 1234, OUTPUT, {"utc": STAMP}, {"utc": END})

    def test_unavailable_profiler_cannot_carry_fake_value(self):
        value = case_value()
        value["samples"][0]["profiler"][0]["value"] = 99
        with self.assertRaises(ValueError): e.validate_case(value, "main", ready(), 1234, OUTPUT, {"utc": STAMP}, {"utc": END})

    def test_time_scale_change_rejected(self):
        value = case_value()
        value["samples"][0]["time_scale"] = .5
        with self.assertRaises(ValueError): e.validate_case(value, "main", ready(), 1234, OUTPUT, {"utc": STAMP}, {"utc": END})

    def test_main_optional_progression_rejected(self):
        value = case_value()
        value["final_objective_flags"] = 127
        with self.assertRaises(ValueError): e.validate_case(value, "main", ready(), 1234, OUTPUT, {"utc": STAMP}, {"utc": END})

    def test_one_second_frame_cannot_be_relabelled_as_quarter_ms_bin(self):
        value = histogram(1)
        value["histogram"][-1] = 0; value["histogram"][0] = 1
        value.update(p95_upper_milliseconds=.25, histogram_overflow_frames=0)
        with self.assertRaises(ValueError): e.validate_histogram(value)

    def test_callback_category_cancellation_does_not_hide_imbalance(self):
        value = case_value()
        value["ordinary_unbound_callbacks"]["context_begin"] += 1
        value["explicit_capture_callbacks"]["context_begin"] -= 1
        with self.assertRaises(ValueError): e.validate_case(value, "main", ready(), 1234, OUTPUT, {"utc": STAMP}, {"utc": END})

    def test_cycle_total_cannot_understate_observed_intervals(self):
        value = case_value(); value["ordinary_cycles"] = 1
        with self.assertRaises(ValueError): e.validate_case(value, "main", ready(), 1234, OUTPUT, {"utc": STAMP}, {"utc": END})

    def test_warm_bin_must_exist_in_raw_lane(self):
        value = case_value()
        warm = value["visits"][0]["intervals"]["warm"]
        warm["histogram"][-1] = 0; warm["histogram"][3] = 1
        warm.update(measured_seconds=.001, mean_milliseconds=1, maximum_milliseconds=1,
                    p95_upper_milliseconds=1, histogram_overflow_frames=0, frames_over_60hz=0,
                    frames_over_33_3ms=0, within_provisional_budget=True)
        with self.assertRaises(ValueError): e.validate_case(value, "main", ready(), 1234, OUTPUT, {"utc": STAMP}, {"utc": END})

    def test_boolean_leaf_count_cannot_pass_as_integer(self):
        rows = callbacks(); rows[2]["passed"] = True
        with self.assertRaises(ValueError): e.event_history(encoded(rows), STAMP, END, final=True)

    def result_xml(self):
        cases = "".join('<test-case fullname="' + name + '" result="Passed" />' for name in e.CASES.values())
        return ('<test-run total="2" passed="2" failed="0" skipped="0" inconclusive="0" result="Passed" duration="1.5">' + cases + '</test-run>').encode()

    def completion(self):
        history = e.event_history(encoded(callbacks()), STAMP, END, final=True)
        results = e.validate_results(self.result_xml())
        completion = dict(schema_version=1, test_profile=e.PROFILE, expected_cases=list(e.CASES.values()),
            case_names=list(e.CASES.values()), total=2, passed=2, failed=0, skipped=0, inconclusive=0,
            results_sha256=results["sha256"], events_sha256=history["sha256"], evidence_complete=True,
            exit_code=0, recorded_utc=END, duration_seconds=1.5,
            case_evidence=[dict(case_key=key, ready_file="case-"+key+"-ready.json", ready_sha256="a"*64,
                                evidence_file="case-"+key+".json", evidence_sha256="b"*64, valid=True) for key in e.CASES])
        return completion, results, history

    def test_successful_completion_binds_exact_xml_callbacks_and_two_case_files(self):
        value, results, history = self.completion()
        self.assertEqual(e.validate_completion(value, results, history, STAMP, END)["exit_code"], 0)

    def test_xml_entities_and_extra_or_duplicate_case_are_rejected(self):
        raw = self.result_xml()
        for changed in (b'<!DOCTYPE test-run [<!ENTITY x "test">]>' + raw,
                        raw.replace(e.CASES["secret"].encode(), e.CASES["main"].encode()),
                        raw.replace(b'total="2"', b'total="3"')):
            with self.subTest(raw=changed[:50]), self.assertRaises(ValueError): e.validate_results(changed)

    def test_completion_swapped_file_or_changed_hash_is_rejected(self):
        for mutation in (lambda v: v.update(events_sha256="f"*64),
                         lambda v: v["case_evidence"][0].update(ready_file="case-secret-ready.json"),
                         lambda v: v.update(evidence_complete=False)):
            value, results, history = self.completion(); mutation(value)
            with self.subTest(hash=value["events_sha256"]), self.assertRaises(ValueError):
                e.validate_completion(value, results, history, STAMP, END)


if __name__ == "__main__":
    unittest.main()
