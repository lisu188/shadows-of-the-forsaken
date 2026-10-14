# Standalone playtest status — 2026-10-14

The owner lifted the foreground-testing deferral and declared their first time playing. The first attempt used `human / first_attempt / basic` and verified source `5af8861c39bd5fbf4d324690e5a74b1d12aae333`. It is retained **incomplete**. A later owner request for computer-use autoplay is separate automation evidence. No completion, pacing, audio, performance or human acceptance is claimed.

## Retained human attempt

Kit: `artifacts/primary-player-playtest/attempt-6e914f6fd871408794a570e817ea79c7`. The player exited normally with code 0 at `2026-10-14T06:23:06.5409484Z`. Final state was Running / Puzzle, `FirstEnemyDefeated` and `MainPuzzleSolved`, 176/200 HP and 210.978363 s elapsed. This is session time, not a route-completion time. Before/after verification matched 199 engine files / 133,842,184 bytes and trusted BUILD-INFO SHA-256 `aa3fbf3c43943579fafe956a8fba43c5348232d3d48ef22f81b8eaa0e6a93578`.

The owner supplied first-attempt familiarity but no interruption declaration. The assistant explicitly recorded `unknown`; its provenance is retained. The timing reader returned **1**, with reason **“Explicit uninterrupted-run declaration is required”**, zero completed runs and `acceptance_passed: false`. The route is independently incomplete in the raw status/events. No declarations or raw records were rewritten to qualify it.

| Retained file | SHA-256 |
| --- | --- |
| [Launch/exit receipt](../../artifacts/primary-player-playtest/attempt-6e914f6fd871408794a570e817ea79c7/attempt/launch.json) | `62d940c152e416526c79e6ec8a837b3043b82b995c1a23dae176c5b85a1c9ce0` |
| [Final status](../../artifacts/primary-player-playtest/attempt-6e914f6fd871408794a570e817ea79c7/attempt/status.json) | `6341c3be6f28f4d515228208a7d6b1e2889951b63f4fb7ca5e7b161f14ecc002` |
| [Timing input](../../artifacts/primary-player-playtest/attempt-6e914f6fd871408794a570e817ea79c7/run.json) | `968609d5b27383e050f79d77613c97ba789c0a886e5eb145b3fd9f084cf63110` |
| [Timing refusal](../../artifacts/primary-player-playtest/attempt-6e914f6fd871408794a570e817ea79c7/timing-review.json) | `0ab531dbe25138643126049871b3a6ebefd07850fae2dd8f5d4c62e0789778b2` |
| [Interruption provenance](../../artifacts/primary-player-playtest/attempt-6e914f6fd871408794a570e817ea79c7/interruption-provenance.json) | `355760f8f75a1080c74621d8ea58c597a8b5f175b8c8733d154710e643027f8a` |
| [Raw-file hash index](../../artifacts/primary-player-playtest/attempt-6e914f6fd871408794a570e817ea79c7/hash-index.json) | `cca0d49d07d4072efadee395ab707d1710265ed5fd6f6ff580e0fccf13786f5b` |
| [Post-play identity verification](../../artifacts/primary-player-playtest/attempt-6e914f6fd871408794a570e817ea79c7/attempt/verify-after.txt) | `6e9fd95c2da6b58040461dbe652e4a3f1a3bdc17517c73d145c0e331238e92ac` |
| [Player log](../../artifacts/primary-player-playtest/attempt-6e914f6fd871408794a570e817ea79c7/attempt/player.log) | `a88489817a9dd96baf9d5f02ad6d1285ca981046db20a7beb0368e8ee7539798` |

All timing-manifest references and existing hash-index entries were checked and match. Events, session performance, room performance, final status and player log remain preserved. The timing refusal is a new supplementary file, not a replacement raw record.

The log contains seven `CameraFollow` safety events reporting rendering suspended until recovery. Their location and duration are unavailable; a lasting visible defect is not established. The DOF/Panini diagnostics concern unused post-process passes: the saved camera disables post-processing, the default DOF mode is Off and Panini distance is zero, and URP constructs/logs these passes before checking activation. No active shader-effect failure or audio error was demonstrated by this log; audible output and visual acceptance remain unverified.

## Separate automation setup, terminal diagnostics and hidden editor follow-up

