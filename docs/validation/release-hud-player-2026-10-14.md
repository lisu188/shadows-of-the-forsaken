# Ordinary Windows Release player with HUD correction — 2026-10-14

The ordinary Windows x64 player was rebuilt from clean LF source commit **`e4d58a19a5b0714fd4519015778b4f8ce8dbf7b9`**, including the primary HUD depth correction and the earlier landing-camera fallback. [CastlePlayerBuild.BuildForBatch](../../Assets/LevelLayout/Editor/CastlePlayerBuild.cs) uses **`BuildOptions.None`**, `StandaloneWindows64` and only the authored `Assets/Scenes/ForsakenCastle.unity` scene. The build succeeded with zero BuildReport errors/warnings. Its exact ordinary player passed a **120-second hidden startup/normal-exit lifecycle check**, and all **200 delivery ZIP entries** passed CRC and content-hash verification. Full ordinary Release gameplay remains separate acceptance work.

This delivery supports DOCX §§2–4 and 6–8. Its bounded startup observation checks the calm courtyard initial state from §3; it does not execute the full progression.

The [HUD/audio report](primary-hud-audio-2026-10-14.md) preserves the actual HUD failure, unchanged diagnostic assertions, focused 3/3 pass and full **233/233 PlayMode / 187/187 EditMode** passes. All 732 native Git blobs of this clean commit match both suites' recorded LF source maps in the [post-execution commit binding](../../artifacts/goal-20261014/current-suites-e4d58a1-commit-binding.json). Those suites executed before commit with an exact overlay; this source comparison does not claim another engine run after commit. The ordinary Release build below directly names the clean commit.

## Build and source identity

The command directly invokes installed pinned **Unity 6000.6.3f1** with `-batchmode -quit -executeMethod CastlePlayerBuild.BuildForBatch`. It reuses the retained isolated verification cache, rather than performing a cold import. The [build execution receipt](../../artifacts/goal-20261014/release-hud-build/execution.json) and [completion inventory](../../artifacts/goal-20261014/release-hud-build/identity/completion.json) record:

| Check | Observed result |
| --- | --- |
| BuildReport | `Succeeded`, **0 errors / 0 warnings** |
| BuildReport duration | **27.4602679 s** |
| Runner duration / exit | **39.219 s / 0**, no runner stop |
| Engine output | **199 files / 133,842,304 bytes** |
| Committed source / actual imported inputs | **732 / 733 files** |
| Temporary wake request | Restored after the build |

The [source ZIP](../../artifacts/goal-20261014/release-hud-build/source.zip) binds this commit's native inputs. The [imported ZIP](../../artifacts/goal-20261014/release-hud-build/identity/imported.zip) freezes all 733 actual completed inputs, including separately generated `SceneTemplateSettings.json`. The new [quarantine receipt](../../artifacts/goal-20261014/release-hud-build/quarantine-result.json) is complete with **zero moves and zero pending entries**: no retained UTF bootstrap asset needed moving in this build. The six moves recorded for the earlier build are historical evidence for that earlier operation.

The [approved import review](../../artifacts/goal-20261014/release-hud-build/identity/import-review.approved.json) and [companion analysis](../../artifacts/goal-20261014/release-hud-build/identity/import-review-analysis.json) qualify **19 exact source/import pairs**, with zero unreviewed pairs and zero unresolved findings. This is technical qualification of the recorded import bytes, separate from gameplay, visual, audio, performance or owner acceptance.

| Identity item | SHA-256 |
| --- | --- |
| `source.zip` | `6f7ad0ad17ad7642d2ddc4e112f6988ddb75093c202600e681f32fbbe6c4c201` |
| `identity/imported.zip` | `ba663c59a954142fcd29e7665fc7e9ff8ea03b037172c40867fe90779505a290` |
| Approved import review | `411206a91e1774aef566b3584302b1e166bc2fe101a050640a97b6d700628350` |
| Companion analysis | `abd409079cafb91d6f681e3eb4398de2ffd246db343602ed44f9132830fabd34` |
| Supplementary `BUILD-INFO.json` | `c75342a1a137ba42d7469e4f70eb73dcbc1d12f9dfa34615badbed9ccf2b86f0` |

`BUILD-INFO.json` is a supplementary manifest-tool receipt, rather than Unity output. It binds the exact engine inventory to the source commit/archive, completed imported archive and reviewed differences. The [independent build-identity review](../../artifacts/goal-20261014/release-hud-identity-independent-review.json), SHA-256 `71061e4160817997ca9bfca95b22214c33d2318dc67bef692826a3f8c90ee561`, rehashes all 199 engine files, verifies the 732-source/733-import input chain and 19 reviewed pairs, and reconstructs this receipt exactly. Its scope is build/source identity; it does not infer a player lifecycle or package result.

Zero BuildReport warnings is a summary result, rather than an error-free Editor-log claim. The retained build log includes licensing/package diagnostics and, after Mono cleanup, four `abort_threads` notices and debugger port 4024 unavailable, followed by successful batch exit 0. These notices do not diagnose the historical long-session Mono shutdown fault.

