# Saved-controller microstep grounding — 2026-10-16

A new hidden native capability diagnostic exposed a strict landing failure in lower-room exploration. A separate saved-scene Unity regression then reproduced a concrete controller defect: with its 1 mm minimum movement threshold, the primary scene lost floor contact and rejected a fresh jump during small positive movements. The proposed correction sets only the saved controller and its builder to `minMoveDistance = 0`. **The unchanged regression passed 1/1 after the correction. Full-suite and rebuilt-player verification remain pending.**

This supports DOCX §3 exploration and the adopted #4 movement/jump controls. The minimum movement threshold and diagnostic step duration are implementation choices, not DOCX values. The DOCX, route topology, optional content, player dimensions, speed, turn rate, gravity, jump force, navigation and progression remain unchanged. Unity/package pins and existing asset GUIDs are preserved.

## Preserved discovery

The [BitBlt-requested native capability diagnostic](../../artifacts/goal-20261014/standalone-presentation-bitblt-capability-20261016/results.xml) ran the unchanged `cdd96da` player and failed **0/3**, XML **79.6872064 s** / runner **83.672 s**. Both ordinary-frame HUD targets remained black, with zero SRP callbacks. Exploration failed `Jump must land` at the puzzle west wall after six checkpoints; its evidence remains incomplete, with zero unsafe-camera observations and blocked warnings. The character's last sampled position was approximately `(-11.68, 0.02, 21.96)`.

The launch requested Unity's documented `-force-d3d11-bitblt-model` flag, while keeping the same hidden host, audio, D3D11/ClientWorkerJobs, player bytes, assertions and deadlines. The actual swapchain type was not directly observed. Neither the landing failure nor its cause is attributed to the flag. PID 17232 self-exited 1 without intervention; hidden Medium parenting, unchanged 348-file identity, host destruction and wake restoration were verified. No detected FMOD/native-fatal marker or requested crash artifact appeared. The [independent diagnostic audit](../../artifacts/goal-20261014/standalone-presentation-bitblt-capability-independent-review-20261016.json) has SHA-256 `4119a588afcd433f428a0048bbfd7a801908725b0225ea3bffd56cb7f22e94da`.

A [managed rendering audit](../../artifacts/goal-20261014/managed-render-suppression-audit-20261016.json), SHA-256 `c60aea3d1bd9f5616f3bbecf512965d6846e32cdf2b8c1980068aff34dfd1182`, found no confirmed render suppression in the pinned project/UTF/URP managed sources. This does not prove the native engine's hidden-window scheduling cause. Native ordinary-frame HUD verification and the historical Mono crash cause remain unresolved.

## Actual red regression

Clean LF commit **`09853a87889014b6a291c192944379751e030e4f`** adds one saved-scene regression and its exact required-catalog entry. No production setting had changed for this run. The catalog now contains **234 PlayMode / 187 EditMode cases**; this focused execution ran only one case.

`CastleCameraExplorationTests.PrimarySavedControllerRetainsGroundContactAtMicrosteps` loads the actual primary scene, settles with 60 normal component steps, samples 32 neutral steps of 0.0001 s, injects one Space event and measures physical lift through 2,000 additional small steps. It calls real production `PlayerMovement.Simulate` and `CharacterController.Move` on the saved floor. The measured phase has no yield where ordinary Update could interleave. The measured phase does not set the controller threshold, reposition the player, change the floor/frame caps or force rendering. It retains the shared fixture's time-scale-1 setup and restoration. All measurements are retained before assertions.

The [red XML](../../artifacts/goal-20261014/saved-controller-microstep-red-20261016/results.xml) records **0 passed / 1 failed**, no skips, **1.010491 s** total / **0.976217 s** case. Its failure is the ground-contact assertion, with these complete diagnostic values:

| Red measurement | Actual value |
| --- | --- |
| Saved minimum move distance | 0.001 m |
| Normal settled height / vertical velocity | 0.01999991 m / -2 m/s |
| Lost neutral ground samples | **32 / 32**, first at step 0 |
| Height after neutral steps | 0.01999991 m |
| Fresh jump velocity | **-2.031392 m/s**; jump rejected |
| Highest height / physical lift | **0.01999991 m / 0 m** |

The [red execution receipt](../../artifacts/goal-20261014/saved-controller-microstep-red-20261016/execution.json) records pinned Unity 6000.6.3f1, **754 exact committed inputs**, audio/graphics enabled, no overlay, PID 50444, normal Unity test-failure exit **2**, runner elapsed **55.750 s**, no stop, terminal-before-snapshot and wake restoration on the same thread. The outer runner exits 1 because it requires successful tests; that assertion does not change the failed NUnit result into a native crash. The **755-input imported ZIP** and changed cache sources remain retained.

