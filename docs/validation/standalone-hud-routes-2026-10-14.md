# Standalone routes with the corrected primary HUD — 2026-10-14

The instrumented Windows test player built from commit `e4d58a19a5b0714fd4519015778b4f8ce8dbf7b9` passed the exact **10-case FullCastleRouteTests filter**, then exited itself with code 0. The run used the corrected production HUD and a capture helper that preserves its camera-space depth. All fourteen harness qualification checks passed. This establishes the selected native test-player cases and shutdown; ordinary Release full-route completion, physical audibility, human acceptance, pacing and rendered performance remain separate.

The progression checks support DOCX §3, §6 and §8 and the recorded design decisions: the library remains mandatory, the catacomb bonus branch remains optional, and both completion routes require the final fight. The native filter contains **six gameplay cases and four framing-geometry cases**, rather than ten complete playthroughs. Earlier standalone reports and failed attempts remain preserved.

## Build and source identity

The build used pinned **Unity 6000.6.3f1**, the retained isolated verification cache and a clean LF checkout of the committed source. Its source map contains **732 committed Unity inputs**. The before-inventory contains 733 files including the generated Editor SceneTemplate setting; the after-inventory and frozen imported ZIP contain **735 files**, including this build's generated UTF bootstrap scene/meta pair.

| Build observation | Actual value |
| --- | --- |
| Target | `StandaloneWindows64` |
| Final BuildReport | `Succeeded`, 0 errors, 0 warnings |
| Reported bytes / duration | 200,787,723 bytes / 33.1821885 s |
| Builder elapsed / exit | 44.844 s / 0 |
| Scene selection | UTF `InitTestScene1315d61e-708f-45ff-ac91-0721d2ac2385.unity`, then `Assets/Scenes/ForsakenCastle.unity` |
| Test-build options | Development and IncludeTestAssemblies true; AutoRunPlayer and ConnectToHost false |
| Builder cleanup | Owned child terminal before snapshot; temporary power request restored on the same native thread |

This is a development test build, separate from the ordinary `BuildOptions.None` package. The [actual final test-build receipt](../../artifacts/goal-20261014/standalone-route-hud-build/player/test-build.json) has SHA-256 `e6dd10369b3f6524f702250c901ba3685c36ab6e225131472bfbd6e511ecc628`. The [imported ZIP](../../artifacts/goal-20261014/standalone-route-hud-build/imported.zip) has SHA-256 `f9831eebd0bd3985e7d7f98abfaac358bbb0dd17df33f079b632d5d49e8d9355`.

The independent audit compared all 732 source hashes to the exact committed Release source ZIP payload, SHA-256 `6f7ad0ad17ad7642d2ddc4e112f6988ddb75093c202600e681f32fbbe6c4c201`. Code, package, version and committed metadata/GUID inputs remained unchanged in the imported snapshot. All **21 source/imported differences** were classified: 13 material serialization/editor-version changes, five settings imports, the generated SceneTemplate setting and the UTF scene/meta pair. The material payloads match after whitespace and `_EMISSION` list normalization; their added URP AssetVersion 10 metadata has generated local IDs. Settings pairs match either the current reviewed Release imports or the prior pinned native test-build imports. Development test settings retain a rendering-debugger resource and `managedCodeVariant.Standalone: 3`; this does not assert ordinary Release equivalence.

| Compiled source | SHA-256 |
| --- | --- |
| `LevelHUD.cs` | `b084172b12a17aa04e9e2f361a2642927a26d49a8ae4337171fd376a7a182a1c` |
| `FullCastleRouteTests.cs` | `d58d6848d3cf146a5435daaf171b7175e3c78344e08c51c6375f4dd8e358b40b` |
| `CameraFollow.cs` | `33e7a6f3d1271422412d89529b59e6c5c998d60544020d8cdcd1601d2a8ee595` |

## Native results and lifecycle

The [NUnit XML](../../artifacts/goal-20261014/standalone-routes-hud/results.xml) records **10 passed, 0 failed, 0 skipped and 0 inconclusive**, duration **430.9186673 s**. Its SHA-256 is `5470580db021314bf45d40f6de376caf2f475f90ab554bcd0aa58ce7db152b30`. The exact full names include both argument variants of `FramingRaySelectsNearestSurfaceIncludingUnlabelledPlayer`. Twenty-two callback events cover run start, ten case starts/finishes and run finish; completion counters and XML/event hashes agree.

| Gameplay case | NUnit duration |
| --- | ---: |
| Final-fight death and saved-world restart | 83.211174 s |
| First-fight death and saved-world restart | 44.796214 s |
| Locked main gates reject walking and jumping | 37.849057 s |
| Basic route completes through controls without secret | 81.646887 s |
| Secret route returns through throne and still requires final fight | 121.244006 s |
| Throne-fight death and saved-world restart | 62.040499 s |

The runtime independently reports `WindowsPlayer`, `is_editor=false`, `batch_mode=false`, Direct3D 11, Unity 6000.6.3f1 and fixture-controlled InputSystem events. Its recorded hardware is an i5-14400F, RTX 4060 Ti, 32,598 MB system memory and a 1280×720 player viewport. The native log observes `kGfxThreadingModeClientWorkerJobs`.

