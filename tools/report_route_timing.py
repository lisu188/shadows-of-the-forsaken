"""Report recorded route stages; never run the game or certify human pacing.

Reads unchanged PlayerValidationEvidence files and explicit, hash-bound run
metadata. The synthetic unit tests exercise this reader, not Unity gameplay.
"""
import argparse
from datetime import datetime
import hashlib
import json
import math
from pathlib import Path
import re
import sys
from uuid import UUID

ROOT = Path(__file__).resolve().parents[1]
OBJECTIVES = dict(zip(("FirstEnemyDefeated", "MainPuzzleSolved", "MinibossDefeated",
                       "LibraryOpened", "SecretLeverPulled", "BonusDiscovered",
                       "FinalEnemyDefeated"), (1, 2, 4, 8, 16, 32, 64)))
EXCLUSIONS = {"Warmup", "Unfocused", "Paused", "NotRunning", "Transition", "Invalid"}
MAX_JSON = 1024 * 1024
MAX_LOG = 8 * MAX_JSON
MAX_ARCHIVE = 64 * MAX_JSON


class EvidenceError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise EvidenceError(message)


def number(value, name, integer=False):
    require(type(value) in ((int,) if integer else (int, float)) and
            math.isfinite(value) and value >= 0, "Invalid " + name)
    return value


def utc(value):
    try:
        result = datetime.fromisoformat(value.replace("Z", "+00:00"))
        require(result.tzinfo is not None, "UTC timestamp needs an offset")
        return result.timestamp()
    except (AttributeError, TypeError, ValueError) as error:
        raise EvidenceError("Invalid UTC timestamp") from error


def decode(raw):
    def pairs(values):
        result = {}
        for key, value in values:
            require(key not in result, "Duplicate JSON key: " + key)
            result[key] = value
        return result
    def nonfinite(_):
        raise EvidenceError("Nonfinite JSON number")
    return json.loads(raw.decode("utf-8-sig"), object_pairs_hook=pairs,
                      parse_constant=nonfinite)


def bounded(path, limit):
    require(path.is_file() and not path.is_symlink(), "Missing or unsafe file: " + path.name)
    require(path.stat().st_size <= limit, "Evidence exceeds size limit: " + path.name)
    with path.open("rb") as stream:
        raw = stream.read(limit + 1)
    require(len(raw) <= limit, "Evidence grew beyond size limit: " + path.name)
    return raw


def reference(base, item, limit, archive=False):
    require(isinstance(item, dict), "Missing file reference")
    name = item.get("path", "")
    require(isinstance(name, str) and name and not Path(name).is_absolute(), "Use a relative evidence path")
    path = base / name
    require(path.resolve().is_relative_to(base.resolve()), "Evidence path escapes the manifest directory")
    expected = item.get("sha256", "")
    require(isinstance(expected, str) and re.fullmatch(r"[0-9a-f]{64}", expected), "Invalid SHA-256 reference")
    if archive:
        require(path.is_file() and not path.is_symlink() and path.stat().st_size <= limit,
                "Missing, unsafe or oversized source archive")
        digest = hashlib.sha256()
        size = 0
        with path.open("rb") as stream:
            while chunk := stream.read(65536):
                size += len(chunk)
                require(size <= limit, "Source archive grew beyond size limit")
                digest.update(chunk)
        require(digest.hexdigest() == expected, "SHA-256 mismatch: " + name)
        return None
    raw = bounded(path, limit)
    require(hashlib.sha256(raw).hexdigest() == expected, "SHA-256 mismatch: " + name)
    return raw


def jsonl(raw, maximum):
    lines = raw.decode("utf-8-sig").splitlines()
    require(0 < len(lines) <= maximum, "Missing or excessive JSONL records")
    require(all(line.strip() for line in lines), "Blank JSONL record")
    result = [decode(line.encode()) for line in lines]
    require(all(isinstance(row, dict) for row in result), "JSONL records must be objects")
    return result


