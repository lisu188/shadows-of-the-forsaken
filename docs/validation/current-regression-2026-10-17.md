# Full current Unity regression — 17 October 2026

Both complete pinned-Unity catalogs pass at source **`91fccb14e56fab0835f76a90cb080e3d41e99c56`**: **188/188 EditMode and 240/240 PlayMode**, with zero failed, skipped or inconclusive cases. The runs closed on 16 October UTC, corresponding to 17 October in Europe/Warsaw. Graphics and audio stayed enabled; neither run required a stop or forced termination.

This verifies full-catalog integration after Main's composite callback registration, passive lifecycle observer and two added regressions. It supports DOCX §§3 and 6–8 and the AGENTS requirement for real scene/component verification. The mandatory library, throne miniboss, final fight, lower puzzle backtracking and optional bonus/return route remain unchanged. It does not supply human pacing or final acceptance.

## Actual engine results

| Complete catalog | Result | XML duration | Host runner duration | Unity PID / exit | Wake owner and release thread |
| --- | --- | ---: | ---: | --- | --- |
| EditMode | 188 passed / 0 failed / 0 skipped | 1.0881511 s | 51.219 s | 46712 / 0 | 51136 |
| PlayMode | 240 passed / 0 failed / 0 skipped | 729.274881 s | 746.141 s | 44640 / 0 | 37040 |

Both commands select the complete expected assembly without `-testFilter`. The current fixture-qualified result validator requires every catalogued case; the independent audits additionally verify the exact case set. Both owned processes terminate before import snapshots; wake restoration occurs on the same owner thread. Recorded free space stays above the **32 GiB** floor and measured task growth below **5 GiB**, within the unchanged **1800-second** runner deadline. Reviewed logs contain no detected FMOD, native-fatal or compile failure marker.

The [independent EditMode review](../../artifacts/goal-20261014/current-full-edit188-independent-review-20261016-v3/independent-review.json), SHA-256 `f79a977b7596cde8d8c03593a8451a98d05c24f67cec472db7a7e50bc4339062`, and [independent PlayMode review](../../artifacts/goal-20261014/current-full-play240-independent-review-20261016/independent-review.json), SHA-256 `4771f357b42eb9ca0f6aeb31a847156f5171948dde4a1e5afdef41b4fc101bda`, qualify the closed results, source/import archives, preimages, retained bootstrap pair, process/resource records and unchanged audited inputs.

| Frozen record | EditMode SHA-256 | PlayMode SHA-256 |
| --- | --- | --- |
| Results XML | [`3fa5fd3b28ae1a79c72fab439e8213c3df3d9261d7570e1ab445932961966a0d`](../../artifacts/goal-20261014/currentfull-edit188-20261016/results.xml) | [`a53488e03fb785878f919533dd46a19af26d2624c2c9f9a94fedc6f4bb693cf4`](../../artifacts/goal-20261014/currentfull-play240-20261016/results.xml) |
| Execution | [`279a234dcd60714b776c78c425d7b8393b06a263a10f80ed5a10869de4bbeb47`](../../artifacts/goal-20261014/currentfull-edit188-20261016/execution.json) | [`a31abd175636111cdd67b902fbd20a5d7d97524ca2cc4c257789d905db7f5d52`](../../artifacts/goal-20261014/currentfull-play240-20261016/execution.json) |
| Imported archive | [`e38bd9202f494c9c4a3b227e54249df08f9c107d94ddbe1184bf75c084c02902`](../../artifacts/goal-20261014/currentfull-edit188-20261016/imported.zip) | [`f3cefe5fbb6a97b0010bfa1cacb9581371e915ade0d0050a8c43a588f17e6557`](../../artifacts/goal-20261014/currentfull-play240-20261016/imported.zip) |

## Source and import scope

Each run uses **778 committed LF Unity inputs**, including **523 unchanged critical inputs**. The complete [source archive](../../artifacts/goal-20261014/audio-run-lifecycle-native-build-repeat3-20261016/source.zip), SHA-256 `019899b2f841f3e9b2d705af1626e2c17cd2ca7d05f24c5b427199437f367dd9`, was created for `971ef31`; all those Unity inputs remain byte-identical at `91fccb`. Both final imported maps contain **779 files**, with no generated UTF bootstrap remaining. The preceding pair is preserved outside the verification cache in the EditMode output.

EditMode preserves **18** overwritten preimages and reviews **22** importer differences; PlayMode preserves **22** preimages and reviews **50** differences. Every complete archive payload has verified CRC/SHA-256 and safe unique paths. The reviews retain exact prior qualified payload comparisons and bounded editor-material metadata/legacy-color normalization; no importer difference is left unreviewed and no tracked material source is rewritten.

