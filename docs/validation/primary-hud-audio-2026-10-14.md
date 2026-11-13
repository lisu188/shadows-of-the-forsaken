# Primary HUD occlusion and bounded mixer check — 2026-10-14

The saved primary castle HUD disappeared behind nearby puzzle-room walls at two recorded camera poses. A clean rendering diagnostic failed both poses before the fix and passed both after moving the camera-space canvas immediately beyond the near clip plane. The same focused run also passed a separate test-only audio mixer check after correcting that fixture's callback ownership and cleanup. **The focused result is 3/3 passed, followed by full 233/233 PlayMode and 187/187 EditMode passes with the fix. Source/tooling checks also passed within their stated scopes. The [new ordinary Release player](release-hud-player-2026-10-14.md) built successfully, passed its bounded startup/normal-exit check and was packaged with verified identity.**

This is integration evidence supporting the controls and presentation of the DOCX castle level, particularly §3 and §7. HUD geometry and the diagnostic signal are implementation choices, rather than additional DOCX requirements. Human readability, audible output, atmosphere, ordinary Release route completion, performance and the two-to-three-minute human pacing target remain separate acceptance work.

## Source and execution identity

The runs used pinned Unity **6000.6.3f1**, a hidden batch Windows Editor with graphics and audio enabled, and the retained isolated verification project. The receipts bind base commit `84d8a1a8b5c4d5d6a893124b85e93a657f64b48f` plus exact staged LF-byte overlays; they do not imply that the unchanged base commit contains the fix.

The clean failure overlay added four test assets. The fixed overlay retained the identical HUD fixture and its `.meta`, corrected the audio fixture, and changed only [LevelHUD.cs](../../Assets/LevelSession/LevelHUD.cs) in production. The three listed source files in the imported ZIP match the execution receipt's source hashes:

| Bound source | SHA-256 |
| --- | --- |
| Unchanged `PrimaryCastleHudRenderingTests.cs` | `2ffc893b342244ee5e70fc28ad92f7b41a6f4f15f68e233cfdfc0ca7abe794d3` |
| Fixed `LevelHUD.cs` | `b084172b12a17aa04e9e2f361a2642927a26d49a8ae4337171fd376a7a182a1c` |
| Fixed `AudioMixerPlaybackTests.cs` | `bee6ababb48a0a70e07908fdf15041ebc462b943c98101c0547d9da79a43d155` |

The full 233-case overlay also includes the faithful route-capture change in [FullCastleRouteTests.cs](../../Assets/Tests/PlayMode/FullCastleRouteTests.cs), source SHA-256 `d58d6848d3cf146a5435daaf171b7175e3c78344e08c51c6375f4dd8e358b40b`. Its capture helper now preserves canvases that already use `ScreenSpaceCamera`, including the primary HUD's production depth. Only overlay canvases are temporarily adapted for render-target capture, at `nearClipPlane + .001f`. Previously, capture preparation moved every canvas to another depth and could conceal wall occlusion. Route screenshots still use a pipeline render request; the ordinary-frame HUD triple remains the independent rendering regression. These source bindings do not imply that all imported material/settings bytes are unchanged by Unity.

The [independent HUD review](../../artifacts/goal-20261014/primary-hud-before-after-review.json), SHA-256 `b7b00323e30d6b9593bb16da5b6bed54ca6ae38ec69ed131ef156e6721ed7156`, binds all twelve PNGs, four pose receipts, XML/log/process/source receipts, imported sources and the exact production diff. It also independently recomputes every reported region's HUD and text pixel counts from the PNGs. Raw bundles are retained locally under `artifacts/goal-20261014/`; they are ignored execution evidence, rather than checked-in build output.

## Reproduction and minimal fix

