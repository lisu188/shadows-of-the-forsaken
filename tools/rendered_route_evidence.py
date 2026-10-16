"""Pure validation of the separate concealed Development two-route diagnostic.

Ordinary SRP boundaries are CPU-side observations. Explicit captures, camera
snaps, readback and their crossing intervals are excluded by the test observer.
This never qualifies GPU time, displayed FPS, Release or human playthroughs.
"""
import datetime as dt
import hashlib
import json
import math
from pathlib import Path
import re
import uuid
import xml.etree.ElementTree as ET

PROFILE = "rendered-routes"
PREFIX = "ShadowsOfTheForsaken.Tests.PlayMode.FullCastleRouteTests."
CASES = {
    "main": PREFIX + "CastleMainRouteCompletesThroughControlsWithoutSecret",
    "secret": PREFIX + "CastleSecretRouteReturnsThroughThroneAndStillRequiresFinalFight",
}
EXPECTED = frozenset(CASES.values())
FILTER = (r"^ShadowsOfTheForsaken\.Tests\.PlayMode\.FullCastleRouteTests\."
          r"(CastleMainRouteCompletesThroughControlsWithoutSecret|"
          r"CastleSecretRouteReturnsThroughThroneAndStillRequiresFinalFight)$")
SCENE = "Assets/Scenes/ForsakenCastle.unity"
INPUT = "Unchanged existing main and secret FullCastleRouteTests InputSystem controls"
MAX_JSON = 32 * 1024 * 1024


def require(condition, message):
    if not condition:
        raise ValueError(message)


def integer(value, label, minimum=0, maximum=100_000_000):
    require(type(value) is int and minimum <= value <= maximum, "Invalid integer " + label)
    return value


def number(value, label, minimum=0, maximum=None):
    require(type(value) in (int, float) and math.isfinite(value), "Invalid finite " + label)
    require((minimum is None or value >= minimum) and (maximum is None or value <= maximum),
            "Out of range " + label)
    return value


def boolean(value, label):
    require(type(value) is bool, "Invalid boolean " + label)
    return value


def text(value, label, maximum=4096):
    require(type(value) is str and 0 < len(value) <= maximum, "Invalid text " + label)
    return value