The [fresh source audit](../../artifacts/goal-20261014/completion-audit-91fccb-20261016/audit.json) verifies all 778 archive payloads against HEAD Git blobs and all **652 production Unity inputs outside `Assets/Tests/`** against delivered game source `23b3ac0`. That count includes the unchanged `Assets/Tests.meta` folder metadata. The later CI/documentation source `b38b963f734ecc3cc0d399ffdcbd7b08ae55b519` has [all 778 Unity Git inputs identical to tested `91fccb`](../../artifacts/goal-20261014/current-full-suite-root-closure-20261016/b38b963-source-identity.json), SHA-256 `626ea7df38645fe3cd3c27b2b10564b788338feecf64341c2903d207e6c7f7c2`. The ordinary Release package, receipt, one-hour lifecycle and recorder pins retain their [separate delivery evidence](minimum-travel-native-release-2026-10-16.md); this Editor regression creates no new shipping package.

## Closed endpoint observations

The [independent closed CoreAudio union](../../artifacts/goal-20261014/current-full-suite-audio-union-preparation-20261016/independent-closed-audio-union-review.json), SHA-256 `63193ab38a8837b40d7048b531951316181c8831cc2e327e7016c6283f8fc420`, checks **4,512 snapshots** from two normally completed collectors (**2,251 + 2,261**). Their actual sampling envelopes overlap by **9.0492534 s**, with no envelope gap. All **432 authored context intervals lie within the combined sampling envelope**: two runner intervals, two NUnit run intervals and all 428 case intervals. However, **405 of those contexts have zero directly overlapping CoreAudio queries**, including short/coarsely timestamped cases. Their retained per-context sample/signature counts are zero; envelope coverage does not establish a direct audio observation during every case.

All three sampled render-default roles remain ACTIVE on the same Realtek endpoint; all observed HRESULTs are zero, with one topology signature, no sampled transitions and no query disagreement. The [exact gap/bracket annex](../../artifacts/goal-20261014/current-full-suite-audio-union-preparation-20261016/actual-gap-brackets-annex.json), SHA-256 `9bdc86e46420df51b959b90564f01da0035451e08ceab8a62b267c69e261e4e5`, retains the actual sample/PID/time boundaries. Maximum combined sample-start gap is **0.4275845 s**; the PlayMode-wrapper gap is **0.312637 s**. Authored wrapper UTC spans (**51.212905 / 746.118297 s**) remain distinct from host monotonic runner durations (**51.219 / 746.141 s**).

These discrete host observations support this run's endpoint context. They do not establish the Unity-selected stream, continuous audio health, physical audibility, recovery after a device transition, future stability or the historical crash cause.

## Route captures and separate acceptance

The current full PlayMode run retains the existing explicit route captures. These two were inspected from the closed output; they are automated Editor evidence, not human playtests or ordinary Release screenshots:

![Current main-route completion at 200 HP](../../artifacts/goal-20261014/currentfull-play240-20261016/captures/CastleMainRouteCompletesThroughControlsWithoutSecret-route-complete-main.png)

![Current optional-route completion with the relic at 200 HP](../../artifacts/goal-20261014/currentfull-play240-20261016/captures/CastleSecretRouteReturnsThroughThroneAndStillRequiresFinalFight-route-complete-secret.png)

The separate [native lifecycle diagnostic](audio-run-lifecycle-2026-10-16.md) remains independently qualified at **24/24**, including thirteen actual managed markers and the pre-quit configuration/hash chain. Its earlier compiler, native **23/24** and focused Editor **1/2** failures remain preserved in that report. These full suites do not retroactively pass those attempts or qualify native stream recovery.

A separate [new rendered-metrics workflow](../../.github/workflows/rendered-routes-tests.yml) has [local source/TRX validation](../../artifacts/goal-20261014/rendered-routes-workflow-preflight-20261016/summary.json): **30/30 pure .NET cases**, with eighteen mutated TRX reports rejected. Remote execution was unverified at that local checkpoint. The subsequent [push-triggered GitHub job at `9793073`](https://github.com/lisu188/shadows-of-the-forsaken/actions/runs/38976242500/job/117226262671) passes **30/30, zero skipped**, the exact result gate and the two-file artifact upload. The [independent downloaded-artifact review](../../artifacts/goal-20261014/metrics-push-artifact-independent-review-20261017/independent-review.json), SHA-256 `bed4092bb5cddee875ecd761b6be9de64b821068b262a6a2bbe7b9f33e2d25aa`, verifies the exact 5,658-byte ZIP digest, both safe CRC-checked payloads, the unchanged remote workflow and successful replay of its gate against the downloaded TRX. This is pure C# CI evidence; neither result activates Unity CI or supplies GitHub engine reports.

First-time human **three basic plus one secret** timing, the DOCX approximately 2–3-minute basic-exploration target, clue/combat/wayfinding and final art acceptance, ordinary Release manual smoke, activated Unity CI and the unresolved FMOD/Mono causes retain their separate requirements. The qualified [rendered-route performance observations](rendered-routes-2026-10-16.md) retain their CPU-boundary/configuration/exclusion limits. This closes the current full-catalog integration gap; it is progress toward the complete goal.