[PrimaryCastleHudRenderingTests](../../Assets/Tests/PlayMode/PrimaryCastleHudRenderingTests.cs) loads `Assets/Scenes/ForsakenCastle.unity` through the existing owned-scene lease and reproduces the recorded puzzle west-wall and northwest-corner presentation poses. It binds a persistent **1280×720** render target, leaves the production canvas render mode/materials unchanged and waits four ordinary frames for each phase, including `Canvas.ForceUpdateCanvases`. It uses neither forced `Camera.Render` nor a pipeline render request. Existing canvases are suspended and restored. Actor child poses are frozen and checked so body/cape animation cannot supply false HUD differences.

Each pose saves three images: HUD visible, whole canvas disabled, and canvas enabled with only the actual Health/Controls `Text` components disabled. Each Health or controls-half region must contribute at least **32 differing pixels** in both the whole-HUD and text-only comparisons. The text-only comparison prevents a panel background from satisfying the glyph check. Bounds, camera transforms, near-plane corners, actor stability and world rendering are saved before assertions.

The clean failed run retained **921,066** world pixels at pose05 and **921,215** at pose08, with four actors / 236 child parts unchanged and all tested text bounds inside the viewport and in front of the camera. Yet Health and the left controls contributed zero pixels at both poses. Pose05 lost all HUD; pose08 retained only a right controls fragment. This reproduces the defect through ordinary frames independently of the earlier one-frame exploration capture.

The production change replaces `Mathf.Max(1, camera.nearClipPlane + .1f)` with `camera.nearClipPlane + .001f`, plus a two-line explanation. For the saved primary camera's near clip **0.3 m**, **75°** FOV and **16:9** aspect, its farthest near-plane corner is approximately **0.557198 m** from the camera. [CameraFollow](../../Assets/CameraFollow.cs) checks overlap using this radius plus half the saved **0.05 m** padding, approximately **0.582198 m**. The old 1 m canvas extends to approximately **1.857326 m** at its corners; the new 0.301 m canvas extends to approximately **0.559055 m**, inside that checked region and 0.001 m beyond the near clip. This reasoning applies to the authored primary configuration. The fix preserves projection/layout bounds, render mode, sorting, camera collision clearance, gameplay, progression and the no-camera overlay fallback.

| Recorded pose / region | Before whole HUD / text pixels | After whole HUD / text pixels |
| --- | ---: | ---: |
| 05 west wall — Health | 0 / 0 | 8,809 / 1,589 |
| 05 west wall — controls left | 0 / 0 | 5,551 / 2,155 |
| 05 west wall — controls right | 0 / 0 | 5,626 / 716 |
| 08 northwest corner — Health | 0 / 0 | 8,807 / 1,607 |
| 08 northwest corner — controls left | 0 / 0 | 5,616 / 2,142 |
| 08 northwest corner — controls right | 4,361 / 200 | 5,640 / 716 |

The fixed receipts record plane distance **0.300999999 m**, identical camera/player poses and viewport settings, unchanged HUD assertions and unchanged world pixel counts. Direct inspection of all twelve images agrees with the metrics. Fixed images show Health and complete controls; their text-disabled partners remove those glyphs while retaining the panels. These pose fixtures keep initial progression state, so their displayed `COURTYARD` label is not a route-progression result.

| Actual retained pose | Before | After |
| --- | --- | --- |
| 05 puzzle west wall | [Failed visible image](../../artifacts/goal-20261014/focused-primary-hud/captures/primary-hud-2eb0ce6ff3434d76bec9e9b8116d78fe/05-puzzle-west-wall-hud-visible.png) | [Fixed visible image](../../artifacts/goal-20261014/focused-hud-audio-fixed/captures/primary-hud-8ba87af2785b43c3b680cc94199fd8dd/05-puzzle-west-wall-hud-visible.png) |
| 08 puzzle northwest corner | [Failed visible image](../../artifacts/goal-20261014/focused-primary-hud/captures/primary-hud-1e29f711f5fb48abbcbe968e084c906d/08-puzzle-northwest-corner-hud-visible.png) | [Fixed visible image](../../artifacts/goal-20261014/focused-hud-audio-fixed/captures/primary-hud-c76e9f8be2f64ef7a6602b777a9866dc/08-puzzle-northwest-corner-hud-visible.png) |

