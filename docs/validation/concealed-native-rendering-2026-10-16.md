# Concealed native rendering — 2026-10-16

The visually concealed native experiment renders ordinary frames and passes both saved HUD cases. After the first-render fixture correction, its latest complete presentation profile remains **failed 2/3**, now solely because FMOD failed during an observed Windows playback-default/device transition. The original never-shown profile remains **failed 1/3**. These results advance DOCX §§3/4/7 presentation verification but do not establish complete native acceptance, ordinary Release traversal or human acceptance.

## Conditions and retained failures

The preceding [native render-context report](native-render-context-2026-10-16.md) records the original **1/3** result: explicit exploration captures worked, but ordinary HUD targets were black and the unbound observer saw no callbacks outside explicit captures. The earlier transparent host did not maintain Unity child visibility during tests: twelve visible-child samples occurred at startup, followed by 383 shown-host samples without a visible child.

The separate concealed experiment keeps its owned host completely transparent and entirely outside the virtual desktop. It verifies actual player ownership, direct parentage, child style, geometry and nonactivation before queuing one `ShowWindowAsync(..., SW_SHOWNOACTIVATE)` after an actual UTF case-start callback. It requires subsequent child visibility and continues checking host opacity, offscreen containment, foreground ownership and every owned window. It adds no OS input or rendering call. [Microsoft's visibility documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-iswindowvisible) distinguishes the window/ancestor visibility state from physical screen exposure.

The [first attempt](../../artifacts/goal-20261014/post-start-child-visible-capability-20261016/execution.json) was **unqualified and aborted**, with no child show request. UTF had started, but the observer could not read the live event stream. One runtime-file and 127 event-stream reads returned sharing error 32; the initial 128-detail cap accidentally became a startup deadline. Re-entering the failed protocol guard also interrupted cleanup's grace loop, and the final receipt records forced termination of the owned player. No detected FMOD/native-fatal marker or requested crash file appeared. This attempt supplies no rendering capability or normal-shutdown acceptance.

The test-only callback writer now explicitly permits `FileShare.Read`. An [isolated pinned Mono console probe](../../artifacts/goal-20261014/pinned-mono-open-writer-sharing-20261016/summary.json) loaded the exact frozen player managed core library: the default three-argument writer blocked a concurrent reader with error 32, while explicit read sharing allowed both flushed callback lines to be read while the writer stayed open. Both modes read after close. The native Editor/player Mono DLL fingerprints differ, so console proof and actual player verification remain distinct.

The ignored launcher revision aggregates repeated initial publication failures and keeps the global timeout authoritative. It retains bounded detail samples plus complete counters and first/last timestamps. During already unqualified intervention cleanup it keeps the visual guard but skips failed protocol reads, visibility qualification and every show operation, allowing the original owned `WM_CLOSE` grace path to run. [Independent review](../../artifacts/goal-20261014/child-visibility-retry-cleanup-independent-20261016/summary.json) records unchanged original validators and successful source/stub regressions; those checks are not native rendering proof.

## Retained first ordinary-frame result

The [fresh native run](../../artifacts/goal-20261014/live-callback-child-visible-capability-20261016/results.xml), source `602ad776`, reports **2 passed / 1 failed / 0 skipped**, **100.0094962 s** XML and **101.844 s** runner. Its exact [execution receipt](../../artifacts/goal-20261014/live-callback-child-visible-capability-20261016/execution.json) records one guarded child-show request, actual visibility readback and 4,139 retained window samples covering all three cases. PID 33684 self-exited 1 after the failed test, without intervention. Integrity, unchanged player identity, host destruction and wake restoration passed; no detected FMOD/native-fatal/requested-crash artifact appeared.

Both HUD cases pass their unchanged ordinary-frame pixel and text-contribution assertions:

| Saved pose | World pixels with HUD disabled | Health text pixels | Controls left / right text pixels |
| --- | ---: | ---: | ---: |
| Puzzle west wall | 921,066 | 1,006 | 1,290 / 565 |
| Puzzle northwest corner | 921,215 | 1,109 | 1,253 / 538 |

Each HUD case records 24 context-begin and 12 owned-camera-begin callbacks. These images use the existing persistent target and ordinary frames; their verification adds no `Camera.Render` or render-pipeline request.

Exploration retains nine checkpoint captures, **68,766 observed LateUpdates**, zero sampled unsafe views and zero blocked-camera warnings. It records 68,766 unbound context/camera callback cycles outside explicit capture brackets, in addition to the nine explicit captures. Bounded callback detail samples were truncated with an explicit dropped-sample count; aggregate counters continue. Neither callback counts nor LateUpdate counts prove GPU completion, displayed pixels or FPS.

The exploration evidence correctly retains **`completed=false`**. Its existing final unexpected-log check occurs before the completion flag is set. Reaching nine checkpoints and preserving safe-camera observations does not turn the failed case into a pass.

## First-render lifecycle correction and actual repeat

The log contains exactly two Gaussian depth-of-field messages, one Bokeh depth-of-field message and one Panini-projection message. The saved camera disables post-processing, default depth of field is Off, Panini distance is zero and unused post-processing resources are stripped. The existing guarded diagnostic helper checks the actual camera and default/pipeline/active scene-volume profiles before expecting only those exact unavailable-shader messages.

Previously the helper ran inside the first explicit capture. An ordinary renderer can initialize the newly activated scene earlier. The native-only fixture correction installs a scoped `sceneLoaded` listener before the additive load, prepares the exact newly owned scene camera, retains and rethrows callback assertion failures, verifies scene ownership and always unsubscribes. Unity [documents the callback before `Start`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.SceneManager-sceneLoaded.html). Route inputs, poses, geometry, ordinary-render calls, yields, timeouts and pixel assertions remain unchanged.

[Pinned API/source verification](../../artifacts/goal-20261014/native-first-ordinary-render-lifecycle-20261016-v3/source-and-api-summary.json) passed Editor/native compilation 2/2 and confirms identical Editor-selected source for both changed test files. Independent review also caught a duplicate-expectation edge in the initial correction: a never-shown player can reach its first explicit capture before ordinary pipeline construction. The native-only helper overload now repeats every existing inactivity and initialized-pipeline check at captures while suppressing only a second enqueue of the four startup expectations. The scoped scene-load call registers them once; other callers retain their existing behavior. No global or pending mutable state is introduced.

Both actual repeats below use the same fresh player built from source `a5e4a26df61508bfcebed191a9829e038359fdfd`. The first-render shader-log failure no longer appears. The original strict-hidden behavior remains visible rather than being reclassified as successful:

| Actual profile | XML result and duration | Exploration | Ordinary HUD | Audio/native observations |
| --- | --- | --- | --- | --- |
| [Never shown](../../artifacts/goal-20261014/first-frame-never-shown-presentation-20261016/results.xml) | **1 passed / 2 failed**, 100.0382058 s | Completed, nine captures, 161,306 LateUpdates, zero sampled unsafe views/warnings | Both fail with black targets and zero outside-explicit callbacks | No detected FMOD/native-fatal marker or requested crash file |
| [Visually concealed child](../../artifacts/goal-20261014/first-frame-concealed-presentation-20261016/results.xml) | **2 passed / 1 failed**, 9.9124162 s | FMOD error 60, `completed=false`, two captures, 5,962 LateUpdates, zero sampled unsafe views/warnings | Both pass the same unchanged world/text assertions, with the pixel counts shown above | Default-change/reset errors followed by emulated no-sound output; no detected native-fatal marker or requested crash file |

The [never-shown execution receipt](../../artifacts/goal-20261014/first-frame-never-shown-presentation-20261016/execution.json) records PID 33284 self-exiting 1 without intervention. The [concealed receipt](../../artifacts/goal-20261014/first-frame-concealed-presentation-20261016/execution.json) records PID 45688 self-exiting 1 without intervention, from **12:32:55.317862 to 12:33:06.734836 UTC**. Failed audio qualification remains a hard failure despite passing HUD assertions. A clean log in the preceding strict run does not establish recovery from the later failure.

## FMOD recurrence and concurrent endpoint evidence

The [fresh basic-route Editor test](../../artifacts/goal-20261014/throne-columns-basic-route-20261016/results.xml) also failed **0/1** solely on FMOD error 60. Its trace reached Exit with 200 HP and eleven decoded checkpoint images, which do not override the unexpected-log failure. The [read-only host audit](../../artifacts/goal-20261014/fmod-recurrence-host-audit-20261016/summary.json) records HDMI endpoint arrival/removal and `Kernel-Power` event 566 with reason `RemoteConnection` within the failed test interval. Wake-request acquisition and restoration passed; a wake request did not prevent these topology changes.

For the paired native repeats, a concurrent [read-only endpoint sampler](../../artifacts/goal-20261014/audio-topology-native-contrast-20261016/completion.json) retained **1,015 samples over 270.019714 s**, from **12:30:40.3099598 to 12:35:10.3157441 UTC**, with a maximum adjacent sample-start gap of **0.35072 s**. Each sample queries the three render-default roles and render endpoint states without changing audio settings or activating a stream. Its closed complete history and exact test/log inputs are bound in the [native FMOD correlation receipt](../../artifacts/goal-20261014/fmod-recurrence-host-audit-20261016/native-fmod-correlation.json).

| Observed bracket, UTC | Three render defaults | HDMI state/event observation |
| --- | --- | --- |
| Initial samples | Realtek Digital Output | Both LG endpoints NOTPRESENT (4) |
| 12:32:38.1832297–12:32:38.4725152 | Realtek → LG ULTRAFINE | ULTRAFINE becomes ACTIVE (1); `RemoteConnection` event at 12:32:38.3203799 |
| By 12:32:39.2791271 | LG ULTRAFINE | LG IPS FULLHD also becomes ACTIVE (1) |
| **12:33:03.5131955–12:33:03.7928250** | **LG ULTRAFINE → Realtek** | **Both LG endpoints become NOTPRESENT (4)**; `RemoteConnection` at **12:33:03.5108055** |

The final switch/removal lies inside the same failed concealed exploration interval. This is actual default-role evidence, beyond the engine's message or a device-presence snapshot. FMOD lines are untimestamped, so the receipt does not prove the exact engine-internal cause or exclude another engine issue. Discrete samples do not establish instantaneous continuous coverage. Further HDMI arrival after the sampler ended invalidates treating its final stable interval as current launch evidence.

## Proposed playback-default selection, approval pending

A reviewed local plan proposes selecting the currently active Realtek Digital Output for Console, Multimedia and Communications before another monitored audio-enabled test. It changes defaults used by other Windows apps, so owner approval is pending and **no default change has been performed**. The [prepared plan and source](../../artifacts/goal-20261014/playback-defaults-plan-20261016/PLAN.md) capture exact target identity, all role-specific originals, active/present validation, readback and guarded restoration. Microsoft documents default retrieval; the prepared setting path uses the explicitly identified **undocumented `IPolicyConfig`** interface from frozen AudioDeviceCmdlets source. No module has been installed. After independent static review, the parent ran only the [read-only Preview](../../artifacts/goal-20261014/playback-defaults-preview-20261016/execution.json): it completed successfully with an empty setter-call array, without constructing PolicyConfig. Apply and Restore remain unexecuted.

At the Preview snapshot, **12:49:29.1038904 UTC**, all three roles already resolved to active Realtek Digital Output and both LG endpoints were NOTPRESENT. Every planned role therefore had `needs_change=false`. This is a single fresh observation; if unchanged, the prepared Apply would issue zero setter calls. It does not establish that Windows will retain that mapping when HDMI returns.

This is a one-time selection, not a durable pin against future Windows policy/device churn. Already-target readback would produce no setter calls and would not prove future preference. A new monitored test must still pass the unchanged FMOD failure gate; there is no reset loop, no-sound acceptance or suppressed error. No audio volume/format, endpoint enable/disable, registry, service, driver or power change is proposed.

The original strict-hidden failures, aborted attempt, earlier shader-log 2/3 and latest FMOD 2/3 remain separate retained evidence. Physical audibility, complete ordinary Release controls/traversal, human readability and pacing, hardware performance and historical native crash causality remain unverified. No complete 3/3 native result is claimed.
