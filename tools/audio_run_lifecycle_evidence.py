"""Pure strict verifier for the optional rendered-route audio lifecycle companion.

validate_output(output, pid, started_utc, completed_utc, events_raw=None) reads
only three closed companion files and the original UTF event bytes. It never
loads Win32/COM, runs a player, changes audio settings or treats an empty actual
configuration callback history as recovery. Original 23 route gates are separate.
"""
import datetime as dt
import hashlib
import json
import math
import re
import stat
from pathlib import Path

PROFILE = "rendered-routes-audio-lifecycle"
CASES = (
    "ShadowsOfTheForsaken.Tests.PlayMode.FullCastleRouteTests.CastleMainRouteCompletesThroughControlsWithoutSecret",
    "ShadowsOfTheForsaken.Tests.PlayMode.FullCastleRouteTests.CastleSecretRouteReturnsThroughThroneAndStillRequiresFinalFight",
)
FILES = ("audio-run-lifecycle.jsonl", "audio-run-lifecycle-prequit.json", "audio-run-lifecycle-completion.json")
LIMIT = 512 * 1024


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def integer(value, name, lower=0, upper=2**63 - 1):
    require(type(value) is int and lower <= value <= upper, "Invalid " + name)
    return value


def utc(value):
    require(type(value) is str, "Missing UTC timestamp")
    match = re.fullmatch(r"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(Z|\+00:00)", value)
    require(match is not None, "Audio lifecycle timestamp must be UTC")
    base = dt.datetime.fromisoformat(match[1] + "+00:00")
    return int(base.timestamp()) * 10_000_000 + int((match[2] or "").ljust(7, "0"))


def pairs(values):
    result = {}
    for key, value in values:
        require(key not in result, "Duplicate JSON field: " + key)
        result[key] = value
    return result