def model():
    raw = bounded(ROOT / "docs/level-contract.json", MAX_JSON)
    contract = decode(raw)
    name = lambda text: "".join(word.capitalize() for word in text.split("_"))
    mask = lambda values: sum(OBJECTIVES[name(value)] for value in values)
    rooms = {name(room["id"]): mask(room["requires"]) for room in contract["rooms"]}
    objectives = {name(event["id"]): (name(event["room"]), mask(event["requires"]))
                  for event in contract["events"]}
    passages = {frozenset((name(edge["from"]), name(edge["to"]))): mask(edge["requires"])
                for edge in contract["connections"]}
    return rooms, objectives, passages, mask(contract["required_events"]), hashlib.sha256(raw).hexdigest()


def stage(room, flags, entered_first):
    if room == "Courtyard" and not entered_first:
        return "calm_introduction"
    if room in {"FirstEncounter", "ThroneRoom", "FinalArena"}:
        objective = {"FirstEncounter": 1, "ThroneRoom": 4, "FinalArena": 64}[room]
        if not flags & objective:
            return "encounter_room_before_objective"
    if (room == "Puzzle" and not flags & 2) or (room == "Library" and not flags & 8):
        return "mechanism_room_before_objective"
    if room == "BonusRoom":
        return "optional_room"
    return "other_traversal_or_exploration"


def initialization_prefix(events, rows, session_id):
    """Recognize only the observer-before-session Start ordering, never a replay."""
    if events[0].get("phase") != "Resetting":
        return events, rows, {"observed": False}
    require(len(events) >= 3 and len(rows) >= 1, "Incomplete initialization prefix")
    started, reset, running = events[:3]
    initial_id = started.get("sessionId")
    require(str(UUID(initial_id)) == initial_id and UUID(initial_id).int != 0 and initial_id != session_id,
            "Invalid initialization session identity")
    require(started.get("kind") == "started" and started.get("beforeSessionId") == initial_id and
            reset.get("kind") == "progression" and reset.get("change") == "SessionReset" and
            reset.get("phase") == "Resetting" and reset.get("sessionId") == session_id and
            reset.get("beforeSessionId") == initial_id and running.get("kind") == "session" and
            running.get("phase") == "Running" and running.get("sessionId") == session_id and
            running.get("beforeSessionId") == session_id, "Not the exact first-session initialization prefix")
    for event in events[:3]:
        require(event.get("elapsedSeconds") == 0 and event.get("objectiveFlags") == 0 and
                event.get("room") == event.get("beforeRoom") == "Courtyard" and event.get("objective") == "None",
                "Initialization prefix contains elapsed gameplay or progress")
        number(event.get("sequence"), "initialization sequence", True)
    require(started["sequence"] < reset["sequence"] < running["sequence"] and
            utc(started.get("utc")) <= utc(reset.get("utc")) <= utc(running.get("utc")),
            "Reordered initialization prefix")
    summary = rows[0]
    stats = summary.get("frameIntervals", {})
    require(summary.get("reason") == "reset" and summary.get("sessionId") == initial_id and
            utc(started["utc"]) <= utc(summary.get("utc")) <= utc(reset["utc"]),
            "Missing matching initialization reset summary")
    require(all(stats.get(key) == 0 for key in ("observedFrames", "measuredFrames", "measuredSeconds",
                                               "warmupObservedEligibleSeconds")) and stats.get("hasSamples") is False and
            stats.get("histogram") == [0] * 2049, "Initialization reset contains frame observations")
    excluded = stats.get("excluded", [])
    require(len(excluded) == len(EXCLUSIONS) and {item.get("reason") for item in excluded} == EXCLUSIONS and
            all(item.get("frames") == 0 and item.get("milliseconds") == 0 for item in excluded),
            "Initialization reset contains exclusions or incomplete counters")
    require(len(rows) > 1 and summary.get("hardware") == rows[1].get("hardware"),
            "Initialization runtime identity changed")
    return events[2:], rows[1:], dict(observed=True, initial_session_id=initial_id,
                                     running_start_sequence=running["sequence"], prefix_event_count=3,
                                     empty_reset_summary_count=1)