Pinned Editor/native API compilation passed **2/2** after an unsupported `Assert.Multiple` preflight call was corrected to ordinary assertions following the complete measurement. Both static attempts remain retained. The final [compile receipt](../../artifacts/goal-20261014/saved-controller-microstep-compile-20261016-v2/summary.json) has SHA-256 `855720a331a56a0ecf72586c1dfe84a1ec55ba4dd3e846a56a68b03ace735b6f`. Static compilation is separate from the actual red engine result.

## Focused green result

Clean LF commit **`9d209233f6c3a3d5fe33672e859bb71c6ff4e0c1`** changes only the builder assignment and saved scene value among the 754 Unity inputs. The regression remains byte-identical to the red run (`b008a20e1b2784798b9073c328ca21447b916e6a0fc789734abade06bff80c2d`). The [green XML](../../artifacts/goal-20261014/saved-controller-microstep-green-20261016/results.xml) passed **1/1**, no skips, **0.9907749 s** total / **0.946018 s** case. The [execution receipt](../../artifacts/goal-20261014/saved-controller-microstep-green-20261016/execution.json) records **27.547 s**, PID 19824, exit 0, no stop, terminal-before-snapshot and same-thread wake restoration.

| Measurement | Red: threshold 0.001 m | Green: threshold 0 m |
| --- | --- | --- |
| Lost neutral ground samples | 32/32 | **0/32** |
| Fresh jump velocity | -2.031392 m/s | **4.999019 m/s** |
| Highest height | 0.01999991 m | **0.824103534 m** |
| Physical jump lift | 0 m | **0.8041036 m** |

The [independent red audit](../../artifacts/goal-20261014/saved-controller-microstep-red-independent-review-20261016.json), SHA-256 `fde9128c8079cf28ccfddeef4591df032ac41c43f10ba9d67aa4900eea17ae39`, and [green audit](../../artifacts/goal-20261014/saved-controller-microstep-green-independent-review-20261016.json), SHA-256 `57cb60a328ea11e318bf2655a6bb27101a1671a5eaad8bc6f92affffe328dece`, verify all committed/clean-clone inputs, both 755-payload ZIPs and CRCs, exact results, unchanged regression and cleanup. Their normalized material/settings import pairs are reviewed; imported bytes are not all identical to source. The [production patch review](../../artifacts/goal-20261014/saved-controller-production-patch-independent-review-20261016.json), SHA-256 `d26d2c1031346da5847e9dd9a1c5a2bad07504ea50814a73d411bc58012c905f`, binds the exact two production changes.

This controlled A/B establishes that removing this threshold fixes the demonstrated saved-component grounding/jump defect. It does not prove the sole cause of the earlier natural-route landing failure, native ordinary rendering gap or historical Mono fault.

## Full-suite evidence isolation follow-up

The first [full PlayMode run](../../artifacts/goal-20261014/grounding-full-playmode-234-20261016/results.xml) on `9d209233` recorded **234/234 passed**, **729.2738891 s** XML / **745.719 s** runner, normal exit 0 and restored wake request. It exposed a fixture evidence defect after the new second case was added: NUnit reused the fixture instance, and the component case inherited the preceding route's capture history and completion flag. The local physical grounding/jump measurements were independent, but the second capture bundle falsely listed earlier captures. This run is preserved as provisional and does not qualify the complete capture bundle.

The fixture now resets all per-case measurement state, refuses to discard an unreleased scene/resource lease, and uses a separate explicitly labelled microstep evidence directory. Both test method bodies, assertions and deadlines are unchanged. The original route retains its existing evidence schema and limitations. [Pinned API/source preflight](../../artifacts/goal-20261014/camera-fixture-case-isolation-compile-20261016-v2/summary.json), SHA-256 `3921e36e0301e219d635f9fb300d9975354324b544ada214a0913adbd45e4a6d`, passed Editor/native compilation 2/2. A new complete PlayMode run will verify the two independent case bundles and the full catalog.

The [first EditMode check](../../artifacts/goal-20261014/grounding-editmode-187-20261016/results.xml) separately passed **187/187** on `9d209233` and the corrected production scene. It preceded this PlayMode-only fixture isolation change; a final same-source check remains pending.

## Correction and pending validation

The correction changes one existing scene value and adds its explicit builder assignment. It follows Unity's [Character Controller guidance](https://docs.unity3d.com/6000.0/Documentation/Manual/class-CharacterController.html), which describes minimum-distance rejection and recommends zero for most controllers. The separate runtime scene's builder already selects zero. The production movement code and the regression are unchanged.

Focused green passed; affected full movement/camera/route validation, the complete required Editor suites and rebuilt player evidence remain pending for this correction. Earlier `e4d58a1`/`cdd96da` results do not validate this changed scene value. The current shipping package still contains the earlier setting; its identity and historical results remain preserved. No native HUD, crash-root-cause, human timing/audio or hardware-performance acceptance is inferred.
