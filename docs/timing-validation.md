# Offline route timing reports

`tools/report_route_timing.py` supports issue #23 and DOCX §3 by reading existing
`PlayerValidationEvidence` logs. It sends no input, launches no game, changes no
Unity source, and preserves raw evidence. Standalone testing remains deferred
under the owner's instruction to continue implementation and test later.

The output is **input for review**, never a declaration that the 120–180 second
target or human acceptance has been achieved. Synthetic parser tests exercise
the reporting tool; they are not timed playthroughs.

## Inputs and invocation

Keep each recorded attempt's original `events.jsonl`, `performance.jsonl` and
final `status.json`. A run manifest supplies the participant's route familiarity,
human versus automated input, route variant and exact session ID. Obtain these
facts from the actual run; do not infer first-time play from a short duration.
Use a separate evidence directory per attempt. A restarted, failed or incomplete
attempt stays preserved separately and cannot be stitched into a completion.

Place the manifest at a common ancestor of its referenced files. All references
are relative, confined to that directory, and SHA-256 checked. No copies of the
source archive or player are needed. Example schema, with deliberately invalid
placeholder hashes and identifiers that must be replaced with actual values:

```json
{
  "schema_version": 1,
  "run_id": "human-basic-01",
  "session_id": "UUID copied from the original observer events",
  "revision": "full 40-character source commit",
  "input_origin": "human",
  "route_familiarity": "first_attempt",
  "route": "basic",
  "interruptions_observed": false,
  "build_receipt": {"path": "player/BUILD-INFO.json", "sha256": "64-character hash"},
  "source_archive": {"path": "source-inputs.zip", "sha256": "64-character hash"},
  "files": {
    "events": {"path": "run-01/events.jsonl", "sha256": "64-character hash"},
    "performance": {"path": "run-01/performance.jsonl", "sha256": "64-character hash"},
    "status": {"path": "run-01/status.json", "sha256": "64-character hash"}
  }
}
```

`input_origin` accepts `human` or `automation`; `route_familiarity` accepts
`first_attempt`, `repeat` or `known_route`; `route` accepts `basic` or `secret`.
The explicit interruption declaration is required in addition to checking the
recorded signals. Missing knowledge must remain unknown, not be set to false to
make a report pass.

The build receipt uses the existing `BUILD-INFO.json` keys `source_commit`,
`source_archive_sha256`, `result: "Succeeded"` and `unity`. The reporter checks
the supplied receipt and archive hashes, commit agreement and recorded runtime
Unity version. This binds the files supplied for review; the current runtime
does not embed build identity, so associating that build with the recorded run
remains an explicit operator declaration. A hash alone does not prove which
executable was launched.

```sh
python3 tools/report_route_timing.py artifacts/timing/run-01.json
python3 tools/report_route_timing.py artifacts/timing/run-01.json artifacts/timing/run-02.json --output artifacts/timing/report.json
python3 -m unittest discover -s tests -p 'test_route_timing.py' -v
```

The command returns 0 for structurally complete review inputs, 1 for rejected or
ambiguous evidence. It never returns an acceptance result: `acceptance_passed`
is always false. `--output` creates a new file exclusively and refuses to
overwrite an existing report or raw log. Without it, JSON goes to stdout.
Collection counts separate human standalone, human editor and automated runs;
three basic and one secret entries are a review method, not automatic approval.
Duplicate runs/sessions/traces and mixed source revisions are rejected as one
timing set. Individual runs retain their own evidence and stated origin.

## What the events can measure

The session clock is focused, unpaused running time. The parser follows the
existing [level contract](level-contract.json), requiring one start at zero,
continuous room/objective progression, all mandatory objectives, a completion
notification, final exit and normal shutdown in the same session. It handles
the actual callback order in which the session completion notification precedes
the Exit room event. It does not replay or simulate the Unity game.

Unity does not order the two components' `Start` methods. When the observer
starts first, the reader recognizes only this exact initialization prefix:
`started/Resetting` on the initial token, `SessionReset/Resetting` from that token
to the declared token, then `session/Running` on the declared token. All three
must be at zero elapsed time with no objectives in the courtyard; the initial
reset performance summary must have zero frame observations and exclusions.
The real Running event supplies the clock origin and the report records the
prefix explicitly. Raw logs are never rewritten. A reset after Running, a
nonzero elapsed time, any observed frame or another token transition is rejected.

Intervals use recorded elapsed times at room entries and completed objectives:

| Category | Supported meaning |
| --- | --- |
| `calm_introduction` | Start until first entry into the first-encounter room. |
| `encounter_room_before_objective` | Time in an encounter room before its objective completes, including approach, movement or waiting there. |
| `mechanism_room_before_objective` | Time in the puzzle/library before the mechanism objective completes, including search or approach. |
| `optional_room` | Time physically attributed to the bonus room. Other optional-branch travel is not inferred. |
| `other_traversal_or_exploration` | Remaining recorded room/objective intervals. |

The segments sum to the recorded route time. These are **not pure combat,
puzzle-solving or movement durations**: existing events do not identify those
boundaries. The rolling status file overwrites previous samples, so it cannot
recover a history that was never retained. Likewise, session performance
summaries cannot identify costly rooms, lights or shadow effects.

## Rejection and evidence limits

The current-format observer's completion and normal-exit performance summaries
must match the session and preserve a nonempty complete histogram. Missing or
capped summaries, death/reset, mixed tokens, duplicate/reordered events, missing
objectives, bad hashes and inconsistent final status are rejected. Older pilot
logs without performance summaries remain incomplete; the tool does not invent
the missing observations. Only the exact empty startup summary described above
is allowed before those two gameplay summaries. Recorded frame durations must
agree with the histogram/counters and cover the session duration.

Observed focus-loss, pause or invalid-frame exclusions disqualify timing input.
More than the initial frame-baseline transition also disqualifies it, including
a suspension without intervening frames. A wall-clock/session-time discrepancy
over one second is treated conservatively as ambiguous, not corrected into a
different gameplay duration. That tolerance is a reporting implementation
choice, not a DOCX requirement. The observer does not emit explicit focus/pause
lifecycle events, and these checks cannot prove uninterrupted human attention.
Human review remains necessary even when all structural checks succeed.

Reads are bounded to 1 MiB per JSON, 8 MiB and 10,000 records for events, 8 MiB
and 256 records for performance, 64 MiB for the streamed source-archive hash,
and 32 manifests per report. No dependencies beyond Python's standard library
are required. The tool performs no cleanup or native test execution.