def route_events(events, session_id, route, initialized_start=False):
    rooms, objectives, passages, required, contract_hash = model()
    require(events[0].get("kind") == ("session" if initialized_start else "started") and
            events[-1].get("kind") == "normal-exit",
            "Need one start and a normal exit, not a partial or observer-disabled trace")
    require(events[0].get("elapsedSeconds") == 0, "Observer started after the session clock")
    room, flags, previous_time, previous_sequence = "Courtyard", 0, 0, 0
    previous_utc = utc(events[0].get("utc"))
    entered_first, completion, exit_event = False, None, None
    segments = []
    for index, event in enumerate(events):
        require(event.get("sessionId") == session_id and event.get("beforeSessionId") == session_id,
                "Mixed, reset or stitched session tokens")
        elapsed = number(event.get("elapsedSeconds"), "elapsedSeconds")
        sequence = number(event.get("sequence"), "sequence", True)
        timestamp = utc(event.get("utc"))
        require(sequence > previous_sequence and elapsed >= previous_time and timestamp >= previous_utc,
                "Reordered, duplicate or backwards event sequence/time")
        require(event.get("room") in rooms and event.get("beforeRoom") in rooms, "Unknown room")
        require(event.get("phase") in {"Running", "Completed"}, "Defeated, resetting or unknown session phase")
        number(event.get("objectiveFlags"), "objectiveFlags", True)
        # This real Running event is the clock origin after the verified
        # zero-observation initialization prefix. No raw event is rewritten.
        kind = "started" if initialized_start and index == 0 else event.get("kind")
        if completion is not None and exit_event is None:
            require(kind == "progression" and event.get("change") == "RoomEntered" and
                    event["room"] == "Exit" and elapsed == completion["elapsedSeconds"],
                    "Completion was not followed by its Exit progression callback")
        if kind == "progression":
            require(exit_event is None, "Progression after exit")
            require(event.get("beforeRoom") == room, "Broken room continuity")
            if elapsed > previous_time:
                segments.append(dict(room=room, category=stage(room, flags, entered_first),
                                     start_seconds=previous_time, end_seconds=elapsed,
                                     seconds=elapsed - previous_time,
                                     end_change=event.get("change"), end_objective=event.get("objective")))
            if event.get("change") == "RoomEntered":
                destination = event["room"]
                key = frozenset((room, destination))
                require(destination != room and key in passages, "Invalid room connection")
                needed = passages[key] | rooms[room] | rooms[destination]
                require(flags & needed == needed, "Room entered before prerequisites")
                require(event.get("objective") == "None", "Room entry carries an objective")
                room = destination
                entered_first |= room == "FirstEncounter"
                if room == "Exit":
                    require(flags & required == required, "Premature exit")
                    exit_event = event
            elif event.get("change") == "ObjectiveCompleted":
                objective = event.get("objective")
                require(objective in objectives and objective in OBJECTIVES, "Unknown objective")
                expected_room, needed = objectives[objective]
                bit = OBJECTIVES[objective]
                require(event["room"] == room == expected_room and flags & needed == needed and not flags & bit,
                        "Missing prerequisite, duplicate objective or incorrect objective room")
                flags |= bit
            else:
                raise EvidenceError("Reset or unknown progression event")
            require(event["objectiveFlags"] == flags, "Objective flags disagree with recorded progression")
            require(event["phase"] == ("Completed" if room == "Exit" else "Running"), "Unexpected progression phase")
        elif kind == "session":
            # Session's Changed listener runs before the observer's RoomEntered
            # callback on Exit. Its snapshot already points at Exit.
            require(completion is None and event["phase"] == "Completed" and
                    event["room"] == event["beforeRoom"] == "Exit" and flags & required == required and
                    event["objectiveFlags"] == flags and event.get("objective") == "None",
                    "Duplicate or inconsistent completion lifecycle event")
            completion = event
        elif kind in {"started", "normal-exit"}:
            require((kind == "started" and index == 0) or (kind == "normal-exit" and index == len(events) - 1),
                    "Repeated start or exit; concatenated runs are not accepted")
            require(event["room"] == event["beforeRoom"] == room and event["objectiveFlags"] == flags and
                    event.get("objective") == "None", "Lifecycle snapshot disagrees with progression")
            require(event["phase"] == ("Running" if kind == "started" else "Completed"),
                    "Normal exit before route completion")
        else:
            raise EvidenceError("Unknown or interrupted lifecycle event")
        previous_sequence, previous_utc = sequence, timestamp
        # The completion notification can precede the final room event at the
        # same clock value; do not lose the last traversal interval.
        if kind != "session":
            previous_time = elapsed
    require(completion is not None and exit_event is not None, "Missing completion or exit progression")
    total = exit_event["elapsedSeconds"]
    require(total > 0 and completion["elapsedSeconds"] == total == events[-1]["elapsedSeconds"],
            "Completion/exit clocks disagree")
    require(abs(sum(part["seconds"] for part in segments) - total) < .001, "Segment coverage is incomplete")
    require((route == "basic" and flags == required) or (route == "secret" and flags == 127),
            "Declared route disagrees with optional objectives")
    # Session time is the source of segment durations. A wall-clock discrepancy
    # is only an exclusion signal, never substituted for playable time.
    wall = utc(completion["utc"]) - utc(events[0]["utc"])
    require(abs(wall - total) <= 1, "Wall/session clock gap: interrupted or ambiguous timing")
    return dict(seconds=total, objective_flags=flags, segments=segments, completion=completion,
                contract_sha256=contract_hash)


