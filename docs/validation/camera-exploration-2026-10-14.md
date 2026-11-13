# Lower-room camera exploration — 2026-10-14

An exploration driven through the saved input actions in the primary `ForsakenCastle` scene reproduced camera rendering suspension beside the rune plinth. After a narrow camera fallback, the same exploration completed with **zero unsafe camera samples and zero blocked-camera warnings**. The focused PlayMode selection passed **45/45**, with no failures, skips or inconclusive cases. This is bounded Editor integration evidence for DOCX §3 and §7–8; it does not establish human acceptance or universal camera safety.

Both runs used pinned Unity **6000.6.3f1** in a hidden batch Editor with graphics and audio enabled, a retained isolated verification cache, D3D11 and an RTX 4060 Ti. No audio-disabling flag was supplied; audible output was not assessed. The source receipt identifies base `0408fa9367ebef8fcab0a91bf20f1a4582107d95` plus the recorded source overlay, rather than claiming the unchanged base commit contains the fix.

The local raw bundles remain under `artifacts/goal-20261014/camera-unsnapped/` and `artifacts/goal-20261014/camera-fixed/`. Each retains `execution.json`, `source-snapshot.json`, `results.xml`, `editor.log` and a unique `captures/camera-exploration-*/evidence.json`. The original failed bundle is preserved separately.

## Reproduction and fix

The failed run exited **2**, with the exploration case failed. Its first unsafe observation was frame **108677**, approximately **93.853 s** into observation, while landing after a normal wall-facing jump:

| Recorded value | Observation |
| --- | --- |
| Player feet / yaw | `(-11.699013, 0.070393, 25.502113)` / `272.929321°` |
| Previous safe feet | Same X/Z, Y `0.073925`, previous frame |
| Camera position | `(-9.778874, 3.822746, 24.144306)` |
| Aspect / clearance radius | `16:9` / `0.557197869 m` |
| Nearby colliders | West wall interior face `x=-12`; rune plinth west face `x=-11`, top `y=1.1`; supporting floor |
| Retained failure | One blocked-camera warning, **749 unsafe observations**, approximately **0.543503 s** summed sampled intervals; no recovery before cleanup |

The one-metre channel fits the saved player capsule. During landing, the existing smoothed, desired and retained camera candidates all failed clearance. The 749 observations are sampling counts from an unrestricted batch Editor, **not displayed FPS** or a human performance measurement. The historical human attempt's seven warnings have no timestamped positions; this reproduction does not prove they occurred here.

The production change adds **16 lines** to [CameraFollow.cs](../../Assets/CameraFollow.cs). Only after the existing candidates fail, it tries a steeper boom with the authored height and horizontal heading, shortening horizontal reach to `2 * EffectiveCollisionRadius + collisionPadding`. It validates that candidate through the existing `TryResolve`, with the same pivot, near-plane sphere, padding, obstruction queries and ceiling checks. It does not reduce clearance, change serialized defaults or hide expected failures.

[LandingBesideLowPlinthRetainsClearCameraAndRecoversFullBoom](../../Assets/Tests/PlayMode/CameraFollowTests.cs) replays the recorded landing with real physics queries, checks boom/near-plane clearance including a ceiling, and checks full-boom recovery after departure. The separate [LowerRoomsRetainVisibleCameraDuringUnsnappedInputExploration](../../Assets/Tests/PlayMode/CastleCameraExplorationTests.cs) earns the first-fight and rune objectives through normal LMB/E input, then explores walls, corners and the plinth channel with W/A/D/Space. It never snaps or manually advances the camera. An order-32000 observer samples after ordinary camera/body-occlusion `LateUpdate`; any unsafe frame or unexpected warning remains a failure.

## Fresh result and capture review

The passing selection comprises **44 `CameraFollowTests` cases plus one exploration case**. XML duration was **100.011018 s**, runner duration **120.031 s**, exit **0**, with the runner power request restored. The exploration's XML duration was **99.853440 s**; its observation receipt records **99.421401 s**, **115,135 observations**, **986 retained periodic poses**, zero unsafe samples and zero blocked warnings. These durations are different scopes, not a player-route pacing result. The solver's intentional negative tests still emit expected safety warnings; the zero-warning claim applies to the exploration receipt.

The fixture explicitly sets the viewport to 16:9 before observation/input, matching the human/native 1280×720 aspect. The receipt records the original 4:3 radius `0.487029493 m` and requested/observed 16:9 radius `0.557197869 m`. The original aspect is restored before unloading through `OwnedSceneLoad`.

All **nine fresh 960×540 PNGs** in `camera-fixed/captures/camera-exploration-9379082f42b24b27aca0f9b9df945f02/` were directly inspected:

| PNG stem | Visible evidence |
| --- | --- |
| `00-saved-spawn`, `01-courtyard-wall` | Lit courtyard, player and wall contact |
| `02-entrance-wall`, `03-encounter-north-wall` | Close wall views with the player visible |
| `04-rune-lever-unsnapped` | Rune mechanism and normal gate-open feedback |
| `05-human-final-position-vicinity` | Close player/wall/torch/window view; HUD absent from this capture |
| `06-puzzle-southwest-corner` | Inside corner and doorway |
| `07-west-plinth-channel` | Visible player, wall, torch and plinth edge after the previously failing exploration |
| `08-puzzle-northwest-corner` | Inside corner and coloured window; controls strip partly clipped |

None is a black or magenta placeholder. These are URP render requests from the sampled unsnapped pose, including the saved camera-space HUD when rendered, without UI mode/layout conversion. The after-load assertion requiring all active canvases to belong to the owned scene passed. Shadowed or duplicate-looking HUD text nevertheless remains in several captures; the Canvas isolation change is **not a confirmed explanation or fix** for that appearance. Capture-resolution/layout behaviour and human UI readability remain unassessed. Close wall/player framing is also not artistic approval. The fixture never forces a disabled camera to render.

For exact source identity, the passing receipt records `CameraFollow.cs` SHA-256 `33e7a6f3d1271422412d89529b59e6c5c998d60544020d8cdcd1601d2a8ee595` and the exploration fixture `d3b8e1fef10a0fa1552304f3e85cf26739a0b064a2ef75b5ee81b1f1a42f225b`. Passing XML SHA-256 is `17a664bffa1647e3abb59acb5da4fac623fbee7df11a71ccf87033d46096a9e4`; its observation receipt is `b40548d7f5b637784f1733931446db95902b9b1bf8ee9616477843f8edd604b2`. The preserved failed observation receipt is `eb364358515ad35ec55b36259543e7b605a7b593273581ac6f0f610a7e850520`.

Full-suite, shipping-player, human timing, audio, performance and final visual acceptance remain separate from this focused camera result.