def parse(raw):
    require(type(raw) is bytes and 0 < len(raw) <= LIMIT, "Missing/oversized companion evidence")
    return json.loads(raw.decode("utf-8-sig"), object_pairs_hook=pairs,
                      parse_constant=lambda value: (_ for _ in ()).throw(AssertionError("Nonfinite JSON " + value)))


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def validate(raw, pid, output, started_utc, completed_utc, events_raw):
    """Validate closed bytes; useful for meaningful forged-evidence regressions."""
    require(type(raw) is dict and set(raw) == set(FILES), "Exact three companion files required")
    require(type(pid) is int and pid > 0, "Invalid actual PID")
    start, end = utc(started_utc), utc(completed_utc)
    require(start <= end, "Runtime UTC ran backwards")
    journal_raw, pre_raw, terminal_raw = (raw[name] for name in FILES)
    pre, terminal = parse(pre_raw), parse(terminal_raw)
    require(type(journal_raw) is bytes and 0 < len(journal_raw) <= LIMIT and journal_raw.endswith(b"\n"), "Missing/truncated journal")
    lines = journal_raw.splitlines(keepends=True)
    require(len(lines) == 13 and all(line.endswith(b"\n") and len(line) <= 4096 for line in lines), "Incomplete/bounded lifecycle journal required")
    rows = [parse(line) for line in lines]
    require(type(pre) is dict and type(terminal) is dict, "Missing lifecycle objects")
    run_id = terminal.get("run_id")
    require(type(run_id) is str and re.fullmatch(r"[0-9a-f]{32}", run_id), "Missing owned run identity")
    main = integer(terminal.get("main_thread_id"), "main thread", 1, 2**31 - 1)
    frequency = integer(terminal.get("stopwatch_frequency"), "clock frequency", 1)
    entity = integer(terminal.get("exit_entity_id"), "exit entity", 1, 2**64 - 1)
    for item in (pre, terminal, *rows):
        require(type(item) is dict and type(item.get("schema_version")) is int and item["schema_version"] == 1 and
                item.get("profile") == PROFILE and type(item.get("pid")) is int and item["pid"] == pid and item.get("run_id") == run_id,
                "Companion profile/process/run binding differs")
    for item in (pre, terminal):
        require(type(item.get("output_directory")) is str and Path(item["output_directory"]).is_absolute() and
                Path(item["output_directory"]).resolve() == Path(output).resolve(), "Companion output escaped this run")
        require(type(item.get("main_thread_id")) is int and item["main_thread_id"] == main and
                type(item.get("stopwatch_frequency")) is int and item["stopwatch_frequency"] == frequency and
                type(item.get("exit_entity_id")) is int and item["exit_entity_id"] == entity, "Companion owner/clock differs")
        require(item.get("errors") == [], "Companion failure evidence is not empty")
    expected = ["observer-began", "run-started", "test-started", "test-finished", "test-started", "test-finished",
                "run-finished", "quit-intent", "audio-closed", "wants-to-quit", "application-quit", "destroyed"]
    require([row.get("kind") for row in rows if row.get("kind") != "exit-attached"] == expected and
            sum(row.get("kind") == "exit-attached" for row in rows) == 1, "Lifecycle identity/order differs")
    attached = next(index for index, row in enumerate(rows) if row["kind"] == "exit-attached")
    quit_index = next(index for index, row in enumerate(rows) if row["kind"] == "quit-intent")
    close_index = quit_index + 1
    require(0 < attached < quit_index, "Missing early exact exit attachment")
    previous_stamp, previous_clock, previous_frame = start, 0, 0
    for index, row in enumerate(rows):
        require(type(row.get("sequence")) is int and row["sequence"] == index and
                type(row.get("phase_token")) is int and row["phase_token"] == index, "Journal sequence/phase differs")
        stamp, clock = utc(row.get("utc")), integer(row.get("clock_ticks"), "journal clock", 1)
        require(previous_stamp <= stamp <= end and previous_clock <= clock, "Journal clocks escaped/backtracked")
        require(type(row.get("thread_id")) is int and row["thread_id"] == main, "Journal was not authored on actual main thread")
        require(type(row.get("exit_entity_id")) is int and row["exit_entity_id"] == (entity if index >= attached else 0), "Exit identity changed")
        require(type(row.get("quit_allowed")) is bool and row["quit_allowed"] is (row["kind"] == "wants-to-quit"), "Quit decision was not explicit true")
        require(type(row.get("is_suite")) is bool, "Missing original suite boolean")
        if index > close_index:
            require(type(row.get("frame")) is int and row["frame"] == -1, "Teardown must contain managed scalars only")
        else:
            frame = integer(row.get("frame"), "ordinary marker frame", 0, 2**31 - 1)
            require(frame >= previous_frame, "Marker frame ran backwards")
            previous_frame = frame
        previous_stamp, previous_clock = stamp, clock
    actual_events = [parse(line) for line in events_raw.splitlines() if line.strip()]
    own_events = [row for row in rows if row["kind"] in ("run-started", "test-started", "test-finished", "run-finished")]
    require(len(actual_events) == len(own_events) == 6, "Missing exact original six UTF tokens")
    require([item.get("kind") for item in actual_events] == ["run-started", "test-started", "test-finished", "test-started", "test-finished", "run-finished"], "Original UTF event order differs")
    require(all(type(item.get("id")) is str and bool(item["id"]) and type(item.get("full_name")) is str and bool(item["full_name"]) and
                type(item.get("is_suite")) is bool and item["is_suite"] is (index in (0, 5)) and
                item.get("result") == ("Passed" if index in (2, 4, 5) else "") for index, item in enumerate(actual_events)), "Original UTF identity/result booleans differ")
    require(actual_events[0]["id"] == actual_events[5]["id"] and actual_events[0]["full_name"] == actual_events[5]["full_name"] and
            actual_events[1]["id"] == actual_events[2]["id"] and actual_events[3]["id"] == actual_events[4]["id"] and
            len({actual_events[0]["id"], actual_events[1]["id"], actual_events[3]["id"]}) == 3, "Original UTF case/suite identity differs")
    original_stamps = [utc(item.get("utc")) for item in actual_events]
    require(start <= original_stamps[0] <= original_stamps[-1] <= end and original_stamps == sorted(original_stamps), "Original UTF UTC escaped/backtracked")
    require([item["full_name"] for item in actual_events if item["kind"] == "test-started"] == list(CASES), "Unexpected two-route cases/order")
    for index, (own, actual) in enumerate(zip(own_events, actual_events)):
        for left, right in (("kind", "kind"), ("original_id", "id"), ("full_name", "full_name"), ("is_suite", "is_suite"), ("result", "result")):
            require(own.get(left) == actual.get(right) and type(own.get(left)) is type(actual.get(right)), "Original UTF callback token differs: " + left)
        lower = start if index == 0 else utc(actual_events[index - 1]["utc"])
        upper = end if index == 5 else utc(actual_events[index + 1]["utc"])
        require(lower <= utc(own["utc"]) <= upper, "Companion callback escaped original adjacent UTF boundaries")
    require(utc(rows[0]["utc"]) <= utc(actual_events[1]["utc"]), "Observer missed initial leaf case")
    require(all(row["result"] == "Passed" for row in own_events if row["kind"] in ("test-finished", "run-finished")), "Original run/cases did not pass")
    for key in ("prequit_observed", "audio_closed"):
        require(terminal.get(key) is True, "Missing completion: " + key)
    for key in ("subscribed", "wants_subscribed"):
        require(terminal.get(key) is False, "Live completion subscription: " + key)
    for key in ("exit_attach_count", "exit_detach_count", "wants_subscribe_count", "wants_detach_count", "wants_to_quit_count", "application_quit_count", "on_destroy_count"):
        require(type(terminal.get(key)) is int and terminal[key] == 1, "Exactly one managed lifecycle event required: " + key)
    require(terminal.get("prequit_sha256") == digest(pre_raw), "Pre-quit evidence hash differs")
    prefix = b"".join(lines[:close_index + 1])
    for item, body, count in ((pre, prefix, close_index + 1), (terminal, journal_raw, len(rows))):
        require(item.get("journal_sha256") == digest(body) and type(item.get("journal_bytes")) is int and item["journal_bytes"] == len(body) and
                type(item.get("journal_records")) is int and item["journal_records"] == count, "Journal complete-prefix/hash/count differs")
    for key, wanted in (("is_editor", False), ("counter_overflow", False), ("accepting_callbacks", False), ("subscribed", False)):
        require(pre.get(key) is wanted, "Pre-quit safety flag differs: " + key)
    for key, wanted in (("subscribe_attempts", 1), ("unsubscribe_attempts", 1), ("queue_capacity", 128), ("record_capacity", 256)):
        require(type(pre.get(key)) is int and pre[key] == wanted, "Pre-quit lease/bounds differ: " + key)

    def configuration(item):
        require(type(item) is dict and type(item.get("readThreadId")) is int and item["readThreadId"] == main, "Configuration reader thread differs")
        stamp, clock = utc(item.get("readUtc")), integer(item.get("readClockTicks"), "configuration clock", 1)
        require(utc(rows[0]["utc"]) <= stamp <= utc(rows[close_index]["utc"]) and rows[0]["clock_ticks"] <= clock <= rows[close_index]["clock_ticks"], "Configuration read escaped ordinary pre-quit lease")
        for name, lower, upper in (("sampleRate", 8000, 384000), ("dspBufferSize", 1, 65536), ("numRealVoices", 1, 1000000), ("numVirtualVoices", 1, 1000000), ("frame", 0, 2**31 - 1), ("sceneHandle", 1, 2**64 - 1)):
            integer(item.get(name), name, lower, upper)
        require(type(item.get("scenePath")) is str and item.get("sceneLoaded") is True, "Configuration owner scene invalid")
        require(item.get("speakerMode") in ("Raw", "Mono", "Stereo", "Quad", "Surround", "Mode5point1", "Mode7point1", "Prologic"), "Invalid actual speaker mode")
        return stamp, clock

    initial, final = pre.get("initial"), pre.get("final")
    initial_utc, initial_clock = configuration(initial)
    final_utc, final_clock = configuration(final)
    require(initial_utc <= utc(own_events[0]["utc"]) and initial_clock <= own_events[0]["clock_ticks"] and
            utc(rows[quit_index]["utc"]) <= final_utc and rows[quit_index]["clock_ticks"] <= final_clock,
            "Initial/final configuration missed run-start/pre-quit boundary")
    require(rows[0]["frame"] <= initial["frame"] <= own_events[0]["frame"] and
            rows[quit_index]["frame"] <= final["frame"] <= rows[close_index]["frame"], "Configuration frame escaped ordinary boundary")
    require(initial["sceneHandle"] == final["sceneHandle"] and initial["scenePath"] == final["scenePath"] and initial["frame"] <= final["frame"], "Observer scene/frame identity changed")
    retained = pre.get("retained")
    require(type(retained) is list and len(retained) <= 256, "Unbounded audio callbacks")
    counts = {key: integer(pre.get(key), key) for key in ("callback_count", "queue_drops", "record_drops", "pending_callbacks")}
    require(counts["callback_count"] == len(retained) + counts["queue_drops"] + counts["record_drops"] + counts["pending_callbacks"] and
            counts["queue_drops"] == counts["record_drops"] == counts["pending_callbacks"] == 0, "Audio callback loss/pending/accounting differs")
    previous_read = initial_clock
    for index, item in enumerate(retained, 1):
        require(type(item) is dict and type(item.get("sequence")) is int and item["sequence"] == index and type(item.get("deviceWasChanged")) is bool, "Callback identity/device boolean differs")
        integer(item.get("callbackThreadId"), "callback thread", 1, 2**31 - 1)
        callback_phase = integer(item.get("callbackPhaseToken"), "callback phase", 0, quit_index)
        drain_phase = integer(item.get("drainedPhaseToken"), "drain phase", callback_phase, quit_index)
        callback_utc = utc(item.get("callbackUtc"))
        read_utc, read_clock = configuration(item.get("configuration"))
        callback_clock = integer(item.get("callbackClockTicks"), "callback clock", rows[0]["clock_ticks"], read_clock)
        require(utc(rows[0]["utc"]) <= callback_utc <= read_utc <= final_utc and
                initial_utc <= read_utc and utc(rows[drain_phase]["utc"]) <= read_utc and
                previous_read <= read_clock <= final_clock and rows[drain_phase]["clock_ticks"] <= read_clock, "Callback/drain time ordering differs")
        require(item["configuration"]["sceneHandle"] == initial["sceneHandle"] and item["configuration"]["scenePath"] == initial["scenePath"], "Callback drain belongs to another scene")
        require(initial["frame"] <= item["configuration"]["frame"] <= final["frame"] and
                rows[drain_phase]["frame"] <= item["configuration"]["frame"], "Callback drain frame predates phase or escaped finalread")
        delay = item.get("observedDelaySeconds")
        require(type(delay) in (int, float) and math.isfinite(delay) and delay >= 0 and
                math.isclose(delay, (read_clock - callback_clock) / frequency, rel_tol=1e-9, abs_tol=1e-9), "Callback delay does not match actual clocks")
        previous_read = read_clock
    return dict(profile=PROFILE, pid=pid, run_id=run_id, callback_count=counts["callback_count"], marker_count=len(rows),
                observer_began_utc=rows[0]["utc"], prequit_utc=rows[quit_index]["utc"], on_destroy_utc=rows[-1]["utc"],
                main_thread_id=main, exit_entity_id=entity, initial_configuration=initial, final_configuration=final,
                original_events_sha256=digest(events_raw),
                files={name: dict(bytes=len(data), sha256=digest(data)) for name, data in raw.items()},
                scope="Actual managed lifecycle callbacks and later main-thread configuration only; zero configuration events is valid. No selected stream, continuous audio, recovery, audibility or completed native teardown is established.")