## Audio fixture correction and measured scope

The first combined attempt failed **0/3**. Its new test fixture placed an `AudioSource`, `AudioListener` and `OnAudioFilterRead` tap on one object. Unity rejected the ambiguous callback ownership, then UTF abandoned the iterator before its `finally`; the surviving probe repeated errors during later HUD cleanup. No audio evidence JSON was produced. The preserved [initial failure review](../../artifacts/goal-20261014/focused-hud-audio/audio-failure-review.json) classifies this as a test fixture setup/lifecycle failure, not an established audio device, FMOD or gameplay defect.

The correction gives the listener its own owned child and adds idempotent NUnit teardown alongside iterator cleanup. [QuietClipTraversesMixerAndReturnsToSilence](../../Assets/Tests/PlayMode/AudioMixerPlaybackTests.cs) then passed in **0.274940 s**. Its actual Windows Editor evidence records Stereo **48 kHz**, buffers **1024×4**, an advancing DSP clock, source/master signal peaks **0.00212132**, **7** audio callbacks, **4,054** nonzero callback samples and zero invalid samples. The generated quiet clip is 0.5 s long; the test observes its signal after approximately **0.042667 DSP s**, then stops it and verifies freshly processed master silence after approximately **0.106667 DSP s**. It does not claim to have played the entire clip.

Owned objects were destroyed and global/source/listener cleanup flags passed with no cleanup errors. This run borrowed **zero sources and zero listeners**, with original gain 1 and pause false, so it does not exercise restoration of borrowed objects or nondefault audio state. The [independent audio review](../../artifacts/goal-20261014/focused-hud-audio-fixed/independent-audio-review.json), SHA-256 `6f47c495224726bb5511a8a07c11e2c0c733e68bc7d21a71b62b562bbe2dfa8b`, binds the PCM/DSP evidence and logs. Real Editor mixer processing does not establish physical speaker output, native standalone mixer behavior or production soundtrack acceptance. No production audio source changed, and the historical Mono shutdown cause remains undiagnosed.

## Results and remaining checks

| Retained run | Actual result | Scope |
| --- | --- | --- |
| `full-current-230-retry` | **230/230 passed**, no failures/skips/inconclusive; XML **727.857252 s**, runner **745.594 s**, exit 0 | Base `84d8a1a` full suite **before the HUD fix and three new cases** |
| `focused-hud-audio` | **0/3**, exit 2 | Initial audio fixture failure plus HUD failures; preserved |
| `focused-primary-hud` | **0/2**, XML **1.374403 s**, runner **16.765 s**, exit 2 | Clean production HUD reproduction without the audio case |
| `focused-hud-audio-fixed` | **3/3**, no failures/skips/inconclusive; XML **1.675561 s**, runner **26.890 s**, exit 0 | Two unchanged HUD render cases plus corrected audio case |
| `full-current-233` | **233/233 passed**, no failures/skips/inconclusive; XML **728.886816 s**, runner **753.281 s**, exit 0 | Full suite with the production HUD fix, three new cases and faithful route captures |
| `editmode-current-187` | **187/187 passed**, no failures/skips/inconclusive; XML **1.110323 s**, runner **17.813 s**, exit 0 | Full EditMode selection with the same six-file source overlay |

The earlier `full-current-230` attempt stopped for the runner's disk-reserve guard after 395.610 s and produced no results XML; it is preserved and is not a passed suite. The clean/fixed focused runs, full 233-case run and EditMode run terminated without a runner stop and restored their temporary wake requests. Known audio/native-fatal failure markers were absent from the focused logs; licensing-token and generic Editor/package diagnostics remain, so this report does not call the entire logs error-free. The full PlayMode [XML](../../artifacts/goal-20261014/full-current-233/results.xml) and [execution receipt](../../artifacts/goal-20261014/full-current-233/execution.json), and EditMode [XML](../../artifacts/goal-20261014/editmode-current-187/results.xml) and [execution receipt](../../artifacts/goal-20261014/editmode-current-187/execution.json), retain the complete selections and source overlay.

