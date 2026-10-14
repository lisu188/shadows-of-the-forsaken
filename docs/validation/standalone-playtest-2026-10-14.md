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

## Separate automation setup and current blocker

- Kit `attempt-3093873a892e41b7bb75450b52086be4`: player launched, no gameplay input sent, then normal exit 0 at `06:28:18.7590277Z`, with observed interruptions and unchanged player identity. Its [final launch receipt](../../artifacts/primary-player-playtest/attempt-3093873a892e41b7bb75450b52086be4/attempt/launch.json) has SHA-256 `2deccf6471aced5013db8580a2e3f0eaf89e16143038e86a1e8df72c1161ec34`.
- Kit `attempt-00b62b64bf054dbbb0849e42733ccd74`: a sandbox PTY extraction failed in `Expand-Archive` / `Write-Progress` with console access denied, before game launch. The partial kit is preserved; neither a launch-started nor final launch receipt exists. This is a setup failure, not a gameplay result.
- Kit `attempt-d2721934e31742688d88c52ef4a2af1c`: a fresh no-TTY recorder with progress display suppressed launched the real player at `06:30:13.9301015Z`, PID 9132, labelled `automation / known_route / basic`. Its [launch-started receipt](../../artifacts/primary-player-playtest/attempt-d2721934e31742688d88c52ef4a2af1c/attempt/launch-started.json) has SHA-256 `29c0c815a17a8971a92f55cdba07da268012ee6df2f48eae0acb8adfa6347278`.

The desktop capture shows the Windows lock screen. A read-only process query confirmed LockApp PID 12696, LogonUI PID 12240 and the player PID 9132 in Session 1. Computer-use input is stopped and the owner has been asked to unlock. **The player launch is real; autoplay gameplay has not executed.** No gameplay input has been sent, and this launch is not a traversal or acceptance pass.

The [playtest handoff](../primary-player-playtest.md) and [acceptance matrix](../docx-acceptance.md) retain the remaining human, three-basic/one-secret timing, death/restart and hardware requirements. Original evidence, partial kits and native inputs remain intact.