def read_closed(path):
    """Bound the actual read as well as its preflight, including rereads."""
    path = Path(path)
    for parent in (path, *path.parents):
        require(not parent.is_symlink(), "Linked lifecycle evidence")
        if parent.exists():
            require(not getattr(parent.stat(), "st_file_attributes", 0) & 0x400, "Reparse lifecycle evidence")
    info = path.stat()
    require(stat.S_ISREG(info.st_mode) and 0 < info.st_size <= LIMIT, "Missing/oversized regular lifecycle file")
    with path.open("rb") as stream:
        raw = stream.read(LIMIT + 1)
    require(0 < len(raw) <= LIMIT, "Lifecycle file grew beyond its bounded read")
    return raw


def validate_output(output, pid, started_utc, completed_utc, events_raw=None):
    output = Path(output).absolute()
    raw = {name: read_closed(output / name) for name in FILES}
    own_events = events_raw is None
    if events_raw is None:
        events_raw = read_closed(output / "events.jsonl")
    require(type(events_raw) is bytes and 0 < len(events_raw) <= LIMIT, "Missing/oversized original callbacks")
    result = validate(raw, pid, output, started_utc, completed_utc, events_raw)
    require(all(read_closed(output / name) == body for name, body in raw.items()), "Lifecycle evidence changed during validation")
    require(not own_events or read_closed(output / "events.jsonl") == events_raw, "Original callbacks changed during validation")
    return result
