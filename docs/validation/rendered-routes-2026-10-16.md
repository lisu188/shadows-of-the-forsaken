# Concealed rendered routes — 2026-10-16

The separate Development/UTF player built from **`87ddbbf8f2489881aec1bae3ece831377fe33f26`** passed both existing full-route cases and all **23 qualification checks**, then exited itself with code 0. Graphics and audio remained enabled. No FMOD failure marker, native fatal section, callback failure or crash dump was detected. This is a clean repeat of the instrumented native routes; it does not establish the cause or permanent repair of the earlier FMOD/Mono failures.

DOCX §§2–5 describe the atmosphere, rooms and visual load; §§3 and 6–8 define progression, the main route and optional content. This diagnostic supplies engineering observations for #24 while retaining the existing mandatory library, throne miniboss, catacomb final fight and optional relic/return route. It does not supply first-time human timing or owner visual/audio acceptance. The document specifies no profiler, numeric frame budget or memory limit; those diagnostic policies are implementation choices described in [the profile contract](../rendered-route-diagnostic.md).

## Source and build

The pinned **Unity 6000.6.3f1** build has **0 errors and 0 warnings**, a 17.9567018 s BuildReport duration, and **349 Unity payload files / 200,989,313 bytes**. The separate 1,221-byte `test-build.json` makes the identity manifest **350 files / 200,990,534 bytes**. Its two-scene list contains the generated UTF bootstrap followed by `Assets/Scenes/ForsakenCastle.unity`. Autorun and connection back to the Editor were disabled. The root-controlled build process exited normally; its temporary wake request was restored.

The [independent build review](../../artifacts/goal-20261014/rendered-routes-native-repeat2-build-independent-review-20261016/independent-review.json) checks all source/import archive payloads, **774 source inputs, 777 final imported inputs and 519 unchanged critical inputs**, plus all 350 player files. It reviews 18 importer changes individually, including unreferenced editor material subasset IDs and document order; no gameplay material property change is inferred from that normalization. All 20 overwritten cache inputs have verified preimage backups. Both earlier generated UTF files are preserved outside the live cache.

| Receipt | SHA-256 |
| --- | --- |
| [Build receipt](../../artifacts/goal-20261014/rendered-routes-native-build-repeat2-20261016/player/PlayerWithTests/test-build.json) | `083131d6631821101f0ea7b5f198453b087222dc73d16b9eb0b8165618ec019c` |
| [Source archive](../../artifacts/goal-20261014/rendered-routes-native-build-repeat2-20261016/source.zip) | `aee381554830664108487a8d74d6a377b506001109976ac85e9b93145d33b43f` |
| [Imported archive](../../artifacts/goal-20261014/rendered-routes-native-build-repeat2-20261016/imported.zip) | `fe658a4408888125ac6372bd3539ec3cdf0df42cfbd1baadd0448dc538657078` |
| [Execution receipt](../../artifacts/goal-20261014/rendered-routes-native-repeat2-20261016/execution.json) | `3e925ad29b9b82a0b1279e5766cb36cfccf44c1bab47479ff2f4385b412ca2bf` |

