# Native Windows audio mixer diagnostic — 2026-10-14

A separate instrumented Windows player passed **1/1** exact audio mixer case and all **15 harness qualification checks**, then exited itself with code 0. Retained PCM establishes nonzero processing through the owned source, audio callback and Unity master mixer, followed by freshly processed silence after stopping. This extends the earlier Editor mixer result to the observed native Windows run. **Physical audibility, production soundtrack playback and an engine repair of the historical Mono shutdown fault remain unproven.**

The selected case is `ShadowsOfTheForsaken.Tests.PlayMode.AudioMixerPlaybackTests.QuietClipTraversesMixerAndReturnsToSilence`. It uses the existing generated quiet clip. This separate profile preserves the original ten-case route contract and does not change production gameplay, scenes or audio. The [ordinary Release package](release-hud-player-2026-10-14.md), [native route result](standalone-hud-routes-2026-10-14.md) and [Editor HUD/audio result](primary-hud-audio-2026-10-14.md) retain their distinct evidence scopes.

This is engine and diagnostic verification supporting DOCX §§3/7 under the owner's request to investigate FMOD/native crashes. The generated signal and its measurement thresholds are test implementation choices; the DOCX does not specify a soundtrack or this diagnostic mechanism.

## Source, preflight and build

The actual test build names clean LF commit **`ff01359117c033967321425328fdbb14462bd4fc`**, pinned Unity **6000.6.3f1**, and **742 committed native inputs**. The [source review](../../artifacts/goal-20261014/native-audio-source-final-review.json), SHA-256 `0056edd0c7a8948c242f7fc98d8b6d411ef6f7ce4f16480ea416e5585900b890`, binds those inputs. The original 732 inputs remain identical to the previously completed game verification; the ten additions are separate test-support source, assembly definitions and metadata. Ordinary Release source and the original route support remain unchanged.

The [saved preflight review](../../artifacts/goal-20261014/native-audio-preflight-review.json), SHA-256 `d969f3d20af0c6dbd494d7fdc47304a5695f9f97e159b1040156e4fd32120b62`, records one confirmed tooling correction: finite but excessively large JSON PCM values could overflow a derived RMS to infinity. The validator now requires a finite positive recomputed RMS, and the malformed-input regression passes. This was a synthetic validator robustness defect, rather than an observed native audio fault; the tested `1e308` values exceed C# `float` range.

[StandaloneAudioTestBuild](../../Assets/Tests/EditMode/StandaloneAudioSupport/StandaloneAudioTestBuild.cs) requires the exact anchored one-case filter and an explicit fresh `-buildPlayerPath` base. UTF's actual output is `player/PlayerWithTests/PlayerWithTests.exe`. It preserves the generated UTF bootstrap first and includes `Assets/Scenes/ForsakenCastle.unity` second. The hook clears AutoRunPlayer and ConnectToHost; the player is launched separately. [StandaloneAudioPlayerEvidence](../../Assets/Tests/PlayMode/StandaloneAudioSupport/StandaloneAudioPlayerEvidence.cs) and [run_standalone_audio.py](../../tools/run_standalone_audio.py) require exact agreement between the one-case profile, NUnit XML, callback events, runtime metadata, completion counters and artifact hashes.

The first compile attempts placed the new attributes beside the existing route attributes and failed with duplicate-attribute errors. Their logs remain retained. The final hooks use separate, non-auto-referenced test-support assemblies constrained by `UNITY_INCLUDE_TESTS`; the original assemblies and route hooks were preserved. The [final pinned-API compile receipt](../../artifacts/goal-20261014/standalone-audio-hook-compile-v3/summary.json) passed five scoped configurations with zero diagnostics. The [UTF source review](../../artifacts/goal-20261014/standalone-audio-support-source-review.json) traces bootstrap assembly loading and callback discovery; actual inclusion and execution were then verified in the built player.

| Build observation | Actual result |
| --- | --- |
| Target / configuration | `StandaloneWindows64`; Development and IncludeTestAssemblies true; AutoRunPlayer and ConnectToHost false |
| Final BuildReport | `Succeeded`, **0 errors / 0 warnings** |
| BuildReport bytes / duration | **200,821,643 bytes / 17.8642239 s** |
| Builder duration / exit | **39.813 s / 0**; no runner stop; temporary wake request restored |
| Engine files / complete test-player tree | **345 engine files**; **346 entries / 200,822,617 bytes** including the 974-byte test-build receipt |
| Pre-build / completed imported inputs | **743 / 745 files**; completed inputs comprise 742 committed files, generated SceneTemplate settings and the new UTF scene/meta pair |