- Kit `attempt-3093873a892e41b7bb75450b52086be4`: player launched, no gameplay input sent, then normal exit 0 at `06:28:18.7590277Z`, with observed interruptions and unchanged player identity. Its [final launch receipt](../../artifacts/primary-player-playtest/attempt-3093873a892e41b7bb75450b52086be4/attempt/launch.json) has SHA-256 `2deccf6471aced5013db8580a2e3f0eaf89e16143038e86a1e8df72c1161ec34`.
- Kit `attempt-00b62b64bf054dbbb0849e42733ccd74`: a sandbox PTY extraction failed in `Expand-Archive` / `Write-Progress` with console access denied, before game launch. The partial kit is preserved; neither a launch-started nor final launch receipt exists. This is a setup failure, not a gameplay result.
- Kit `attempt-d2721934e31742688d88c52ef4a2af1c`: a fresh no-TTY recorder with progress display suppressed launched the real player at `06:30:13.9301015Z`, PID 9132, with declared intent `automation / known_route / basic`. The [launch-started receipt](../../artifacts/primary-player-playtest/attempt-d2721934e31742688d88c52ef4a2af1c/attempt/launch-started.json) has SHA-256 `29c0c815a17a8971a92f55cdba07da268012ee6df2f48eae0acb8adfa6347278`. The player later exited with **reported code 0** at `07:25:09.0578989Z`, with unchanged identity; this is not a clean-shutdown qualification.

During the earlier wait, the desktop capture showed the Windows lock screen and a read-only query confirmed LockApp PID 12696, LogonUI PID 12240 and player PID 9132 in Session 1. That lock is now lifted: the owner confirmed Windows unlocked, a fresh query found no LogonUI or player process, and the game-window inventory was empty. Residual LockApp alone does not establish a current lock. The recorder wrapper was waiting for a declaration on closed stdin after the player had exited. Its exact PID, parent, creation time and recorder command were verified before stopping only that helper; the wrapper then returned 1. This cleanup did not stop the already-exited player or change its reported exit code, log or raw evidence.

No agent gameplay input was sent during d272. Nevertheless, raw events record late entry into FirstEncounter and `FirstEnemyDefeated`; final state is Running / FirstEncounter, 176 HP and 3286.333740 s elapsed. The actual actor/input source is **unqualified**, with observed interruptions. Declared automation intent does not authenticate that movement, and this counter is not a route-completion time.

The d272 log contains an actual **Native Crash Reporting** section and unknown-native-code/fatal-runtime diagnostic after physics/input shutdown, despite reported exit 0 and a normal-exit observer event. The managed stack is empty and the cause is unproven. It also records a default audio-device change, two FMOD error-60 failures and fallback to **software output with no sound**. Startup low-integrity and AMD/NVIDIA plugin-load diagnostics do not establish the crash cause. Two camera safety events remain unlocated/untimed; the DOF/Panini diagnostics still concern unused passes. These are distinct from the earlier human log's seven camera events and absence of audio errors.

| d272 terminal evidence | SHA-256 |
| --- | --- |
| [Reported exit and identity](../../artifacts/primary-player-playtest/attempt-d2721934e31742688d88c52ef4a2af1c/attempt/launch.json) | `23ff03d5ba2e30306ec1ba02ad200df728289267f2e8b3e90cb9d3c0868cfebb` |
| [Full terminal log](../../artifacts/primary-player-playtest/attempt-d2721934e31742688d88c52ef4a2af1c/attempt/player.log) | `625ed38df371f9ff732bf48d4a1ae65b9c0030cb71674eec87cf0dbeed16b5a5` |
| [Supplementary closure review](../../artifacts/primary-player-playtest/attempt-d2721934e31742688d88c52ef4a2af1c/closure-review.json) | `8c2dbce578283acddd0afb2ec03aac4fc70184677b428328998eaef1dfc616b8` |
| [Final hash index](../../artifacts/primary-player-playtest/attempt-d2721934e31742688d88c52ef4a2af1c/hash-index-final.json) | `6bfeb53c13d00c5286aa5d67f5c5e309531f45c0ad9acae0a99ea078714858a9` |
| [Recorder-only cleanup](../../artifacts/primary-player-playtest/attempt-d2721934e31742688d88c52ef4a2af1c/recorder-cleanup.json) | `1332c2f906894d7dd5bf623c7d8c6583c0579041b47ab969829f0fd43c4cd379` |

The new closure review and index preserve the original setup review and raw receipts, explicitly record observed interruptions and unqualified actual input, and make no acceptance claim. All 14 final-index references were checked; no qualifying `run.json` was generated.

The owner next requested **“use hidden window”** and then selected **“Hidden Unity route test”**. The [separate hidden editor run](hidden-routes-2026-10-14.md) completed with **10/10 passes** (six gameplay cases and four helpers). Its known-route basic/secret traces finished at 81.756554/121.097183 s with 200 HP. It uses Unity test-fixture events, not OS input or ComputerUse; it neither repairs this attempt's input provenance nor supplies standalone/human timing acceptance. The owner also reported no noticed view disappearance/flicker or missing audio during the earlier incomplete human attempt; that limited feedback does not override d272's separate diagnostics or qualify full-route audio/visual acceptance.

The [playtest handoff](../primary-player-playtest.md) and [acceptance matrix](../docx-acceptance.md) retain the remaining human, three-basic/one-secret timing, death/restart and hardware requirements. Original evidence, partial kits and native inputs remain intact.