The [independent full PlayMode review](../../artifacts/goal-20261014/full-current-233-independent-review.json), SHA-256 `5000a9d33c596a4af52cc03a7004eb823df97dd8ece243af95bfd23c7a4a3026`, verifies the exact 233-case catalog, all cases passed, source overlay/import archive and individually classified importer changes. It structurally checks all 64 PNGs and independently decodes the six HUD images to reproduce their contribution counts. Audio DSP/source/master signal, stopped silence and cleanup are verified; the unsnapped exploration records zero unsafe samples or blocked-camera warnings. This is evidence from the retained warm Editor cache with the stated overlay, rather than a new standalone or human run. Shutdown debugger/thread notices remain classified in the review.

The [independent EditMode review](../../artifacts/goal-20261014/editmode-current-187-independent-review.json), SHA-256 `0a44420728baac2cef7bc6b1998812671f8192d25fc06b9982106d310cd0e863`, verifies all 187 expected cases passed, the 732-source-file overlay and 733-input imported archive, and 22 individually reviewed serialization changes with no unexplained source drift or added/removed inputs. It also retains precise shutdown notices from both suites: after `Cleanup mono`, EditMode logs four `abort_threads` notices and debugger port 3996 unavailable; PlayMode logs six `abort_threads` notices and debugger port 3980 unavailable. Both suites produced complete passing NUnit results and owned runner exit 0. These notices and the separate licensing diagnostic remain visible evidence; they are neither an error-free-log claim nor proof of a fatal gameplay/audio cause.

The later [clean-commit binding](../../artifacts/goal-20261014/current-suites-e4d58a1-commit-binding.json), SHA-256 `c0351ef58c6a2d19a4b572c077857b0fba3ffafda9605c4cc4a7eea52abdf42e`, compares all 732 native Git blobs and actual LF bytes of commit `e4d58a19a5b0714fd4519015778b4f8ce8dbf7b9` with both completed suites' execution maps. Their native source sets match, as do the 233/187 catalogs. The suites ran before that commit using the recorded overlay; this comparison binds the source afterward and does not claim another engine execution after commit.

The separate [source-check bundle](../../artifacts/goal-20261014/hud-audio-source-checks/) records:

| Check | Actual result and scope |
| --- | --- |
| DOCX/design contract | Exit 0; **29 basic / 63 secret reachable model states**, source DOCX unchanged. This does not execute Unity scenes, combat or timing. |
| Unity project source integrity | Exit 0; receipt counts **330 assets, 896 tracked files, 374 unique GUIDs** at that check. This does not validate runtime behavior. |
| Python tooling in WSL | **206 total: 193 passed, 13 Windows-only skipped**; test **10.886 s**, runner **12.016 s**, exit 0. Skips are not passes. |
| Same-source .NET progression core | **29/29 passed**, zero failed/skipped; TRX retained, runner **5.610 s**. This compiles the shared deterministic source, not MonoBehaviour integration. |

A later project-integrity check at 10:57 passed with **330 assets, 897 tracked files and 374 unique GUIDs**. The earlier source-check receipt's 896-file count remains accurate for its snapshot; neither source inventory count is runtime acceptance.

The [new ordinary Release package](release-hud-player-2026-10-14.md) binds the HUD correction to clean source commit `e4d58a1`, with a completed two-minute hidden lifecycle check and verified 200-entry ZIP. This establishes compiled delivery and startup/shutdown identity; the earlier package remains historical. Automated pixel contribution also does not establish typography quality or owner approval. A full ordinary Release playthrough, audible assessment and declared uninterrupted human timing remain required for their respective acceptance claims.