def unique_json(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON key: " + key)
        result[key] = value
    return result


def parse_json(raw, limit=MAX_JSON):
    require(type(raw) is bytes and 0 < len(raw) <= limit, "Invalid JSON byte size")
    return json.loads(raw.decode("utf-8-sig"), object_pairs_hook=unique_json,
                      parse_constant=lambda value: (_ for _ in ()).throw(ValueError("Nonfinite JSON " + value)))


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def timestamp(value):
    result = dt.datetime.fromisoformat(text(value, "UTC timestamp", 64).replace("Z", "+00:00"))
    require(result.tzinfo is not None and result.utcoffset() == dt.timedelta(0), "Expected UTC timestamp")
    return result


def fresh(value, lower, upper):
    result = timestamp(value)
    require(timestamp(lower) <= result <= timestamp(upper), "Timestamp outside this execution")
    return result


def exact_cases(names):
    require(type(names) is list and all(type(name) is str for name in names) and
            len(names) == 2 and len(set(names)) == 2 and set(names) == EXPECTED,
            "Expected exactly the existing main and secret route cases")


def counts(value, passed=True):
    rows = {key: integer(value.get(key), key, maximum=2)
            for key in ("passed", "failed", "skipped", "inconclusive")}
    require(sum(rows.values()) == 2, "Result counters do not total two")
    if passed:
        require(rows == {"passed": 2, "failed": 0, "skipped": 0, "inconclusive": 0}, "Routes did not both pass")
    return rows


def validate_results(raw, passed=True):
    require(type(raw) is bytes and 0 < len(raw) <= 10_000_000, "Invalid NUnit XML size")
    require(b"<!DOCTYPE" not in raw.upper() and b"<!ENTITY" not in raw.upper(), "XML DTD/entities refused")
    root = ET.fromstring(raw)
    require(root.tag == "test-run", "Expected callback test-run XML")
    require(int(root.attrib["total"]) == 2, "Unexpected NUnit total")
    result_counts = counts({key: int(root.attrib[key]) for key in ("passed", "failed", "skipped", "inconclusive")}, passed)
    nodes = list(root.iter("test-case"))
    exact_cases([node.get("fullname") for node in nodes])
    if passed:
        require(root.get("result") == "Passed" and all(node.get("result") == "Passed"
                for node in [*nodes, *root.iter("test-suite")]), "NUnit case/suite/root failed")
    return {"counts": result_counts, "case_names": sorted(EXPECTED), "sha256": sha(raw),
            "duration_seconds": number(float(root.get("duration", "nan")), "NUnit duration")}


def validate_runtime(value, pid, output):
    require(value.get("schema_version") == 1 and type(value["schema_version"]) is int and
            value.get("test_profile") == PROFILE, "Runtime schema/profile differs")
    require(value.get("pid") == pid and type(value["pid"]) is int, "Runtime PID differs")
    require(value.get("unity_version") == "6000.6.3f1" and value.get("platform") == "WindowsPlayer" and
            value.get("graphics_api") == "Direct3D11", "Runtime pin/platform/renderer differs")
    require(value.get("is_editor") is False and value.get("batch_mode") is False and
            value.get("development") is True, "Expected graphical Development native test player")
    require(Path(text(value.get("output_directory"), "runtime output")).absolute() == output,
            "Runtime output belongs to another execution")
    require(type(value.get("expected_case_count")) is int and value["expected_case_count"] == 2,
            "Runtime selected count differs")
    exact_cases(value.get("expected_cases"))
    require(value.get("test_input_source") == INPUT, "Input origin differs")
    hardware = value.get("hardware")
    require(type(hardware) is dict, "Missing hardware metadata")
    for key in ("cpu", "gpu", "graphics_vendor", "os"):
        text(hardware.get(key), "hardware " + key)
    for key in ("logical_processors", "system_memory_mb", "graphics_memory_mb", "display_width",
                "display_height", "screen_width", "screen_height"):
        integer(hardware.get(key), "hardware " + key, minimum=1, maximum=10_000_000)
    require(type(value.get("command")) is list and all(type(arg) is str for arg in value["command"]),
            "Runtime command metadata missing")
    text(value.get("scope"), "measurement scope")
    timestamp(value.get("recorded_utc"))
    return value


def event_history(raw, lower, upper, final=False):
    require(type(raw) is bytes and len(raw) <= 8 * 1024 * 1024, "Callback input exceeds its bound")
    complete = raw[:raw.rfind(b"\n") + 1] if b"\n" in raw else b""
    events = [parse_json(line, 64 * 1024) for line in complete.splitlines() if line.strip()]
    require(1 <= len(events) <= 6, "Missing/excessive callback events")
    require(events[0].get("kind") == "run-started", "Missing run-started boundary")
    starts, finishes, active, ended = {}, {}, None, False
    previous = timestamp(lower)
    for i, event in enumerate(events):
        stamp = fresh(event.get("utc"), lower, upper)
        require(stamp >= previous, "Callback clock regressed")
        previous = stamp
        kind = event.get("kind")
        require(not ended and kind in ("run-started", "test-started", "test-finished", "run-finished"),
                "Unknown/late callback event")
        boolean(event.get("is_suite"), "callback suite")
        if kind == "run-started":
            require(i == 0, "Duplicate run start")
        elif kind == "test-started":
            name = event.get("full_name")
            require(not event["is_suite"] and name in EXPECTED and name not in starts and active is None,
                    "Duplicate/foreign/overlapping route start")
            starts[name], active = event, name
        elif kind == "test-finished":
            name = event.get("full_name")
            require(not event["is_suite"] and name in starts and name not in finishes and active == name,
                    "Route finish has no matching start")
            leaf_counts = [integer(event.get(key), "case " + key, maximum=1)
                           for key in ("passed", "failed", "skipped", "inconclusive")]
            require(sum(leaf_counts) == 1, "Case callback counters do not total one")
            finishes[name], active = event, None
        else:
            require(active is None and set(starts) == EXPECTED and set(finishes) == EXPECTED,
                    "Run finished without both complete routes")
            counts(event, passed=False)
            ended = True
    if final:
        require(ended and len(events) == 6 and raw == complete, "Missing final complete callback boundary")
        counts(events[-1])
        require(events[-1].get("result") == "Passed", "Run callback failed")
        require(all(item.get("result") == "Passed" and item.get("passed") == 1 and
                    all(item.get(key) == 0 for key in ("failed", "skipped", "inconclusive"))
                    for item in finishes.values()), "Route callback failed")
    return {"events": events, "starts": starts, "finishes": finishes, "active_case": active,
            "run_finished": ended, "count": len(events), "sha256": sha(complete), "complete": complete}


def validate_build_report(value, player):
    require(value.get("schema_version") == 1 and type(value["schema_version"]) is int and
            value.get("test_profile") == PROFILE and value.get("test_filter") == FILTER,
            "Build schema/profile/filter differs")
    exact_cases(value.get("expected_cases"))
    require(value.get("result") == "Succeeded" and value.get("target") == "StandaloneWindows64" and
            type(value.get("errors")) is int and value["errors"] == 0, "Build did not succeed")
    integer(value.get("warnings"), "build warnings")
    integer(value.get("bytes"), "build bytes", minimum=1, maximum=10 * 2**30)
    number(value.get("duration_seconds"), "build duration", minimum=1e-9)
    require(value.get("include_test_assemblies") is True and value.get("development") is True and
            value.get("auto_run_player") is False and value.get("connect_to_host") is False,
            "Build options differ from local build-only Development UTF")
    require(Path(text(value.get("output_path"), "build output")).absolute() == player,
            "Build executable path differs")
    scenes = value.get("scenes")
    require(type(scenes) is list and len(scenes) == 2 and scenes[1] == SCENE and
            type(scenes[0]) is str and re.fullmatch(
                r"Assets/InitTestScene[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.unity", scenes[0]),
            "Expected one UTF bootstrap then primary scene")
    timestamp(value.get("recorded_utc"))
    return value


def validate_histogram(value):
    require(type(value) is dict, "Missing histogram")
    count = integer(value.get("measured_frames"), "measured frames")
    bins = value.get("histogram")
    require(type(bins) is list and len(bins) == 2049 and
            all(type(item) is int and item >= 0 for item in bins) and sum(bins) == count,
            "Histogram bin/count mismatch")
    require(value.get("histogram_bucket_milliseconds") == .25 and value.get("histogram_finite_buckets") == 2048,
            "Histogram policy differs")
    require(value.get("histogram_overflow_frames") == bins[-1], "Histogram overflow differs")
    total = number(value.get("measured_seconds"), "measured seconds")
    mean = number(value.get("mean_milliseconds"), "mean milliseconds")
    maximum = number(value.get("maximum_milliseconds"), "maximum milliseconds")
    p95 = number(value.get("p95_upper_milliseconds"), "p95 upper milliseconds")
    require(value.get("has_samples") is (count > 0), "Empty histogram classification differs")
    if count:
        require(math.isclose(mean, total * 1000 / count, rel_tol=1e-9, abs_tol=1e-9) and mean <= maximum + 1e-9,
                "Histogram mean/max differs")
        cumulative, percentile = 0, None
        for index, item in enumerate(bins):
            cumulative += item
            if cumulative >= math.ceil(count * .95):
                percentile = maximum if index == 2048 else (index + 1) * .25
                break
        require(p95 == percentile, "Histogram percentile differs")
        lower_moment = sum(bins[index] * index * .25 for index in range(2049))
        upper_moment = sum(bins[index] * (index + 1) * .25 for index in range(2048)) + bins[-1] * maximum
        require(lower_moment <= total * 1000 + 1e-6 <= upper_moment + 2e-6,
                "Histogram duration is outside its bin moments")
        last = max(index for index, item in enumerate(bins) if item)
        require(maximum >= last * .25 and (last == 2048 or maximum <= (last + 1) * .25),
                "Histogram maximum is outside its last occupied bin")
    else:
        require(total == mean == maximum == p95 == 0, "Empty histogram carries timing values")
    over60 = integer(value.get("frames_over_60hz"), "frames over 60Hz", maximum=count)
    over33 = integer(value.get("frames_over_33_3ms"), "frames over provisional budget", maximum=count)
    require(over33 <= over60, "Frame threshold counters disagree")
    for threshold, actual in ((1000 / 60, over60), (33.3, over33)):
        definitely_over = sum(bins[index] for index in range(2049) if index * .25 >= threshold)
        possibly_over = bins[-1] + sum(bins[index] for index in range(2048) if (index + 1) * .25 > threshold)
        require(definitely_over <= actual <= possibly_over, "Histogram threshold counter differs from bins")
    require(value.get("within_provisional_budget") is (count > 0 and mean <= 33.3 and p95 <= 33.3),
            "Provisional budget classification differs")
    return value


ROOMS = {name: index for index, name in enumerate(("Courtyard", "FirstEncounter", "Puzzle", "ThroneRoom",
                                                "Library", "Catacombs", "BonusRoom", "FinalArena", "Exit"))}
COMMON_READY = frozenset(("schema_version", "pid", "owned_camera_count", "test_profile", "recorded_utc",
                         "case_full_name", "output_directory", "scene_path", "scene_name", "camera_name",
                         "camera_scene_path", "owned_scene_prepared", "camera_post_processing",
                         "pipeline_at_preparation", "pipeline_after_preparation", "pipeline_at_publication"))
READY_FIELDS = COMMON_READY | {"case_key", "scene_handle", "camera_entity_id", "frame", "session_id", "realtime_seconds"}
COUNTERS = ("context_begin", "context_end", "all_camera_begin", "all_camera_end", "owned_camera_begin", "owned_camera_end")
METRICS = {"Main Thread", "Render Thread", "Draw Calls Count", "SetPass Calls Count", "Shadow Casters Count", "Triangles Count"}


def session(value):
    try:
        result = uuid.UUID(text(value, "session", 36))
    except (ValueError, AttributeError) as error:
        raise ValueError("Invalid session UUID") from error
    require(result.int != 0 and str(result) == value, "Expected canonical nonempty session UUID")
    return value


def signed_integer(value, label):
    return integer(value, label, minimum=-(2**31), maximum=2**31 - 1)


def validate_ready(value, key, pid, output, lower, upper):
    require(type(value) is dict and set(value) == READY_FIELDS, "Readiness DTO fields differ")
    require(type(value["schema_version"]) is int and value["schema_version"] == 1 and
            value["test_profile"] == PROFILE and type(value["pid"]) is int and value["pid"] == pid,
            "Readiness schema/profile/PID differs")
    require(value["case_key"] == key and value["case_full_name"] == CASES[key], "Readiness case differs")
    require(Path(text(value["output_directory"], "readiness output")).absolute() == output,
            "Readiness output belongs to another execution")
    require(value["scene_path"] == value["camera_scene_path"] == SCENE and value["scene_name"] == "ForsakenCastle",
            "Readiness scene/camera ownership differs")
    require(type(value["owned_camera_count"]) is int and value["owned_camera_count"] == 1 and
            value["owned_scene_prepared"] is True and value["camera_post_processing"] is False,
            "Owned single camera was not prepared before rendering")
    text(value["camera_name"], "owned camera", 256)
    integer(value["camera_entity_id"], "camera entity", minimum=1, maximum=2**64 - 1)
    integer(value["scene_handle"], "owned scene handle", maximum=2**64 - 1)
    integer(value["frame"], "ready frame")
    number(value["realtime_seconds"], "ready realtime")
    session(value["session_id"])
    for field in ("pipeline_at_preparation", "pipeline_after_preparation", "pipeline_at_publication"):
        boolean(value[field], field)
    fresh(value["recorded_utc"], lower, upper)
    return value


def canonical_ready(value):
    """Map the fully checked extended DTO to concealment-critical common fields."""
    return {key: value[key] for key in COMMON_READY}


def validate_configuration(value):
    require(type(value) is dict, "Missing render configuration")
    for field in ("screen_width", "screen_height", "pixel_width", "pixel_height", "display_render_width",
                  "display_render_height", "display_system_width", "display_system_height"):
        integer(value.get(field), field, minimum=1, maximum=100_000)
    for field in ("quality_index", "target_display", "msaa_samples"):
        integer(value.get(field), field, maximum=1024)
    integer(value.get("vsync_count"), "vsync", maximum=4)
    integer(value.get("target_frame_rate"), "target frame rate", minimum=-1, maximum=100_000)
    integer(value.get("render_frame_interval"), "render frame interval", minimum=1, maximum=100_000)
    for field in ("fullscreen_mode", "quality_name", "camera_type", "pipeline_type", "pipeline_asset"):
        text(value.get(field), field)
    require(value["camera_type"] == "Game", "Observed camera is not Game type")
    for field in ("target_is_null", "camera_active", "camera_post_processing", "hdr_supported", "display_active",
                  "on_demand_will_render", "run_in_background"):
        boolean(value.get(field), field)
    number(value.get("aspect"), "camera aspect", minimum=1e-9)
    number(value.get("render_scale"), "render scale", maximum=10)
    rect = value.get("pixel_rect")
    require(type(rect) is dict and set(rect) == {"x", "y", "width", "height"}, "Invalid camera pixel rectangle")
    for field in ("x", "y"):
        number(rect[field], "pixel rectangle " + field, minimum=None)
    for field in ("width", "height"):
        number(rect[field], "pixel rectangle " + field, minimum=1e-9)
    return value


def validate_visit(value, case, previous_end):
    require(type(value) is dict and value.get("session_id") == case["final_session_id"], "Foreign visit session")
    room = value.get("room")
    require(room in ROOMS and value.get("room_id") == ROOMS[room], "Visit room differs")
    visit = integer(value.get("visit"), "visit", minimum=1, maximum=256)
    fresh(value.get("started_utc"), case["started_utc"], case["ended_utc"])
    fresh(value.get("ended_utc"), value["started_utc"], case["ended_utc"])
    text(value.get("reason"), "visit close reason", 80)
    validate_configuration(value.get("configuration_at_start"))
    validate_configuration(value.get("configuration_at_end"))
    boolean(value.get("configuration_changed"), "configuration changed")
    integer(value.get("memory_samples"), "visit memory samples", maximum=1024)
    integer(value.get("peak_sampled_managed_bytes"), "sampled managed peak", maximum=2**50)
    integer(value.get("peak_sampled_unity_used_bytes"), "sampled Unity peak", maximum=2**50)
    intervals = value.get("intervals")
    require(type(intervals) is dict and intervals.get("session_id") == value["session_id"] and
            intervals.get("room") == room and intervals.get("visit") == visit, "Visit metric identity differs")
    start = number(intervals.get("start_realtime"), "visit start", minimum=previous_end)
    end = number(intervals.get("end_realtime"), "visit end", minimum=start)
    require(case["start_realtime"] <= start <= end <= case["end_realtime"], "Visit clock outside case")
    duration = number(intervals.get("duration_seconds"), "visit duration")
    require(math.isclose(duration, end - start, rel_tol=1e-9, abs_tol=1e-8), "Visit duration differs")
    tail = number(intervals.get("unobserved_tail_seconds"), "unobserved visit tail", maximum=duration + 1e-8)
    text(intervals.get("pending_boundary_exclusion"), "pending interval exclusion", 80)
    require(intervals.get("warmup_required_eligible_seconds") == 5, "Visit warmup policy differs")
    warmup = number(intervals.get("warmup_observed_eligible_seconds"), "observed eligible warmup")
    raw = validate_histogram(intervals.get("raw_cold_inclusive"))
    warm = validate_histogram(intervals.get("warm"))
    require(warm["measured_frames"] <= raw["measured_frames"] and warm["measured_seconds"] <= raw["measured_seconds"] + 1e-8,
            "Warm histogram is not a raw subset")
    require(all(warm_bin <= raw_bin for warm_bin, raw_bin in zip(warm["histogram"], raw["histogram"])) and
            warm["maximum_milliseconds"] <= raw["maximum_milliseconds"], "Warm histogram bin/max is not a raw subset")
    require(math.isclose(raw["measured_seconds"], warm["measured_seconds"] + warmup, rel_tol=1e-9, abs_tol=1e-8),
            "Raw/warm/warmup duration accounting differs")
    excluded = intervals.get("exclusions")
    require(type(excluded) is list and len(excluded) <= 32, "Unbounded visit exclusions")
    reasons, total_seconds, warmup_count, excluded_count = set(), 0, 0, 0
    for exclusion in excluded:
        reason = text(exclusion.get("reason"), "exclusion reason", 80)
        require(reason not in reasons, "Duplicate visit exclusion reason")
        reasons.add(reason)
        count = integer(exclusion.get("intervals"), "excluded intervals", minimum=1)
        seconds = number(exclusion.get("seconds"), "excluded seconds")
        if reason == "warmup":
            require(math.isclose(seconds, warmup, rel_tol=1e-9, abs_tol=1e-8), "Warmup exclusion duration differs")
            warmup_count = count
        else:
            total_seconds += seconds
            excluded_count += count
    require(integer(intervals.get("observed_intervals"), "observed visit intervals") ==
            raw["measured_frames"] + excluded_count, "Observed visit interval accounting differs")
    require(raw["measured_frames"] - warm["measured_frames"] == warmup_count, "Warmup frame accounting differs")
    require(math.isclose(raw["measured_seconds"] + total_seconds + tail, duration, rel_tol=1e-8, abs_tol=1e-6),
            "Visit measured/excluded/unobserved duration differs")
    if warm["has_samples"]:
        require(warmup >= 5, "Warm measurements began before five eligible seconds")
    return end


def validate_case(value, key, ready, pid, output, start_event, finish_event):
    require(type(value) is dict and type(value.get("schema_version")) is int and value["schema_version"] == 1 and
            value.get("test_profile") == PROFILE and value.get("case_key") == key and value.get("case_full_name") == CASES[key],
            "Case schema/profile/selection differs")
    require(value.get("pid") == pid and type(value["pid"]) is int and
            Path(text(value.get("output_directory"), "case output")).absolute() == output, "Case PID/output differs")
    require(value.get("scene_path") == SCENE and value.get("scene_handle") == ready["scene_handle"] and
            value.get("camera_entity_id") == ready["camera_entity_id"], "Case owned scene/camera differs")
    require(value.get("disposed") is True and value.get("completed") is True and value.get("errors") == [],
            "Observer failed, did not complete or was not disposed")
    require(value.get("final_session_id") == ready["session_id"], "Case session changed during no-death route")
    require(value.get("final_objective_flags") == (79 if key == "main" else 127), "Case objective/optional progression differs")
    fresh(value.get("started_utc"), start_event["utc"], finish_event["utc"])
    fresh(value.get("ended_utc"), value["started_utc"], finish_event["utc"])
    start = number(value.get("start_realtime"), "case start")
    end = number(value.get("end_realtime"), "case end", minimum=start)
    require(start <= ready["realtime_seconds"] <= end, "Readiness realtime outside case")
    fresh(value.get("measurement_ended_utc"), value["started_utc"], value["ended_utc"])
    measurement_end = number(value.get("measurement_end_realtime"), "measurement end", minimum=start, maximum=end)
    integer(value.get("completion_frame"), "completion frame", minimum=ready["frame"])
    integer(value.get("post_completion_contexts"), "post-completion contexts")
    limits = {"visits": 256, "state_samples": 1024, "callback_details": 256, "worst_intervals": 64,
              "error_details": 16, "profiler_metrics": 6, "histogram_buckets": 2049,
              "sample_interval_seconds": 1, "warmup_eligible_seconds": 5}
    require(value.get("limits") == limits, "Observer retention limits differ")
    validate_configuration(value.get("configuration_at_start"))
    validate_configuration(value.get("configuration_at_end"))
    for label in ("all_callbacks", "ordinary_unbound_callbacks", "explicit_capture_callbacks", "outside_callbacks"):
        require(type(value.get(label)) is dict and set(value[label]) == set(COUNTERS), "Callback counter DTO differs")
        for field in COUNTERS:
            integer(value[label][field], label + " " + field)
    for field in COUNTERS:
        require(value["all_callbacks"][field] == sum(value[label][field] for label in
                ("ordinary_unbound_callbacks", "explicit_capture_callbacks", "outside_callbacks")),
                "Callback classification counters differ")
    for begin, end_field in (("context_begin", "context_end"), ("all_camera_begin", "all_camera_end"),
                             ("owned_camera_begin", "owned_camera_end")):
        require(all(value[label][begin] == value[label][end_field] for label in
                ("all_callbacks", "ordinary_unbound_callbacks", "explicit_capture_callbacks", "outside_callbacks")),
                "Unbalanced SRP callback category")
    cycles = integer(value.get("ordinary_cycles"), "ordinary cycles", minimum=1)
    integer(value.get("rejected_cycles"), "rejected render cycles")
    require(cycles <= min(value["ordinary_unbound_callbacks"][field] for field in COUNTERS),
            "Accepted cycles exceed ordinary callback evidence")
    expected_captures = 11 if key == "main" else 12
    require(type(value.get("capture_begins")) is int and value["capture_begins"] == value.get("capture_ends") == expected_captures and
            type(value.get("active_capture_depth")) is int and value["active_capture_depth"] == 0,
            "Complete existing capture brackets differ")
    details = value.get("callback_details")
    require(type(details) is list and 0 < len(details) <= 256, "Missing/unbounded SRP details")
    dropped = integer(value.get("callback_details_dropped"), "dropped callback details")
    require(len(details) + dropped == sum(value["all_callbacks"][field] for field in
            ("context_begin", "context_end", "all_camera_begin", "all_camera_end")), "Callback detail retention count differs")
    for detail in details:
        require(detail.get("kind") in ("context-begin", "context-end", "camera-begin", "camera-end") and
                detail.get("classification") in ("ordinary-unbound-state", "explicit-capture", "outside-owned-unbound"),
                "Unknown SRP detail")
        integer(detail.get("frame"), "SRP frame")
        number(detail.get("realtime"), "SRP realtime", minimum=start, maximum=end)
        depth = integer(detail.get("capture_depth"), "SRP capture depth", maximum=1)
        require((detail["classification"] == "explicit-capture") is (depth == 1), "SRP capture classification differs")
        integer(detail.get("context_camera_count"), "SRP context camera count", minimum=-1, maximum=1024)
        for field in ("owned_camera_callback", "owned_camera_in_context", "target_is_null"):
            boolean(detail.get(field), field)
    visits = value.get("visits")
    require(type(visits) is list and 0 < len(visits) <= 256, "Missing/unbounded visits")
    previous = start
    for index, visit in enumerate(visits, 1):
        require(visit.get("visit") == index, "Visit numbers are not sequential")
        previous = validate_visit(visit, value, previous)
    require(sum(visit["intervals"]["observed_intervals"] for visit in visits) == cycles,
            "Accepted cycles differ from observed visit intervals")
    require(all(visit["intervals"]["end_realtime"] <= measurement_end for visit in visits),
            "Visit continued beyond measurement completion")
    observed_rooms = {visit["room"] for visit in visits}
    needed = set(ROOMS) - {"Exit", "BonusRoom"}
    require(needed <= observed_rooms and (("BonusRoom" in observed_rooms) is (key == "secret")),
            "Rendered observer did not retain all mandatory/optional route visits")
    require(sum(visit["intervals"]["raw_cold_inclusive"]["measured_frames"] for visit in visits) > 0,
            "Route had no eligible ordinary rendered intervals")
    samples = value.get("samples")
    require(type(samples) is list and 0 < len(samples) <= 1024, "Missing/unbounded configuration and memory samples")
    previous = start
    for sample in samples:
        fresh(sample.get("utc"), value["started_utc"], value["ended_utc"])
        previous = number(sample.get("realtime"), "state realtime", minimum=previous, maximum=measurement_end)
        require(sample.get("session_id") == value["final_session_id"] and sample.get("room") in ROOMS,
                "State sample identity differs")
        integer(sample.get("frame"), "state frame")
        require(sample["frame"] <= value["completion_frame"], "State sample continued after completion frame")
        integer(sample.get("capture_depth"), "state capture depth", maximum=1)
        require(sample.get("time_scale") == 1, "Existing route did not retain normal timeScale")
        for field in ("running", "application_focused", "paused"):
            boolean(sample.get(field), field)
        validate_configuration(sample.get("configuration"))
        memory = sample.get("memory")
        require(type(memory) is dict, "Missing memory sample")
        for field in ("managed_estimated_bytes", "unity_used_bytes", "unity_reserved_bytes", "unity_unused_reserved_bytes"):
            integer(memory.get(field), field, maximum=2**50)
        boolean(memory.get("unity_allocator_available"), "Unity allocator availability")
        text(memory.get("meaning"), "memory scope")
        metrics = sample.get("profiler")
        require(type(metrics) is list and len(metrics) == 6 and {metric.get("name") for metric in metrics} == METRICS,
                "Profiler capability list differs")
        for metric in metrics:
            for field in ("category", "unit", "attribution"):
                text(metric.get(field), "profiler " + field)
            available = boolean(metric.get("available"), "profiler availability")
            valid = boolean(metric.get("valid"), "profiler validity")
            count = integer(metric.get("count"), "profiler retained count", maximum=1)
            integer(metric.get("value"), "profiler counter", maximum=2**63 - 1)
            require((not valid or available) and (valid or count == metric["value"] == 0), "Invalid optional profiler metric state")
    worst = value.get("worst_intervals")
    require(type(worst) is list and 0 < len(worst) <= 64, "Missing/unbounded worst eligible intervals")
    previous = math.inf
    for item in worst:
        visit_number = integer(item.get("visit"), "worst visit", minimum=1, maximum=len(visits))
        visit = visits[visit_number - 1]
        require(item.get("session_id") == visit["session_id"] and item.get("room") == visit["room"], "Worst interval visit differs")
        integer(item.get("frame"), "worst frame")
        number(item.get("realtime"), "worst realtime", minimum=visit["intervals"]["start_realtime"], maximum=visit["intervals"]["end_realtime"])
        previous = number(item.get("milliseconds"), "worst milliseconds", maximum=previous)
        boolean(item.get("warm"), "worst warm classification")
    text(value.get("scope"), "case measurement scope")
    return {"case_key": key, "case_full_name": CASES[key], "ordinary_cycles": cycles,
            "capture_brackets": expected_captures, "visits": len(visits), "rooms": sorted(observed_rooms),
            "state_samples": len(samples), "raw_frames": sum(v["intervals"]["raw_cold_inclusive"]["measured_frames"] for v in visits),
            "warm_frames": sum(v["intervals"]["warm"]["measured_frames"] for v in visits),
            "empty_warm_visits": [v["visit"] for v in visits if not v["intervals"]["warm"]["has_samples"]],
            "worst_interval_milliseconds": worst[0]["milliseconds"], "scope": value["scope"]}


def validate_completion(value, results, history, lower, upper, passed=True):
    require(type(value) is dict and type(value.get("schema_version")) is int and value["schema_version"] == 1 and
            value.get("test_profile") == PROFILE, "Completion schema/profile differs")
    exact_cases(value.get("expected_cases"))
    exact_cases(value.get("case_names"))
    require(type(value.get("total")) is int and value["total"] == 2, "Completion total differs")
    result_counts = counts(value, passed)
    require(history["run_finished"] and result_counts == results["counts"] and
            all(history["events"][-1].get(key) == count for key, count in result_counts.items()),
            "Completion/XML/callback counters differ")
    require(value.get("results_sha256") == results["sha256"] and value.get("events_sha256") == history["sha256"],
            "Completion artifact hashes differ")
    complete = boolean(value.get("evidence_complete"), "case evidence complete")
    expected_exit = 0 if result_counts["passed"] == 2 and complete else 1
    require(type(value.get("exit_code")) is int and value["exit_code"] == expected_exit, "Requested exit code differs")
    if passed:
        require(complete and expected_exit == 0, "Case evidence did not qualify")
    fresh(value.get("recorded_utc"), lower, upper)
    number(value.get("duration_seconds"), "completion duration")
    files = value.get("case_evidence")
    require(type(files) is list and len(files) <= 2, "Unbounded case evidence list")
    keys = set()
    for item in files:
        require(type(item) is dict and set(item) == {"case_key", "ready_file", "ready_sha256", "evidence_file", "evidence_sha256", "valid"},
                "Completion evidence binding DTO differs")
        key = item["case_key"]
        require(key in CASES and key not in keys, "Duplicate/foreign completion evidence key")
        keys.add(key)
        require(item["ready_file"] == "case-" + key + "-ready.json" and item["evidence_file"] == "case-" + key + ".json",
                "Completion evidence filename differs")
        for field in ("ready_sha256", "evidence_sha256"):
            require(type(item[field]) is str and re.fullmatch(r"[0-9a-f]{64}", item[field]), "Invalid evidence SHA256")
        boolean(item["valid"], "closed case validity")
        if passed:
            require(item["valid"], "Closed case is not valid")
    require(not complete or keys == set(CASES) and all(item["valid"] for item in files), "Evidence-complete flag differs")
    return value