The [bootstrap retention receipt](../../artifacts/goal-20261014/standalone-audio-build/bootstrap-retention-result.json) records exactly **two hash-verified moves**, preserving the previous bootstrap scene/meta pair, with zero pending entries. The new bootstrap is `Assets/InitTestScene21b55c9f-75cf-4d5f-a925-d0527925b7ce.unity`. The retained isolated cache was reused; this is not a cold build.

The [final test-build receipt](../../artifacts/goal-20261014/standalone-audio-build/player/PlayerWithTests/test-build.json) has SHA-256 `0d5165e411921af34611b55f6ace97e32ea7df67b9460f6089f8541c0273b5f2`. The [745-input imported ZIP](../../artifacts/goal-20261014/standalone-audio-build/imported.zip) has SHA-256 `8e2fe05d57c056b76437829fc1710367ed255cf48e3c7f5d4d825787b65ca95c`, with CRC verification retained. The [build execution receipt](../../artifacts/goal-20261014/standalone-audio-build/execution.json) records terminal ownership before the completion snapshot.

## Native result and retained samples

The [NUnit XML](../../artifacts/goal-20261014/standalone-audio-native/results.xml) records **1 passed, 0 failed, 0 skipped and 0 inconclusive**, duration **0.3212733 s**. The [execution receipt](../../artifacts/goal-20261014/standalone-audio-native/execution.json), SHA-256 `e68d8fc2919aaa1de8cbe7a1c8be84b3b73ce5c2889a773ed5148470f4b3788b`, records **4.375 s** total runner elapsed, player PID **43472**, native WindowsPlayer metadata and unchanged complete player identity.

Launcher and player both had **medium integrity**. The normal renderer ran with `batch_mode=false`, observed Direct3D11 and **`kGfxThreadingModeClientWorkerJobs`** after requesting legacy graphics jobs. The actual child was embedded in the invisible owned host; the [independent live observation](../../artifacts/goal-20261014/standalone-audio-native-live-independent.json), SHA-256 `f62952df04e3f30f84a5e28f063a399c178c994c0c59846d389278747f403a61`, verifies token/window state separately from launch intent. No foreground/visible-window violation, input, activation or progression injection was used.

The player self-exited **0**, without WM_CLOSE, forced termination or cleanup intervention. The owned host was destroyed, temporary wake request restored and all 15 qualification gates passed. The three native logs contain no detected FMOD/audio-failure marker, native-fatal marker or diagnostic candidate; no callback-failure or crash artifact was produced. This statement concerns the retained native interval, not the separate Editor build log.

| Mixer measurement | Actual retained value |
| --- | --- |
| Output / DSP buffers | **48 kHz Stereo / 1024 frames × 4 buffers** |
| Generated clip | 24,000 mono frames, **0.5 s**, authored peak 0.003 |
| Source / master PCM windows | **1024 samples each**, all finite; recomputed peak **0.00212132022716105**, RMS **0.0012506990052274**, **1023 literal nonzero samples** in each |
| Audio-thread tap | **7 callbacks**, **4,054 nonzero samples**, **0 invalid samples** |
| Signal observed after Play | **0.021333 DSP s** |
| Fresh master silence after Stop | **0.106667 DSP s**; stopped master peak 0 |
| Cleanup | Owned objects destroyed; source/listener/global restoration flags true; no cleanup errors |
| Borrowed source/listener objects | **0 / 0**, original listener gain 1 and pause false |

The [actual mixer payload](../../artifacts/goal-20261014/standalone-audio-native/audio/audio-mixer-7d1be9a9a6854d8f965661d627845163/evidence.json), SHA-256 `3896118ab4bab8002b5e0bd2c1d061d52cb23ae957d96134b7064b489ca538c3`, retains both complete PCM windows, DSP clocks, bounded sample history and callback/cleanup counters. The runner recomputes PCM peak/RMS rather than accepting success booleans alone, and confines the payload timestamp to this actual run.

The clip's generated length does not mean the full 0.5 seconds played: the test stops after observing the qualifying signal. Zero borrowed objects and default original global settings also mean this run does not demonstrate restoration of borrowed objects or nondefault audio state. The test establishes the actual mixer path and fresh stopped silence, without observing a physical speaker or listener judgment.

## Retained diagnostics and scoped tooling checks

