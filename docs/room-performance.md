# Per-room performance observations — #21 / #24

DOCX §§2, 4 and 5 describe the castle's lighting and visual atmosphere; §§3 and 6–8 describe the short route and its rooms. Room-labelled performance evidence supports reviewing that implementation. The document does not specify frame budgets or a profiling system. The observation format and limits below are implementation choices; they do not qualify the hardware, lighting or final level.

`PlayerValidationEvidence` remains inactive unless `SHADOWS_PLAYER_EVIDENCE_DIR` names an absolute local directory. The existing `performance.jsonl` session summaries, event/timing format and session accumulator are preserved. A separate **`room-performance.jsonl`** contains at most **256 completed visit records**, counting existing lines at startup. Rolling `status.json` adds the room-summary count and limit flag; reaching this cap leaves ordinary session/event/status observation running. The observer never sends input or changes progression, quality, lighting, frame caps, time scale or gameplay state.

Each row stores:

- Schema version, closing reason, UTC start/end, frozen session ID, logical room and visit number. Re-entering a room starts a distinct visit.
- Real-time interval bounds and duration, plus the trailing partial interval between the last observed frame and the closing callback. This tail is **unobserved**, not fabricated as a full frame.
- The existing fixed **2049-bin frame histogram**, measured counts/durations and exclusions, produced by a separate `PlayerFrameMetrics` accumulator for that visit.
- Hardware identity, display/quality/VSync/frame-cap configuration at start and end, and a sticky flag when a configuration difference is observed during the visit. Configuration changes entirely between observations cannot be detected.

A measured interval requires eligible endpoints within the same room/session. The first frame after a room boundary is excluded as `Transition`; changing rooms twice between frames produces an empty visit, not two performance samples. Room baselines are independent of the existing session baseline. Focus/pause callbacks invalidate only their relevant baselines and preserve the room exclusion even if no frames ran while suspended. A callback that finishes a visit retains its original identity even when progression already points at another room or token.

**Warmup is per visit:** the first five eligible seconds, including the crossing frame in full, are excluded using the existing accumulator policy. A short visit can contain only warmup/exclusions and has no measured result. This is not a warmed-session subset; do not treat empty visits or `withinProvisionalFrameBudget` alone as evidence that a room is qualified. Do not combine histograms from different hardware, configurations, source builds, sessions or visit policies without explicit review.

A room change closes the old visit and opens the new one only while the session is Running. Defeat, completion and Resetting close the stored visit before its identity can change. A new session starts observation when Running resumes. Completion arrives before the Exit room callback; it closes the final arena and does not invent a playable Exit visit. Zero-frame startup/reset visits remain visibly empty. Quit and observer-disable close at most once; disabling then re-enabling the component preserves the existing behavior of leaving observation stopped.

Room-file failures use the existing fail-closed observer behavior: a warning identifies the operation and error type/code without dumping a path, then observation unsubscribes and stops. Gameplay continues independently. Rows are written only when a visit closes; an abrupt process kill can leave the current visit absent. Retain logs and do not infer a completed visit from missing output.

These are real-time **frame interval observations**, not CPU/GPU profiler timings or attribution to individual lights, shadows, shaders or draw calls. Room labels are the existing logical progression rooms, not a new map/trigger system. Frame rate depends on hardware, workload, focus and configuration; unchanged session logs retain their original interpretation. Standalone testing, first-time human route/pacing acceptance and hardware-specific qualification remain deferred/pending.

## Validation scope

The separate dependency-free room accumulator is exercised by `tests/Performance/Performance.Tests.csproj` together with the production `PlayerFrameMetrics.cs`; this .NET project does not compile or simulate MonoBehaviour. Cases cover frozen identity, foreign-token/room rejection, warmup/histogram accounting, suspension without frames, empty visits, trailing intervals and invalid clocks. The existing shared C# workflow now runs it with the same pinned NUnit dependencies.

`PlayerValidationPerformanceTests` adds eight Unity PlayMode regressions for room transitions, defeat/restart/quit, external reset ordering, stale callback rejection, output cap, disable lifecycle, write failure and configuration changes. Existing actual-frame and completion cases additionally check per-room exclusions and final-arena attribution. Room commit `8bb24ff` passed **84 Python tests, 7 room-accumulator .NET cases and 29 progression .NET cases**, together with the design-contract check. After rebase onto merged PR #35, all compiled C# sources, test sources, project files and the progression contract remain byte-identical to that tested commit; the retained **7/7 and 29/29** results are reused rather than reported as a new execution. The post-rebase source/design checks and **142 Python tests passed** (20.817 s). The strict catalog now requires **173 EditMode and 202 PlayMode cases**, including the 15-case observer fixture. The historical 193-case camera fixture still excludes both the later zero-aim guard and these eight room cases. The .NET and Python results alone do not validate engine integration.

On Unity **6000.6.3f1**, source `8cdba019` passed **173/173 EditMode10** (exit 0; 153.25 s runner / 3.0085722 s XML) and **202/202 PlayMode44** (exit 0; 619.03 s runner / 540.658514 s XML), including all 15 observer cases. Both runs retained **662 unchanged native inputs** and a **64-file / 249,232-byte frozen delta**. The [validation report](validation/room-performance-2026-10-01.md) also retains **PlayMode43: 200/202**: `CastleLockedMainGatesRejectWalkingAndJumping` and `CastleSecretRouteReturnsThroughThroneAndStillRequiresFinalFight` exceeded the unchanged 30-second scene-load deadline. All 15 observer cases passed in that attempt. PlayMode44 repeated the full suite without source, deadline or assertion changes; it does not erase the earlier failure or establish its cause. **Windows build7 succeeded** (exit 0, no stop condition; 158.17 s runner). Unity reports **0 errors, 0 warnings, 133,699,472 bytes** and build duration **`00:01:30.5031540`**. Its 662 inputs remained unchanged, with the same 64-file / 249,232-byte frozen delta. All 198 engine files (133,699,472 bytes) and the separate BUILD-INFO receipt are verified against the tested source. The player remains unlaunched.

Run locally:

```sh
dotnet test tests/Performance/Performance.Tests.csproj --configuration Release
dotnet test tests/Progression/Progression.Tests.csproj --configuration Release
python3 tools/validate_level_contract.py
python3 -m unittest discover -s tests -p 'test_*.py' -v
python3 tools/unity_validation.py project
```

The native suites validate observer compilation and integration within their recorded input scope. A standalone evidence capture still requires the separately coordinated player-validation step; automated fixture success does not establish hardware qualification, lighting-cost attribution or final #21/#24 acceptance.