## Lifecycle and delivery package

The [ordinary-player lifecycle receipt](../../artifacts/goal-20261014/release-hud-native/execution.json), SHA-256 `29294481fe36e03d5d35a877323393cb00e6360c256af1579338b3420a84d30f`, records **`startup_lifecycle_verified`**, **126.500 s** total runner time and **476 fresh snapshots** matching one valid session. The configured **120-second monotonic observation gate passed**. The first-to-last observer UTC span is **119.930980 s**, a distinct sampled interval. All nine startup checks passed: Running/Courtyard, zero objectives, full 200 HP, three expected actors, all dormant, no observer I/O failures, the pinned player, observed D3D11 and a valid session ID. Twenty-three transient missing-file retries and one sharing retry remain recorded; the final errors list is empty.

Launcher and child both had **medium integrity**. The normal renderer ran with `batch_mode=false` in an invisible nonactivating owned parent, requesting legacy graphics jobs and observing **`kGfxThreadingModeClientWorkerJobs`**. Audio remained enabled. No input, focus, time or progression injection was used, and no foreground/visible-window violation occurred. The three retained native logs contain no detected native-fatal/audio-failure marker or diagnostic candidate; the requested crash directory is empty. These log checks do not assess physical speaker output or human audio acceptance.

The runner posted **WM_CLOSE only to its own player PID 38004**. The player recorded normal exit and returned **0**, with no forced termination or other process touched. The owned parent was destroyed, the temporary wake request restored and all engine/receipt identity remained unchanged.

The [new ordinary Windows x64 ZIP](../../Builds/ShadowsOfTheForsaken-Windows-x64-20261014-e4d58a1.zip) is **44,483,614 bytes**, SHA-256 **`574edc715016c1fe7ec3ce56446e128b09bc61b281a7b15cc1f92ec2a9f9c297`**. The [package receipt](../../artifacts/goal-20261014/release-hud-build/package.json), SHA-256 `fc8fd2c30210626c6722777611ca60205a70616ca88ce10b3492e50e249b93ff`, verifies **200 entries**: 199 engine files plus `BUILD-INFO.json`, exact paths, CRC and every SHA-256. It binds the same source/archive/manifest and completed lifecycle receipt and verifies unchanged engine identity after packaging. The recorder's six delivery pins were updated to this package.

The [final independent delivery review](../../artifacts/goal-20261014/release-hud-final-independent-verification.json), SHA-256 `4a3db3b0a625b70fc0b15b3fa5f24dffb9e873d065475e646559a2c54d45640b`, verifies all 476 fresh startup snapshots, normal owned-player exit, medium-integrity hidden lifecycle, host/wake cleanup and unchanged engine identity. It independently checks all 200 ZIP entries and their container, confirms all six recorder pins with unchanged recorder logic, and rehashes both historical October 8 and `08b2697` ZIPs as preserved. It reports **zero unresolved findings** within those scopes and retains the separate human/gameplay/audio acceptance limits.

The [earlier ordinary Release report](release-player-2026-10-14.md) and its `08b2697` package remain separately retained historical evidence and cannot qualify the new HUD bytes. The [current instrumented native route evidence](standalone-hud-routes-2026-10-14.md) remains distinct from a full ordinary Release playthrough.

## Final scoped tooling checks

The [final delivery checks](../../artifacts/goal-20261014/hud-delivery-checks/) retain two fresh executions with the new recorder pins:

| Check | Actual result |
| --- | --- |
| Windows recorder selection | **13/13 passed**, zero skips/failures; **10.462 s** test / **10.609 s** runner, exit 0 |
| WSL Python tooling suite | **206 total: 193 passed, 13 Windows-only skipped**; **9.963 s** test / **10.703 s** runner, exit 0 |
| Final DOCX/design contract | Exit 0; original DOCX unchanged, 29 basic / 63 secret reachable model states |
| Final Unity source integrity | Exit 0; 330 assets, 899 tracked files, 374 unique GUIDs |

The Windows result covers the recorder selection, rather than the full Python suite. WSL skips are not passes. Retained log SHA-256 values are `4def5e3084e9299682029eb9afd3fa8022e8b839337e1ebdaa9693a75b8ebb04` for the recorder and `3ddc35bf2a3988762e0656f6ec80b4f67475ccca0670537834aea36cd3081857` for WSL. The separate DOCX/contract, source-integrity, .NET core and 233/187 Editor checks remain recorded with their own counts and execution scopes in the [HUD/audio report](primary-hud-audio-2026-10-14.md).

Source checks, deterministic .NET tests, Editor integration, native test-player routes and this ordinary Release lifecycle check have distinct scopes. This hidden startup check does not establish the two puzzle-pose HUD views in an ordinary Release playthrough. Human controls/readability, audible output, DOCX two-to-three-minute pacing, hardware performance and final owner acceptance remain open. The historical Mono shutdown cause remains undiagnosed; this build does not establish an engine repair.