The [independent production/shipping and C# runtime review](../../artifacts/goal-20261014/rendered-routes-production-and-csharp-runtime-review-20261016/independent-review.json), SHA-256 `2be1b5cacdad3e0019a0120fd50755ac786768c431cc9253cdc2467e18e7f0e4`, verifies **652 production Unity files** outside `Assets/Tests` identical to game source `23b3ac0`. It rechecks all 199 shipping files, the delivery receipt/source archive and all 200 ZIP entries by CRC/SHA-256. The current ordinary [Release delivery](minimum-travel-native-release-2026-10-16.md) and its recorder pins remain the separately verified `23b3ac0` delivery promoted at `ae78d21`; this test-only instrumentation does not create a new shipping package.

## Native repeat

The [closed runtime output](../../artifacts/goal-20261014/rendered-routes-native-repeat2-20261016/execution.json) records PID **30160**, runner start **20:05:58.547200 UTC**, observed self-exit **20:09:24.220403 UTC**, and **205.688 s** host monotonic lifecycle time. Its recorded wrapper-closure UTC is 20:09:24.224407 (205.677207 s from start); later postmortem validation/publication is outside that lifecycle interval. The runner observed Medium integrity, an actual owned live parent, a fully offscreen alpha-zero nonactivating host and a visible child suitable for ordinary rendering. Its sampled guard detected no exposed/foreground game window. Both cases were observed after their own scene/camera/session readiness receipts. Polling cannot exclude changes between observations.

Every qualification check passed: exact XML/callback/completion identities; both result/ready hashes; route metrics and existing capture records; self-exit without cleanup intervention; concealment, readiness, visibility and live ownership; destroyed owned host and restored wake request; unchanged 350-file identity; bounded resources; reviewed logs with normal `ClientWorkerJobs`; and no fatal audio/native/dump or callback failure. The requested crash directory is empty. The full window history, bounded observer details and file inventory remain in the output.

The [independent runtime replay](../../artifacts/goal-20261014/rendered-routes-runtime-repeat2-independent-review-20261016/independent-runtime-review.json), SHA-256 `1fd8df536278f5f6b62d39cd3c855069b8b4d88cc1a51767579a5f1c1289745f`, passes with no errors. It checks every retained window row and readiness prefix, all 31 captures, source/build/import/backups and complete binary identities, the corrected schemas, histogram accounting and final inventory. The four exact inactive post-processing shader messages retain their explicitly checked scope; additional messages cannot silently qualify. Runtime/player bytes remain unchanged by the audit.

| Existing case | Result | Session seconds from input trace | Final health | Ordinary unbound render cycles | Logical visits / state samples |
| --- | --- | ---: | ---: | ---: | ---: |
| `CastleMainRouteCompletesThroughControlsWithoutSecret` | Passed | 81.66049194335938 | 200 | 52,221 | 8 / 83 |
| `CastleSecretRouteReturnsThroughThroneAndStillRequiresFinalFight` | Passed | 121.22715759277344 | 200 | 84,081 | 12 / 123 |

Both routes use their unchanged InputSystem fixture controls, normal Unity updates and time scale 1. They do not prove OS input or a first-time human's 2–3 minute route. All ordinary context/camera begin/end totals balance and rejected-cycle counts are zero. The 11 main and 12 secret capture brackets account for their explicit render requests separately. Completion freezes the original session and flags before the unchanged restart; five post-completion contexts per case remain outside the route measurement.

Per-callback detail retention is bounded at **256 records per case**, with **208,736 main / 336,184 secret details dropped** after that cap. Balanced complete counters and visit histograms remain recorded; they must not be described as a complete per-callback trace. Window history has its own separately verified complete retained scope.

The exact capture contract verifies **31 files: 23 RGB PNGs at 960×540, six combat-framing JSONs and two input traces**. The secret trace preserves the optional relic and return path while still requiring the final fight. These screenshots are captures from the test player's explicit capture method; their rendering and crossing intervals are excluded from performance timing.

![Secret route completed at 200 HP with the hidden relic](../../artifacts/goal-20261014/rendered-routes-native-repeat2-20261016/captures/CastleSecretRouteReturnsThroughThroneAndStillRequiresFinalFight-route-complete-secret.png)

## Read-only audio observations

The [closed endpoint sampler](../../artifacts/goal-20261014/audio-topology-rendered-routes-repeat2-20261016/completion.json) records **1,128 samples / 300.0163151 monotonic seconds**, from **20:05:48.0482153 to 20:10:48.0684928 UTC**, and normal exit 0. The sampler reads all three render-default roles and endpoint states; it performs no device, stream, service, power, input or window mutation.

The [independent audio audit](../../artifacts/goal-20261014/rendered-routes-runtime-repeat2-independent-review-20261016/repeat2-audio-independent-review.json), SHA-256 `b3dbd6ddbfe6e903cfef35544448d7acd63e55c6e312762e9ee4f3bc7a07ffc5`, verifies the closed history hash and **772 overlapping samples** across the recorded runner start/closure span and all six UTF callbacks. Every sampled HRESULT is 0; all three default roles remain ACTIVE Realtek with one unchanged topology signature. The maximum overlapping inter-sample gap is **0.2986709 s** (whole history **0.3149002 s**). Sequential polls can miss changes or races between reads. These observations establish sampled endpoint state during this run, not audible output, continuous stability or Unity stream recovery.

The additive [publication-clock/audio annex](../../artifacts/goal-20261014/rendered-routes-runtime-repeat2-independent-review-20261016/publication-clock-audio-annex.json) also brackets the inventory's authored UTC and final-file write observations with actual audio samples. It preserves the distinction between the lifecycle interval and later publication; no shell-tool return timestamp or continuous observation is inferred.

## Render-boundary measurement scope

The runtime reports **Intel Core i5-14400F / 16 logical processors**, **NVIDIA GeForce RTX 4060 Ti / Direct3D11**, Windows 11 build 26300, 32,598 MB system memory and 7,949 MB graphics memory. The game camera renders **1280×720**, quality `PC`, URP `PC_RPAsset`, render scale 1, MSAA 1, VSync 0, target frame rate -1 and on-demand interval 1. The physical display's reported 3840×2160 dimensions do not change the camera's measured resolution. The scene camera already has post-processing disabled; the existing preparation verifies that state and inactive guarded effects.

Initial main-Courtyard metadata observes pipeline initialization from null to URP. Later sampled main configuration and all sampled secret configuration fields remain unchanged; changes entirely between observations are not excluded.

Ordinary end-to-end SRP boundary intervals include intervening game work. Capture brackets and intervals crossing capture/room/session boundaries remain excluded. Main retains **52,203 raw / 27,438 warm measured intervals**; secret retains **84,059 raw / 46,994 warm**. These are separate visit histograms, not a combined session or hardware score. The greatest eligible cold-inclusive intervals are **6.1842 ms in main FirstEncounter** and **3.9231 ms in secret FirstEncounter**. A largest observed interval identifies a place to inspect; it does not attribute cost to a particular light, effect or shader.

The warm lane excludes the first five eligible seconds of every visit. Main Library and secret Library on both visits, plus the return to ThroneRoom, have no warm measured samples. They remain explicitly unqualified for the provisional room budget. All long eligible intervals remain present. Sampled managed/Unity allocator pools can miss short peaks and do not represent complete native/process or GPU memory. GPU completion, display presentation/FPS and ordinary Release performance are not measured.

The [independent readable performance report](../../artifacts/goal-20261014/rendered-routes-repeat2-performance-analysis-20261016/observed-performance.md) retains every visit, raw/warm count, exclusion, unobserved tail, histogram result and sampled memory range. Its [machine-readable analysis](../../artifacts/goal-20261014/rendered-routes-repeat2-performance-analysis-20261016/observed-performance-analysis.json), SHA-256 `003b55dd6603e177bc8ed8a44c29a4a55a834e8451e4d1ee97303729a4719ccf`, passes **4,835 independent checks with zero mismatches**. All 20 raw and 16 nonempty warm lanes meet the local provisional 33.3 ms mean/p95 budget; the four empty warm lanes remain unavailable. The highest p95 bin upper bound is **2.50 ms, main FirstEncounter**. No eligible interval exceeds 16.667 or 33.3 ms. This is measurement/budget evidence for this concealed workload, not final hardware or displayed-FPS qualification.

| Sampled peak | Main route | Secret route |
| --- | ---: | ---: |
| Managed estimate | 12,091,392 B | 12,685,312 B |
| Unity allocator used | 191,307,239 B | 195,536,837 B |
| Unity allocator reserved | 584,646,656 B | 586,743,808 B |

Available Main Thread samples report **1.0467–2.6332 ms / 1.1299–2.2472 ms** at read time for main/secret respectively, with 82/122 retained valid observations. They cannot be assigned to an originating room/frame. SetPass, shadow-caster and triangle counters retain their actual Render/Count metadata and values in the analysis. Unavailable Render Thread/draw counters, short visits and missing GPU/display timings remain explicit rather than replaced or inferred.

## Preserved first attempt and corrections

The [first attempt](../../artifacts/goal-20261014/rendered-routes-native-20261016/execution.json), source `1dffab2`, passed its two route cases and self-exited 0, but the overall diagnostic remains **rejected at 22/23**. Its strict camera-rectangle validator incorrectly expected four keys. Actual Unity JSON includes `serializedVersion: "2"` alongside `x`, `y`, `width` and `height`. The corrected validator requires exactly those five keys, string version `"2"`, finite coordinates and positive extents, rejecting unknown fields and wrong versions. The old receipt and files were not rewritten or promoted.

The first profiler selected the first matching counter name, which returned UI Toolkit's `Draw Calls Count`. The correction requires the intended category and exact name: Internal for thread counters, Render for draw/SetPass/shadow/triangle counters. The fresh repeat explicitly records **Render Thread and Render/Draw Calls Count unavailable (`not-enumerated`)**. Its available counters keep their actual category/unit/validity/count; zero retained samples are not a measured zero workload. At-read statistics do not establish their originating frame or room.

## Source checks and remaining acceptance

The [actual source-check commands and unchanged-source receipt](../../artifacts/goal-20261014/rendered-route-repeat2-source-checks-20261016/summary.json) record:

- DOCX/level contract and Unity project integrity passed.
- Full Python discovery in WSL: **383 discovered, 370 passed, 13 Windows-only skips**.
- Focused rendered-route Python tests on native Windows: **63/63**.
- Pure C# rendered-route metrics/selector tests: **30/30**. These do not simulate MonoBehaviour.
- Separate [pinned API compilation](../../artifacts/goal-20261014/rendered-route-profiler-selector-preflight-20261016/source-and-api-summary.json): **5/5** assemblies compiled with warnings treated as errors; this is source/API proof, distinct from the actual build and two-case native execution.

The earlier full Windows discovery remains failed by an existing symlink fixture's `WinError1314`; no Windows permission setting was changed. The previous production **238/238 PlayMode**, native10, presentation3, PCM diagnostic and one-hour Release results retain their original source/profile scope. The full 238-case catalog and full EditMode suite were not rerun for this test-only change; the new native run selects exactly two existing cases.

First-time human basic/secret completion, human pacing/readability, owner art acceptance, physical audible output, a full ordinary Release route, GPU/display timing and individual light/effect cost remain open. Short/empty warm visits cannot qualify room performance. Clean observed runs and stable sampled endpoints do not establish general FMOD stream recovery or the cause of the old native crash.