Zero BuildReport warnings does not erase the separate build log. After `Cleanup mono` at line 13137, it retains four `abort_threads` notices, debugger port 4000 unavailable, and three `JobTempAlloc` remaining-allocation diagnostics at lines **13152, 13154 and 13157**. The licensing-token diagnostic at line 98 is also retained. These are Editor-build shutdown observations, absent from the native player's log; their cause is unproven. They do not establish a production gameplay leak or explain the historical Mono crash.

The [final Python source-check receipt](../../artifacts/goal-20261014/native-audio-source-checks-final/python.json) records **235 total: 222 passed / 13 Windows-only skipped**, **8.813 s** test / **9.469 s** runner, exit 0. Its retained log contains all **29 audio tooling cases passed**, including the RMS-overflow regression. The log SHA-256 is `80df2c2d0d836249b8e0e501c10b81401fafc044a85271e322a894d80f83d85c`. The [earlier source-check bundle](../../artifacts/goal-20261014/native-audio-source-checks/) separately retains DOCX/contract pass, source integrity and 29/29 shared progression-core tests; skipped cases are not passes.

The [completed independent native verification](../../artifacts/goal-20261014/standalone-audio-independent-verification.json), SHA-256 `35b73fff12fdc3f0508f8b900d9641405493b37ee46f560d72001915a5011e06`, checks all 742 committed inputs, 745 imported payloads, the exact singleton selection and support DLL, all 346 built-player entries, XML/events/completion, raw PCM calculations, live hidden-window observation, logs, normal exit and cleanup. It confirms the native result within the scope above.

## Fresh focused Editor regression

After the new support assemblies were added, a hidden graphics/audio-enabled Editor run passed **3/3 selected cases**: the two ordinary-frame primary HUD poses and the existing mixer case. The [execution receipt](../../artifacts/goal-20261014/audio-editor-regression/execution.json), SHA-256 `a9117019b4b17a60354ffbad812c985f2443de37ab31fa46011b412613f12e1e`, binds the same 742 committed inputs, **1.7536232 s** XML / **17.359 s** runner, exit 0, no runner stop and restoration of the temporary wake request. Its [imported archive](../../artifacts/goal-20261014/audio-editor-regression/imported.zip) has SHA-256 `d61bc477ef18cdec96a776d10b09f916e2a4a003a7ad65c0778aac259233869f`. The preceding native-audio UTF bootstrap pair was preserved before copying sources. This run executed three cases, regardless of the larger discovered assembly catalog.

Six fresh **1280×720** PNGs retain HUD-visible, canvas-disabled and text-disabled phases at both poses. These fixtures retain initial progression; the displayed `COURTYARD` label is not route traversal evidence.

The [independent focused Editor review](../../artifacts/goal-20261014/audio-editor-regression-independent-review.json), SHA-256 `3f398372b6f37917d11f66af7924c6bd0b81feb2c812dee090aae33ef7b77590`, verifies the three exact cases, all 743 imported payloads, source/import pairs and bootstrap retention, all six PNGs and recomputed HUD/text/world pixel counts, separate Editor PCM windows, normal exit and wake restoration. Standalone-callback-only outputs were absent. The Editor log retains four `abort_threads` notices, debugger port 3988 unavailable and licensing refresh diagnostics; no native-fatal or audio-failure marker was found. These Editor shutdown observations remain separate from the native run above.

| Fresh Editor pose | Actual HUD-visible capture |
| --- | --- |
| Puzzle west wall | [1280×720 PNG](../../artifacts/goal-20261014/audio-editor-regression/captures/primary-hud-d0a58c4c8efc494eacdd1e5cbd6d3b00/05-puzzle-west-wall-hud-visible.png) |
| Puzzle northwest corner | [1280×720 PNG](../../artifacts/goal-20261014/audio-editor-regression/captures/primary-hud-93b783cacc99470a9e9a0683c7984618/08-puzzle-northwest-corner-hud-visible.png) |

The earlier full **233/233 PlayMode**, **187/187 EditMode** and **10/10 native route** runs were not repeated here. Their original 732 native inputs remain byte-identical; the separate new native case and focused Editor cases supply the added support's affected-path verification.

This one-case Development/UTF result is separate from the ordinary Release package, full game routes, the still-unexecuted native ordinary-frame HUD/unsnapped-camera cases, human controls/readability, uninterrupted DOCX two-to-three-minute pacing, hardware performance and physical audio acceptance. No production audio change or engine root-cause repair is claimed.
