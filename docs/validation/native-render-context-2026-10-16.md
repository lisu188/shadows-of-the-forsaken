# Hidden native render-context follow-up — 2026-10-16

The current [grounding candidate](controller-grounding-2026-10-16.md) passes complete Editor suites, native ten-case routes and ordinary Release startup/shutdown. Its strict native presentation remains failed 1/3: ordinary HUD render targets are black, with no observed camera callbacks. This follow-up supports DOCX §§4/7 presentation verification without changing authored content or substituting explicit render requests for ordinary-frame assertions.

## Measurement change

Only the existing native-test branch of `PrimaryCastleHudRenderingTests` changes. Its read-only probe additionally subscribes to SRP begin/end context callbacks and retains bounded frame/camera-count/owned-camera-membership samples. Each of the existing three presentation phases reads the actual OnDemandRendering interval/prediction, screen and camera viewport dimensions, enabled-camera membership, display activation/dimensions and the count of `FinishFrameRendering` entries in the current player loop. It neither changes those values nor renders, activates a window or alters timing, pose, input or progression. All original assertions, pixel thresholds, yields, deadlines and exact three-case selection remain unchanged. Subscriptions are removed through the existing disposal and teardown paths.

Pinned URP 17.6 enters its context callback scope before iterating the camera list, including an empty list. Context observations can therefore distinguish an observed empty-camera render context from absence of observed URP context callbacks; zero callbacks alone do not establish an engine root cause or GPU completion. Unity's [context callback API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.RenderPipelineManager-beginContextRendering.html) and [render interval API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.OnDemandRendering-renderFrameInterval.html) describe the measured interfaces. The prediction is recorded as diagnostic state, not proof that a frame was rendered.

[Pinned API compilation](../../artifacts/goal-20261014/native-context-probe-api-compile-20261016/summary.json) passed Editor/native **2/2**, with no compiler output. The selected Editor source remains byte-identical to the preceding candidate after preprocessing (`22786089b95bf6fbef3f05629102c7ece2dcf7624b8d925ebc81110cc307dba0`); the native fixture source has SHA-256 `6357c5a96171a38a7f630abe64430efebe69b50acd8c550b28050116c1ab6ef9`. This is API/source proof, not engine execution.

## Pending engine evidence

A fresh exact-source native presentation build and hidden three-case execution are pending. No new runtime result, ordinary rendering, human timing/audio, hardware performance or historical Mono crash causality is claimed. The qualified ordinary Release and recorder pins remain unchanged; failed runs, imports, players and screenshots remain preserved.