def performance(rows, session_id, source, completion):
    require(len(rows) == 2 and [row.get("reason") for row in rows] == ["completion", "normal-exit"],
            "Need one completion and one normal-exit performance summary; do not combine sessions/checkpoints")
    for row in rows:
        require(row.get("sessionId") == session_id, "Performance belongs to another session")
        stats = row.get("frameIntervals", {})
        frames = number(stats.get("measuredFrames"), "measuredFrames", True)
        require(frames > 0 and stats.get("hasSamples") is True, "No measured frames")
        hist = stats.get("histogram")
        require(isinstance(hist, list) and len(hist) == 2049, "Missing complete frame histogram")
        require(all(type(value) is int and value >= 0 for value in hist) and sum(hist) == frames,
                "Frame histogram count mismatch")
        require(stats.get("histogramBucketMilliseconds") == .25 and stats.get("histogramFiniteBuckets") == 2048 and
                stats.get("warmupRequiredEligibleSeconds") == 5, "Unsupported frame metric format")
        measured = number(stats.get("measuredSeconds"), "measuredSeconds")
        average = number(stats.get("averageMilliseconds"), "averageMilliseconds")
        maximum = number(stats.get("maximumMilliseconds"), "maximumMilliseconds")
        lower = sum(index * .25 * count for index, count in enumerate(hist))
        upper = sum((index + 1) * .25 * count for index, count in enumerate(hist[:-1])) + hist[-1] * maximum
        require(math.isclose(average * frames, measured * 1000, rel_tol=1e-8, abs_tol=1e-6) and
                maximum >= average and lower - 1e-6 <= measured * 1000 <= upper + 1e-6,
                "Frame duration disagrees with counters/histogram")
        excluded = stats.get("excluded", [])
        require(isinstance(excluded, list) and len(excluded) == len(EXCLUSIONS) and
                {item.get("reason") for item in excluded} == EXCLUSIONS, "Missing or duplicate exclusion reasons")
        counts = {}
        for item in excluded:
            counts[item["reason"]] = number(item.get("frames"), "excluded frames", True)
            milliseconds = number(item.get("milliseconds"), "excluded milliseconds")
            require(counts[item["reason"]] > 0 or milliseconds == 0, "Exclusion duration without frames")
        require(number(stats.get("observedFrames"), "observedFrames", True) == frames + sum(counts.values()),
                "Observed frame count mismatch")
        require(not any(counts[reason] for reason in ("Unfocused", "Paused", "Invalid")),
                "Focus loss, pause or invalid frame observations disqualify this timing input")
        require(counts["Transition"] == 1, "Extra or missing frame baseline: interruption cannot be excluded")
        require(number(stats.get("measuredSeconds"), "measuredSeconds") > 0 and
                number(stats.get("warmupObservedEligibleSeconds"), "warmup") >= 5,
                "Incomplete performance observation")
        if row["reason"] == "completion":
            require(counts["NotRunning"] == 0, "Non-running frames before completion")
            observed_seconds = stats["measuredSeconds"] + sum(item["milliseconds"] for item in excluded) / 1000
            require(abs(observed_seconds - completion["elapsedSeconds"]) <= 1,
                    "Performance coverage disagrees with the session duration")
        hardware = row.get("hardware", {})
        require(type(hardware.get("isEditor")) is bool and hardware.get("unityVersion") == source["unity"],
                "Missing or mismatched runtime identity")
        require(number(row.get("screenWidth"), "screenWidth", True) > 0 and
                number(row.get("screenHeight"), "screenHeight", True) > 0, "Missing display configuration")
    require(rows[0]["hardware"] == rows[1]["hardware"], "Hardware identity changed within the run")
    for key in ("measuredFrames", "measuredSeconds", "histogram"):
        require(rows[0]["frameIntervals"][key] == rows[1]["frameIntervals"][key],
                "Gameplay samples changed after completion")
    require(abs(utc(rows[0].get("utc")) - utc(completion["utc"])) <= 1 and
            utc(rows[1].get("utc")) >= utc(rows[0].get("utc")), "Performance lifecycle times disagree")
    return rows[0]


