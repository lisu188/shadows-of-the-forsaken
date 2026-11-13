# Ordinary Windows Release player — 2026-10-14

The ordinary Windows x64 player was rebuilt from source commit **`08b26973d36fd22b0864649073b3ffe334eec62b`**, including the validated landing-camera fallback. [CastlePlayerBuild.BuildForBatch](../../Assets/LevelLayout/Editor/CastlePlayerBuild.cs) uses **`BuildOptions.None`**, `StandaloneWindows64` and only the authored `Assets/Scenes/ForsakenCastle.unity` scene. This is separate from the development test player that passed [10/10 fixture-controlled native route cases](standalone-routes-2026-10-14.md).

The new **200-entry delivery ZIP** passed CRC and content-hash verification. Its exact player passed a **120-second hidden startup lifecycle check**, with normal requested shutdown and unchanged identity. This qualifies the observed ordinary-player startup/shutdown and package identity; full Release gameplay remains separate.

## Build and source identity

The recorded command directly invokes the installed pinned **Unity 6000.6.3f1** Editor, with `-batchmode -quit -executeMethod CastlePlayerBuild.BuildForBatch`. The retained isolated verification cache is reused; this is not a cold import. The build receipt records:

| Check | Observed result |
| --- | --- |
| BuildReport | `Succeeded`, **0 errors / 0 warnings** |
| BuildReport duration | **17.4957044 s** |
| Total runner duration / exit | **30.015 s / 0** |
| Engine output | **199 files / 133,842,248 bytes** |
| Source / actual imported native inputs | **728 / 729 files** |
| Temporary wake request | Restored after the build |

Raw build evidence is preserved under [release-build](../../artifacts/goal-20261014/release-build), including `execution.json`, `editor.log`, `source-snapshot.json`, actual pre/post input inventories and [identity/completion.json](../../artifacts/goal-20261014/release-build/identity/completion.json). Zero BuildReport warnings is a build-summary result, not a claim that every Editor diagnostic is absent.

The [source archive](../../artifacts/goal-20261014/release-build/source.zip) contains the **728 native input files plus 47 ZIP directory entries**, with the exact commit in its archive comment. All 728 file bytes were independently compared with the receipt's SHA-256 values and the commit's Git blobs. The [imported archive](../../artifacts/goal-20261014/release-build/identity/imported.zip) freezes all 729 actual completed native inputs, including generated `SceneTemplateSettings.json`.

Before this ordinary build, three retained UTF bootstrap scenes and their `.meta` files were moved out of the verification project into `retained-utf-bootstrap/`. The [quarantine receipt](../../artifacts/goal-20261014/release-build/quarantine-result.json) records exactly six hash-verified moves, all completed, with no pending move or deletion. The subsequent pre-build inventory contains only committed native inputs plus the separately recorded generated scene-template settings.

The [approved import review](../../artifacts/goal-20261014/release-build/identity/import-review.approved.json) covers **19 exact source/import pairs**, with **0 unreviewed pairs / 0 unresolved findings**. Thirteen material runtime objects remain equivalent after formatting normalization and added pinned-URP editor version metadata. The other six pairs cover pinned-package defaults, unresolved disabled volume-reference cleanup, build-generated shader prefilters/runtime-settings selection, whitespace and generated scene-template defaults. The [companion analysis](../../artifacts/goal-20261014/release-build/identity/import-review-analysis.json) records the exact byte comparisons and package-source basis. This is local technical qualification, not separate owner or gameplay approval.

| Identity item | SHA-256 |
| --- | --- |
| `source.zip` | `ec101e80f4b748dce69387996f5cda44a61fb80a19a971d2157a5c017d26c566` |
| `identity/imported.zip` | `9fde1928dc55039be3a40ba706641ce448a19e9bc181c3548890a38298776ec4` |
| Approved import review | `30287e22aa5eb13e9828869ef11a3d5d9021e94a9691ad5aedea460be900cdd1` |
| Companion analysis | `65052673e8d77a6c632b804702372d14828a0a57ad2e2b1edf9c5c168c20f4fd` |
| Supplementary `BUILD-INFO.json` | `83ffdc101ad51227e5ae32de2332bfbbd2fdc92a47dca055772d4eda7cd2c371` |

`BUILD-INFO.json` is a supplementary manifest-tool receipt, not a file emitted by Unity. It binds the engine inventory to the source commit/archive, imported input archive and reviewed differences.

## Completed lifecycle and package checks