An independent live Win32 read verified player PID 45244 at Medium integrity/RID 8192. Its render window's actual parent was the owned hidden host, and its actual client rectangle was 1280×720. All observed player/host windows were hidden and outside the foreground. The [live review](../../artifacts/goal-20261014/standalone-routes-hud-live-review.json), SHA-256 `2decd789b1f9bb1011446ea1f6474537407530bd75de8d7820641ea93b3016dd`, is separate from command-line intent.

The player exited itself at **11:12:53 UTC**, code 0, after **434.907 s** runner elapsed. No WM_CLOSE, forced termination or cleanup intervention was used. The owned host was destroyed and the temporary awake request restored after child exit. The audit rehashed the actual **344-file test-player tree** against both before/after snapshots and the build inventory; every entry matched. It also rehashed all **67 raw evidence inventory entries**. All fourteen qualification checks were true.

The [execution receipt](../../artifacts/goal-20261014/standalone-routes-hud/execution.json) has SHA-256 `67ec9c20dbbf707a6c5c300c6ae14a296e1ce3b9e8663a29371afd9bebb15e71`. The [independent completed review](../../artifacts/goal-20261014/standalone-routes-hud-independent-review.json), SHA-256 `c9a036b242fe78d92054ded83aefa483f35966372edcd70cea8444e039d0fcce`, binds build/source/import hashes, the exact case catalog, runtime/log/cleanup evidence, input traces and captures. Raw bundles remain local ignored evidence, not checked-in player output.

## Route traces and current captures

The basic-route trace records **81.526489 s**, 3,358 input records and final health 200/200. It completes the first encounter, main puzzle, miniboss, library and final fight before Exit, without the optional objectives. The secret trace records **121.121826 s**, 7,066 input records and health 200/200; it opens the library before the catacomb lever/bonus branch, returns through the throne/library connection and still defeats the final enemy before Exit. Both traces contain 28 attack-true input records. These are scripted route elapsed times including instrumentation/capture overhead; input records are not rendered frame counts, and these timings do not establish the DOCX two-to-three-minute human pacing target.

All **39 PNGs** passed independent chunk-CRC, 960×540 RGB8 dimension and decompressed-scanline checks. Nine framing receipts report no geometry failure. This reviewer visually inspected nine representative fresh captures covering courtyard, rune puzzle, miniboss, sealed library, relic, both exits and first/final defeat. Health, controls and outcome text were visible in those images; this is reviewer inspection, not owner acceptance or every-view typography proof.

The capture helper now converts only overlay canvases for the camera render request. It preserves an already camera-space HUD's production depth, including the new `nearClipPlane + .001f` value; the earlier unconditional capture-depth override can no longer mask a deeper production canvas. Existing restoration and gameplay assertions/deadlines remain unchanged. These programmed captures still use the fixture's existing camera snap and explicit render requests. The ordinary-frame HUD before/after diagnostics and unsnapped camera exploration remain separate evidence, described in [the HUD/audio report](primary-hud-audio-2026-10-14.md).

| Fresh instrumented native capture | Evidence |
| --- | --- |
| Sealed library before activation | [Current library view](../../artifacts/goal-20261014/standalone-routes-hud/captures/CastleFinalFightDeathRestartsTheSavedWorld-route-library-sealed.png) |
| Basic completion | [Current basic exit](../../artifacts/goal-20261014/standalone-routes-hud/captures/CastleMainRouteCompletesThroughControlsWithoutSecret-route-complete-main.png) |
| Optional completion still after final fight | [Current secret exit](../../artifacts/goal-20261014/standalone-routes-hud/captures/CastleSecretRouteReturnsThroughThroneAndStillRequiresFinalFight-route-complete-secret.png) |
| Final defeat and restart display | [Current defeat view](../../artifacts/goal-20261014/standalone-routes-hud/captures/CastleFinalFightDeathRestartsTheSavedWorld-route-defeat-FinalEnemyDefeated.png) |

## Retained diagnostics and limits

All three native logs were scanned after exit. Known native-fatal and FMOD/audio initialization/reset failure markers were absent; no crash reports or callback-failure artifact were produced. The native log retains four exact unavailable/stripped inactive-pass messages: two Gaussian Depth of Field, one Bokeh Depth of Field and one Panini Projection. The guarded first-case capture setup records their unavailable state and expects those messages; the case passed. This report does not call the logs error-free.

The separate **test-build Editor process 44348** retained three `JobTempAlloc` remaining-allocation/leak diagnostics at `editor.log` lines **13101, 13103 and 13106**, after `Cleanup mono` at 13086 and package-manager shutdown at 13100. They were not observed in the native player's log. Their cause is unproven: the successful BuildReport's zero warning count does not erase shutdown diagnostics, and these observations do not establish a production gameplay leak or performance qualification. Other generic Editor/package/licensing/mono cleanup diagnostics remain in the raw build log. The historical low-integrity native shutdown crash cause also remains undiagnosed.

The selected ten native cases do **not** execute `AudioMixerPlaybackTests`. Absence of native audio-failure markers does not establish native PCM playback or physical audibility. The separate Editor mixer result remains limited to real Editor mixer processing. Ordinary Release full-route play, audible assessment, uninterrupted human timing, performance acceptance and owner visual approval remain outside this instrumented result.
