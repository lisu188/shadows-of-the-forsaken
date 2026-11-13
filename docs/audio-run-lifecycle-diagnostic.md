# Passive audio and quit chronology for hidden routes

DOCX §3 describes exploration through the level. The owner's subsequent FMOD/native-crash request motivates this separate diagnostic; the document does not prescribe audio recovery code, device selection or a shutdown protocol.

The preserved historical native crash contains no usable stack or matching dump. Existing clean runs do not establish its cause. The [cause-evidence refresh](../artifacts/goal-20261014/native-audio-cause-evidence-refresh-20261016/independent-review.json) also finds that the earlier native mixer's passive configuration lease lasts only 0.2657017 s and closes before TestFinished and shutdown. That short lease remains valid for its recorded interval and is preserved unchanged.

This optional test-only companion observes the existing main and secret [concealed rendered routes](rendered-route-diagnostic.md). It retains a single run-owned audio lease across scene loads and both cases. The lease's identifier belongs to the diagnostic run, independently of the two gameplay sessions. Production progression, route controls, test assertions, PCM evidence and shipping assemblies are unchanged.

## Lifetime and evidence

The companion activates only in a Windows test player when `SHADOWS_AUDIO_RUN_LIFECYCLE_OUTPUT` names the exact active `SHADOWS_RENDERED_ROUTE_OUTPUT` directory. The launcher clears inherited overrides and sets this value only for the explicit option below.

Configuration-change callbacks capture managed scalar timestamps, thread and phase information into the existing bounded queue. Configuration reads and queue draining occur on the captured Unity main thread. An owned instance subscription to the existing rendered-route exit helper performs the last drain/read and closes acceptance **before** its existing `Application.Quit` call, during the normal update loop. No wait, new quit request or gameplay mutation is introduced.

The subsequent wants-to-quit, `OnApplicationQuit` and `OnDestroy` observations write managed timeline markers. They do not read audio configuration. An unexpected quit without the prepared boundary seals the lease without a configuration read and cannot qualify. This distinction also applies to failure paths; the old mixer's MonoBehaviour and its own cleanup semantics are not reused or altered.

The fresh evidence directory retains:

- `audio-run-lifecycle.jsonl`: bounded, flushed run/case/quit journal with immutable ownership and actual callback timestamps.
- `audio-run-lifecycle-prequit.json`: closed configuration observations and the journal-prefix digest, published before the quit call.
- `audio-run-lifecycle-completion.json`: final journal digest and actual destruction-callback outcome. Missing or partial terminal evidence remains a failure.

The pure verifier checks ownership, exact existing UTF case identities, callback order and time envelope, configuration-record provenance, bounds/drops/errors, pre-quit closure and terminal markers. It preserves registration-order differences between independent UTF callbacks. Synthetic verifier/lifecycle tests exercise the tooling; they do not manufacture natural Unity audio events or native acceptance.

Configuration callback details stay in the bounded in-memory lease until pre-quit publication. A native crash before that point can lose those details; previously flushed lifecycle markers remain available. The companion does not claim complete event persistence through an abrupt crash.

## Invocation and limits

Build the same two-case diagnostic using pinned Unity **6000.6.3f1** and reviewed source/import inputs. Then run with a fresh output directory:

```sh
python3 tools/run_standalone_rendered_routes.py --player ABSOLUTE_PLAYER_EXE --output FRESH_ABSOLUTE_OUTPUT --build-receipt ABSOLUTE_TEST_BUILD_JSON --build-receipt-sha256 ACTUAL_SHA256 --observe-audio-lifecycle
```

The option retains all original **23** qualification checks and requires a **24th** passive-lifecycle check. Without it, the original 23-check profile remains available and the new override is cleared. An opted-in missing, malformed or incomplete companion cannot be treated as an unobserved success. Existing fatal-log, FMOD/no-sound, crash-file, normal self-exit, concealment, identity, resource and wake-state checks remain mandatory.

No audio reset, endpoint/service/driver/volume change or synthetic native configuration event is performed. Zero natural configuration callbacks means none were observed during the retained lease; it does not demonstrate recovery. Configuration metadata does not identify the selected native stream or prove physical audibility. Managed quit markers and normal process closure still do not attribute a native teardown fault without native evidence. GPU/display timing, ordinary Release route performance and human acceptance retain their separate scopes.

The first actual Unity build at `ea3083e` failed with CS0579: the Main test assembly already had a `TestRunCallback` registration, and the new companion added a second. Its [failed build evidence](../artifacts/goal-20261014/audio-run-lifecycle-native-build-20261016/execution.json) is retained; it produced no player and has no native route result. The correction uses one dispatcher that forwards the original callback first and the optional companion in `finally`. The observer also lives outside the unchanged route fixture's gameplay-namespace suspension rule, so it can keep draining during the real routes.

The [expanded API compilation](../artifacts/goal-20261014/audio-run-lifecycle-composite-aggregate-preflight-20261016-v4/summary.json) reproduces the original duplicate-registration error, then compiles the corrected Main assembly in Editor and player configurations. This is source/API evidence, distinct from Unity execution. The first companion player at `c8bb588` completed both routes and exited normally but remains rejected at **23/24**: the hidden exit helper was never attached, so no pre-quit record was published. The actual focused Unity tests reproduced that discovery failure at **1/2**.

At **`971ef31`**, discovery uses `Resources.FindObjectsOfTypeAll` while retaining the existing unique-owner/name/flags/scene/activity checks. Unity's [pinned API documentation](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/Object.FindObjectsByType.html) confirms that the previous lookup excludes `DontSave` objects. The unchanged attachment assertion then passed in the actual **2/2 focused Unity repeat**. The [final source checks](../artifacts/goal-20261014/audio-run-lifecycle-source-checks-repeat5-20261016/summary.json) also pass.

The fresh native repeat passes **both routes and all 24 checks**, including the 13-marker journal and actual pre-quit closure. It self-exits 0 without intervention and contains no detected FMOD/no-sound marker, native fatal section or crash file. Initial and final configuration reads occur on thread 1 at 48 kHz Stereo; zero natural configuration callbacks were observed. The [validation report](validation/audio-run-lifecycle-2026-10-16.md) binds the independent runtime/audio reviews and preserves both failed attempts. The completed `87ddbbf` two-route/23-check result and ordinary `23b3ac0` Release remain separately scoped; these test-only changes do not establish the historical crash cause, recovery, physical audibility or human acceptance.