The [ordinary-player execution receipt](../../artifacts/goal-20261014/release-native/execution.json) records **`startup_lifecycle_verified`**, **126.516 s** total runner time and **477 fresh snapshots** matching one valid session. The configured **120-second monotonic observation gate passed**; the first-to-last observer UTC span is **119.923738 s**, a distinct sampled interval. All nine startup checks passed: Running/Courtyard, no objectives, full 200 HP, three expected actors, all dormant, no observer I/O failures, the pinned native player, D3D11 and a valid session ID. Twenty-three transient missing-file retries and one file-sharing retry are retained separately; the final errors list is empty.

Launcher and player both had **medium integrity**. The normal renderer ran with `batch_mode=false`, D3D11 and the actually observed **`kGfxThreadingModeClientWorkerJobs`** after requesting legacy graphics jobs. An invisible nonactivating owned parent kept the run hidden; no foreground/visible-window violation was recorded. No input, focus, time or progression injection was used. Audio remained enabled, with audible output unassessed. All three retained logs contain no detected audio-failure/native-fatal marker or diagnostic candidate, and the requested crash folder is empty.

The runner posted **WM_CLOSE only to its own player PID 40220**. The player recorded normal exit and returned **0**, without forced termination or touching another process. The owned parent was destroyed, the temporary wake request restored and build identity remained unchanged. The execution receipt SHA-256 is `44e35e8cb67199efdfc69e012a46408b3bbd411fbe1cbfe5bb1723ca846e0866`.

The new [Windows x64 ZIP](../../Builds/ShadowsOfTheForsaken-Windows-x64-20261014-08b2697.zip) is **44,483,085 bytes**, SHA-256 **`c507664ae0be9fd5ac465afcb44bf33e4677537859a731c625ab3b62d0f5b372`**. The [package receipt](../../artifacts/goal-20261014/release-build/package.json) verifies all **200 entries**: 199 engine files plus `BUILD-INFO.json`, unique exact paths, CRC, sizes and every SHA-256. It verifies the same source/archive/manifest identity before and after packaging. The historical October 8 ZIP remains unchanged, independently rehashed as `3c328a9382826bce0c1090fb164f09e6b572d04d6c03fd408e2a520409b71984`.

The [independent Release review](../../artifacts/goal-20261014/release-build/identity/independent-release-review.json), SHA-256 `94990118f440ff27532e26ff65546170dce4587aed949a1b4a4a152b125fc5d5`, rehashes all 199 engine files, verifies the 728 source/729 imported inputs and 19 reviewed pairs, and checks all 477 snapshots, increasing sequences and the same valid session. It confirms normal exit, medium integrity, observed graphics mode, host/wake cleanup, absent test-instrumentation assemblies and 17 raw native evidence entries. Its build-log review retains the startup access-token diagnostic followed by successful licensing/entitlement; this is not an error-free Editor-log claim.

A [separate final ZIP/recorder verification](../../artifacts/goal-20261014/release-independent-verification.json), SHA-256 `ef2f155dc902f07037baa4926b25ad75ca1a800dedd3d36ce4d68bcb0855f98c`, rehashes 27 bound files, independently checks the 200 ZIP entries and container identity, and confirms all six recorder delivery pins match this package while other recorder text remains unchanged. It reports zero actionable or unresolved findings and preserves the same runtime and acceptance boundaries.

## Scoped source checks

The [final delivery checks](../../artifacts/goal-20261014/release-delivery-checks) retain these separate results:

| Check | Observed result |
| --- | --- |
| DOCX/contract model | Pass; unchanged DOCX, **29 basic / 63 secret reachable states** |
| Unity source integrity | Pass; **328 assets / 891 tracked files / 372 GUIDs** at execution |
| Windows Python suite | **206 total: 204 passed, 1 skipped, 1 environment error**, exit **1** |
| WSL Python suite | **206 total: 193 passed / 13 Windows-only skips**, **8.696 s** test time / **9.313 s** runner, exit **0** |
| Windows recorder cases | All **13 passed** in the fresh Windows run with the updated package pins |

The preserved Windows error is `test_unity_validation.ProjectValidationTests.test_rejects_unsafe_and_symlink_paths`: the OS refused symlink creation with **WinError 1314**, before that fixture could complete. The separate manifest symlink case was skipped. WSL executed the former case successfully without changing its source or assertion. This is not a clean full Windows pass. The retained WSL log SHA-256 is `82c038654f2bdac8f7578c25031d00550109bd2dc55a38eec8bdee289d369c9d`.

The [focused 45/45 camera result](camera-exploration-2026-10-14.md) and [10/10 instrumented native route result](standalone-routes-2026-10-14.md) retain their own scopes. Neither is a full ordinary-Release route playthrough. Human controls, audible output, DOCX 2–3 minute pacing, hardware performance and final owner visual acceptance remain open. The earlier long-session Mono shutdown fault remains unreproduced and undiagnosed; this rebuild does not establish its root cause or an engine repair.