def report_run(path):
    path = Path(path)
    metadata_raw = bounded(path, MAX_JSON)
    metadata = decode(metadata_raw)
    require(type(metadata.get("schema_version")) is int and metadata["schema_version"] == 1,
            "Unsupported timing manifest")
    for key, values in (("input_origin", {"human", "automation"}),
                        ("route_familiarity", {"first_attempt", "repeat", "known_route"}),
                        ("route", {"basic", "secret"})):
        require(metadata.get(key) in values, "Explicit " + key + " is required")
    require(metadata.get("interruptions_observed") is False, "Explicit uninterrupted-run declaration is required")
    require(isinstance(metadata.get("run_id"), str) and re.fullmatch(r"[A-Za-z0-9_.-]{1,100}", metadata["run_id"]),
            "Invalid run_id")
    session_id = metadata.get("session_id")
    require(str(UUID(session_id)) == session_id, "Use a canonical session UUID")
    require(UUID(session_id).int != 0, "Session UUID must not be empty")
    revision = metadata.get("revision", "")
    require(isinstance(revision, str) and re.fullmatch(r"[0-9a-f]{40}", revision), "Explicit full revision is required")
    source = decode(reference(path.parent, metadata.get("build_receipt"), MAX_JSON))
    require(source.get("source_commit") == revision and source.get("result") == "Succeeded" and
            isinstance(source.get("unity"), str) and source["unity"], "Build receipt does not bind the declared revision")
    archive = metadata.get("source_archive", {})
    require(archive.get("sha256") == source.get("source_archive_sha256"), "Build/source archive identities disagree")
    reference(path.parent, archive, MAX_ARCHIVE, archive=True)
    files = metadata.get("files", {})
    events = jsonl(reference(path.parent, files.get("events"), MAX_LOG), 10000)
    rows = jsonl(reference(path.parent, files.get("performance"), MAX_LOG), 256)
    status = decode(reference(path.parent, files.get("status"), MAX_JSON))
    summary_count = len(rows)
    events, rows, initialization = initialization_prefix(events, rows, session_id)
    route = route_events(events, session_id, metadata["route"], initialization["observed"])
    summary = performance(rows, session_id, source, route["completion"])
    require(abs(utc(rows[-1].get("utc")) - utc(events[-1].get("utc"))) <= 1,
            "Normal-exit performance and event times disagree")
    require(status.get("sessionId") == session_id and status.get("phase") == "Completed" and
            status.get("room") == "Exit" and status.get("objectiveFlags") == route["objective_flags"] and
            status.get("elapsedSeconds") == route["seconds"], "Final status does not match this completed route")
    require(number(status.get("sequence"), "status sequence", True) > events[-1]["sequence"] and
            utc(status.get("utc")) >= utc(events[-1]["utc"]), "Final status predates normal exit")
    require(status.get("performanceSummariesWritten") == summary_count and status.get("performanceSummaryLimitReached") is False,
            "Missing or capped performance summaries")
    require(number(status.get("player", {}).get("health"), "final health", True) > 0, "Player did not survive completion")
    require(status.get("hardware") == summary["hardware"], "Status hardware differs from performance evidence")
    require(status.get("performance", {}).get("measuredFrames") == summary["frameIntervals"]["measuredFrames"],
            "Status performance disagrees with lifecycle evidence")
    totals = {}
    for segment in route["segments"]:
        totals[segment["category"]] = totals.get(segment["category"], 0) + segment["seconds"]
    return dict(status="review_input", run_id=metadata["run_id"], session_id=session_id,
                revision=revision, input_origin=metadata["input_origin"], route_familiarity=metadata["route_familiarity"],
                route=metadata["route"], is_editor=summary["hardware"]["isEditor"], seconds=route["seconds"],
                initialization_prefix=initialization,
                stage_totals_seconds=totals, segments=route["segments"], hardware=summary["hardware"],
                display={key: summary.get(key) for key in ("screenWidth", "screenHeight", "fullScreenMode", "qualityLevel", "vSyncCount", "targetFrameRate")},
                provenance=dict(manifest_sha256=hashlib.sha256(metadata_raw).hexdigest(), files=files,
                                build_receipt=metadata["build_receipt"], source_archive=archive,
                                contract_sha256=route["contract_sha256"]),
                human_review_candidate=metadata["input_origin"] == "human" and not summary["hardware"]["isEditor"])


