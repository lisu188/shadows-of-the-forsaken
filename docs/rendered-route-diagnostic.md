# Concealed rendered-route diagnostic

DOCX §§2–5 and 6–8 describe the castle, its visual load and both routes. Issue #24 asks for hardware, configuration, frame, memory and costly-place evidence. The document does not prescribe a profiler, numeric frame budget or memory limit. This separate opt-in Development/UTF profile supplies local engineering observations; it does not qualify displayed FPS, GPU completion, ordinary Release performance or human play.

The profile selects exactly two existing cases, without adding NUnit route cases or changing their controls, yields, deadlines, gameplay, balance, assertions or captures:

- `FullCastleRouteTests.CastleMainRouteCompletesThroughControlsWithoutSecret`
- `FullCastleRouteTests.CastleSecretRouteReturnsThroughThroneAndStillRequiresFinalFight`

The ten-case native route profile, three-case presentation profile, ordinary startup/hour profiles and delivered Release retain their own evidence. The new support assemblies are test-only and activate only through `SHADOWS_RENDERED_ROUTE_BUILD=1` at build time and `SHADOWS_RENDERED_ROUTE_OUTPUT` at runtime. The launcher clears conflicting profile overrides. Production progression and `PlayerValidationEvidence` are unchanged.

## Observation and limitations

A scene-owned probe binds the case, process, progression session, saved scene handle and camera entity ID. A closed readiness receipt is published after existing initialization and capture-pipeline preparation. The launcher keeps host and child hidden until it verifies that receipt in the same active case, then permits ordinary rendering inside a transparent, fully offscreen, nonactivating parent. It retains sampled window ownership, visibility and foreground state throughout. Polling cannot establish continuous observation between samples.

Matched ordinary SRP context/camera begin/end callbacks identify eligible end-to-end boundary intervals. They are CPU-side render boundary intervals, including intervening game work; they are not GPU timers or display presentation times. The existing `Capture` calls snap the camera, submit two explicit render requests and read PNG pixels. The complete method and crossing intervals are bracketed and excluded from timing. Those camera snaps still belong to this unchanged instrumented route workload, so the report must not describe an unsnapped human route.

Each logical room visit retains a cold-inclusive histogram and a separate lane excluding its first five eligible seconds. Both use the existing 0.25 ms / 2049-bin policy and provisional 33.3 ms choice. Long eligible frames stay in the data. Empty and short warm visits remain explicit; they cannot pass a room budget. Exclusions, interval counts, trailing unobserved time, configuration changes and restart boundaries remain recorded. Completion freezes the original route session before its unchanged terminal restart; capture and callback accounting continue until teardown.

At most 1024 state samples retain actual application focus, time scale, display/camera/pipeline configuration, a managed-memory estimate and Unity allocator pools. Sampling runs no more frequently than once per second, apart from initial/final snapshots; it may miss short peaks. These pools do not represent complete process or GPU memory. No forced garbage collection occurs.

Six optional `ProfilerRecorder` capabilities record their actual category, units, validity, retained count and value: Main Thread, Render Thread, Draw Calls Count, SetPass Calls Count, Shadow Casters Count and Triangles Count. Unavailable values remain unavailable. They are completed-frame statistics observed at read time; their originating room/frame is not proved. Do not infer individual light, shadow, shader or effect cost from these counters. Recorders and subscriptions are disposed before evidence closure.

The output includes runtime/callback/XML/completion files, separate ready/result JSON for both cases, and the existing **23 PNGs, six combat-framing JSONs and two route input traces**. Completion binds both ready and result file hashes. Pure validators check histogram moments, percentile/count accounting, visit/session ownership, exact cases, callback balance and artifact identity. Successful qualification additionally requires normal self-exit, guarded resources, unchanged player files, restored wake state, clean native/audio fatal checks and no requested crash dump. An intervention or a zero process exit with a fatal log cannot qualify.

## Running it

Build with pinned Unity **6000.6.3f1** in an isolated project whose complete source/import inputs have been reviewed. Set `SHADOWS_RENDERED_ROUTE_BUILD=1` and use the exact anchored filter below with `-runTests -testPlatform StandaloneWindows64 -assemblyNames Shadows.PlayMode.Tests`, an absolute fresh `-buildPlayerPath`, and `-doNotReportTestResultsBackToEditor`. Keep graphics and audio enabled. The build modifier preserves the UTF bootstrap followed by `Assets/Scenes/ForsakenCastle.unity`, disables autorun/host connection and writes the finalized `PlayerWithTests/test-build.json` before normal Editor exit.

```text
^ShadowsOfTheForsaken\.Tests\.PlayMode\.FullCastleRouteTests\.(CastleMainRouteCompletesThroughControlsWithoutSecret|CastleSecretRouteReturnsThroughThroneAndStillRequiresFinalFight)$
```

After independently checking the build receipt and complete player identity, run on native Windows:

```sh
python3 tools/run_standalone_rendered_routes.py --player ABSOLUTE_PLAYER_EXE --output FRESH_ABSOLUTE_OUTPUT --build-receipt ABSOLUTE_TEST_BUILD_JSON --build-receipt-sha256 ACTUAL_SHA256
```

The launcher requires no overlapping Unity/game/test player, at least 32 GiB free space, at most 5 GiB fresh evidence growth and a finite 5–600 second deadline (default 360). It never changes Windows audio defaults, volume, services or drivers. It uses only owned-window cleanup on failure and preserves that failure. Source and fake-host checks do not establish an actual native rendered run.

Focused local source checks:

```sh
dotnet test tests/RenderedRoutes/RenderedRoutes.Tests.csproj --configuration Release
python3 -m unittest discover -s tests -p 'test_*rendered_route*.py' -v
python3 -m unittest discover -s tests -p 'test_concealed_native_window.py' -v
```

The initial implementation is pending its own Unity build and native execution. The previous completed native10, concealed3 and hour results remain separately scoped to their original source and profiles.
