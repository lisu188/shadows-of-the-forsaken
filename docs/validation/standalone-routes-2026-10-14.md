# Hidden Windows route verification — 2026-10-14

The primary castle's instrumented Windows test player passed **10/10 cases**, with no failures, skips or inconclusive cases, audio and D3D11 enabled, and a normal self-exit **0**. This extends the [earlier audio/native investigation](audio-native-2026-10-14.md) beyond idle startup/shutdown to complete fixture-controlled gameplay. It supports DOCX §§3–4 and 6–8 and is separate from a human playthrough or shipping-player qualification.

## Implementation and retained failures

The existing `FullCastleRouteTests` now compiles in a test player. It retains the six gameplay cases and four framing cases, saved input bindings, real combat/interactions, scene-load deadlines, assertions and cleanup. Only Editor APIs are conditional; the Editor-active fixture text remains unchanged. The public pinned Test Framework 1.8 build hooks preserve its bootstrap and the primary castle scene, disable automatic launch and Editor connection, and write the finalized BuildReport after `BuildPipeline.BuildPlayer` returns. The runtime callback saves exact NUnit XML, case events, hardware metadata and completion hashes before requesting application exit.

The first build is preserved with its incomplete pre-final size/duration receipt. The corrected second build succeeded, but its native run was correctly rejected at **9/10**, exit **1**: the first final-death case encountered four unhandled startup logs for stripped, inactive post-processing passes. Gameplay/reset assertions completed. [V2's independent review](../../artifacts/goal-20261014/standalone-routes-v2-independent-review.json) preserves that failure, unchanged player files and all 39 rendered images; it has not been relabelled a pass.

The player fixture now checks that camera post-processing is disabled and configured depth-of-field/Panini effects are inactive. Before first pipeline creation it expects only the exact pinned messages for actually unavailable shaders: Gaussian twice, Bokeh once and Panini once. Already initialized matching URP renderers enqueue no startup expectations. Every other log remains subject to the existing strict checks. No shader, audio, gameplay, assertion or timeout was disabled. The [source/API proof](../../artifacts/goal-20261014/player-pipeline-preparation/summary.json) records these conditions and unchanged Editor-active text.

Separately, unsnapped exploration reproduced and fixed a real landing-related camera suspension. Its [camera report](camera-exploration-2026-10-14.md) records **45/45 focused passes**, the preserved failure and unchanged collision constraints. This camera source change is included in V3; the earlier October 8 shipping ZIP remains unchanged.

## Qualified V3 result

The [finalized build receipt](../../artifacts/goal-20261014/standalone-route-build-v3/player/test-build.json) records `Succeeded`, **0 errors / 0 warnings**, **200,752,748 bytes** and **21.7254192 s**. The frozen directory contains 343 build files plus the 655-byte receipt: **344 entries / 200,753,403 bytes**. It is a development test player built with UTF's checked/background-running configuration, rather than the ordinary Release player.

Source provenance is base `0408fa9367ebef8fcab0a91bf20f1a4582107d95` plus the [Git-index LF overlay](../../artifacts/goal-20261014/camera-fixed-overlay.json). All **728 native source inputs** match the index and isolated LF checkout. Actual pre/post inventories contain **733/735 files**, explicitly retaining previous UTF bootstrap files and generated scene-template settings. The [independent camera/build review](../../artifacts/goal-20261014/camera-fixed-v3-independent-review-final/summary.json) resolves all import pairs with zero outstanding findings. It distinguishes reviewed material/settings serialization from production code changes. Build Editor shutdown allocation/debugger diagnostics remain in its log; zero BuildReport warnings is not a wholly clean-log claim.

The hidden runner uses the normal renderer with **ClientWorkerJobs**, an invisible, nonactivating runner-owned parent, and medium-integrity launcher/player tokens. A [live independent query](../../artifacts/goal-20261014/standalone-routes-v3-live-review.json) verifies the actual `GetParent` relationship and 1280×720 child window, with neither host nor child visible or foreground. Hardware was an Intel i5-14400F, RTX 4060 Ti and Windows 11. No audio-disabling or null-graphics flag was supplied. Python sends no gameplay or focus input; the fixture supplies InputSystem events.

The [execution receipt](../../artifacts/goal-20261014/standalone-routes-v3/execution.json) records **435.047 s** runner time and all 14 qualification checks passing. [NUnit XML](../../artifacts/goal-20261014/standalone-routes-v3/results.xml) records **430.9392252 s**, all ten exact case identities passing, and no skips. The formerly failing final-death case now passes with the four specific inactive-pass logs consumed. Both normal and optional routes, enemy-inflicted death/restart at all three fights, locked-gate walking/jumping and framing checks passed.

| Known-route observation | Basic | Secret |
| --- | --- | --- |
| Session seconds | 81.5287933 | 121.1197205 |
| Final health | 200 | 200 |
| Final state | Exit; all mandatory objectives | Exit; all mandatory objectives plus secret lever/relic |

These fixture-controlled times do not qualify the DOCX's human 2–3 minute target. The secret path physically returns through the throne shortcut and still requires the final fight.

The player exited itself with code 0; no WM_CLOSE or forced termination was needed. The owned parent was destroyed, the temporary wake request restored, and all 344 frozen entries remained unchanged. Player log and launcher stdout/stderr contain **no FMOD failure or native-fatal marker**, and the requested crash folder is empty. The inactive shader diagnostics remain preserved. Exact case events and completion hashes are in [completion.json](../../artifacts/goal-20261014/standalone-routes-v3/completion.json) and the [raw evidence inventory](../../artifacts/goal-20261014/standalone-routes-v3/evidence-file-inventory.json). The [independent final review](../../artifacts/goal-20261014/standalone-routes-v3-independent-review.json), SHA-256 `c48a93701ff549dddac34b126fd1a3c9f2731f50b1417a46febf0d4f1c6cbe6f`, rehashes all 344 player entries and 67 raw evidence entries, checks all 39 PNGs and nine framing receipts, and verifies the exact XML/event/completion correspondence.

Fresh native images are retained in [V3 captures](../../artifacts/goal-20261014/standalone-routes-v3/captures):

![Native library mechanism](../../artifacts/goal-20261014/standalone-routes-v3/captures/CastleMainRouteCompletesThroughControlsWithoutSecret-route-library.png)

![Native optional-route completion](../../artifacts/goal-20261014/standalone-routes-v3/captures/CastleSecretRouteReturnsThroughThroneAndStillRequiresFinalFight-route-complete-secret.png)

## Source checks and remaining scope

The unchanged DOCX/contract passes with 29 basic / 63 secret reachable states. Unity source integrity passes (328 assets / 891 tracked files / 372 GUIDs). Fresh WSL Python execution has **206 cases: 193 passed and 13 Windows-only skips**; the preserved failed historical-fixture run is separate. The 13 recorder cases have a retained Windows pass, with unchanged recorder/test bytes verified. Progression's **29/29 C# tests** ran earlier in this work; their sources remain unchanged. The delivery manifest additionally accepts an exact installed direct Editor path while retaining Hub pin verification; 35 focused manifest cases passed in WSL. These scopes are retained under the [final source checks](../../artifacts/goal-20261014/delivery-source-checks) and [earlier C# receipt](../../artifacts/goal-20261014/standalone-source-checks/progression.trx). The required PlayMode catalog is now 230; the previous full 228 pass and new focused 45 pass are separate executions, not a new full 230/230 run.

The earlier long-session Mono shutdown fault remains unreproduced and undiagnosed. Scoped wake requests mitigate the observed monitor-power/audio-endpoint churn; these shorter stable runs do not establish an engine repair or cover explicit lock/remote/device-removal transitions. Audible output, foreground OS controls, human pacing/readability, hardware performance and final owner acceptance remain open. The test player and its receipt do not replace the shipping player's identity.