def report(paths):
    require(0 < len(paths) <= 32, "Supply 1-32 run manifests")
    runs = []
    for path in paths:
        try:
            runs.append(report_run(path))
        except (EvidenceError, OSError, ValueError, TypeError, KeyError, AttributeError, OverflowError, RecursionError) as error:
            runs.append(dict(status="rejected", manifest=str(path), reason=str(error)))
    valid = [run for run in runs if run["status"] == "review_input"]
    errors = []
    for key in ("run_id", "session_id"):
        if len({run[key] for run in valid}) != len(valid):
            errors.append("Duplicate " + key + "; repeated evidence is not another attempt")
    hashes = [run["provenance"]["files"]["events"]["sha256"] for run in valid]
    if len(set(hashes)) != len(hashes):
        errors.append("The same event trace was supplied more than once")
    identities = {(run["revision"], run["provenance"]["source_archive"]["sha256"]) for run in valid}
    if len(identities) > 1:
        errors.append("Mixed revisions/source archives cannot form one timing set")
    counts = {origin: {route: 0 for route in ("basic", "secret")} for origin in ("human_standalone", "automation", "human_editor")}
    if not errors:
        for run in valid:
            group = "automation" if run["input_origin"] == "automation" else "human_editor" if run["is_editor"] else "human_standalone"
            counts[group][run["route"]] += 1
    return dict(schema_version=1, status="rejected" if errors or len(valid) != len(runs) else "review_input",
                runs=runs, collection_rejections=errors, distinct_complete_runs=counts,
                acceptance_passed=False,
                limitations=["No 120-180 second compliance claim or human/standalone acceptance is made by this tool.",
                             "Origins, familiarity and associations with build/source receipts are explicit operator declarations, not runtime authentication.",
                             "Stage intervals follow room/objective events; encounter/mechanism intervals include approach, search and waiting. Pure combat/travel time is unavailable.",
                             "The current observer has no explicit focus/pause lifecycle events. Exclusions, extra baseline transitions and wall/session gaps reject observed ambiguity; absence is not proof of uninterrupted attention.",
                             "Aggregate performance identifies no costly room or light/shadow effect; it is used only to qualify recorded timing coverage.",
                             "At least three basic and one secret human runs with declared familiarity are still review inputs, not automatic DOCX acceptance."])


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifests", nargs="+", type=Path)
    parser.add_argument("--output", type=Path, help="Create a new report; existing files are never overwritten")
    args = parser.parse_args(argv)
    try:
        result = report(args.manifests)
        payload = json.dumps(result, indent=2, ensure_ascii=False, allow_nan=False) + "\n"
        if args.output:
            with args.output.open("x", encoding="utf-8") as stream:
                stream.write(payload)
        else:
            print(payload, end="")
        return 0 if result["status"] == "review_input" else 1
    except (EvidenceError, OSError, ValueError) as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
