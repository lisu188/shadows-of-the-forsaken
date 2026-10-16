# Minimum-travel camera: native and Release qualification — 2026-10-16

The corrected camera at game source **`23b3ac05322983ecdbbeb72551d5260f9c20796d`** now has independently qualified native route **10/10** and concealed presentation **3/3** results, plus a fresh ordinary Release build. These advance DOCX §§3/7 exploration and visibility verification after the [minimum-travel correction](camera-minimum-travel-2026-10-16.md). All **758 Unity Git inputs** remain identical at helper commit `015319c737ebdd1c76ccc5270806f295151dc3a8`. The ordinary Release's one-hour run is **currently running**, with no duration/shutdown qualification or new delivery promotion yet.

## Ordinary Release build and identity

The [independent build review](../../artifacts/goal-20261014/minimum-travel-release-build-independent-review-20261016.json) approves all **758 source / 759 imported payloads / 503 critical inputs**, fifty preserved import preimages and nineteen actual import pairs. Material serialization changes and the remaining exact known import pairs are reviewed; there are no unreviewed differences. The primary scene, corrected camera, existing column mesh and asset identities match source. This is `BuildOptions.None`, primary scene only, without test-player assemblies or a generated test scene.

Unity PID **28048** exits **0** without intervention; wake restoration occurs on owner thread **2212**. The build reports **zero errors / zero warnings**, **23.3580416 s** build duration and **41.469 s** runner duration. The [completion inventory](../../artifacts/goal-20261014/minimum-travel-release-build-20261016/identity/completion.json) and verified [BUILD-INFO](../../artifacts/goal-20261014/minimum-travel-release-build-20261016/player/BUILD-INFO.json) bind **199 engine files / 133,840,024 bytes**. Source ZIP SHA-256 is `3e7f4e13ee4aff4bafcdf53e21c517d6b93a87d50e91e24e2c43f0176c4004a1`; imported ZIP is `4c0a8b94673360c8691e4175e835422a1b9d5b65a2df1f2a8d13137699380186`.

## Actual native results

| Profile | Actual result and retained scope |
| --- | --- |
| [Native route build](../../artifacts/goal-20261014/minimum-travel-native10-build-independent-review-20261016.json) | Same 758 LF source inputs, 503 unchanged critical inputs, 761 imported payloads, eighteen preserved backups and 348 player files; exact ten-case filter. Unity PID 21388 exits 0; 42.062 s runner; build has zero errors/warnings. |
| [Native route runtime](../../artifacts/goal-20261014/minimum-travel-native10-runtime-independent-review-20261016.json) | **10/10 / zero skipped / all fourteen gates**, 431.0790379 s XML / 435.032 s runner. PID **55144** self-exits 0 without intervention. All 348 player files remain unchanged; 39 PNGs, nine framing artifacts and two input/progression traces are verified. Basic/secret fixture clocks are **81.553177 / 121.122047 s**, both ending at 200 HP. These are automated input results, not human pacing measurements. |
| [Concealed presentation build](../../artifacts/goal-20261014/minimum-travel-concealed-native-build-independent-review-20261016.json) | Same 758 source inputs, 503 critical inputs, 761 imported payloads, eighteen backups and 348 player files. Independent build review occurred concurrently with the already launched runtime; it is not represented as a delivered prelaunch gate. |
| [Concealed presentation runtime](../../artifacts/goal-20261014/minimum-travel-concealed-native-independent-review-20261016.json) | **3/3 / zero skipped / all twenty-one gates**, 100.0531037 s XML / 101.907 s runner. PID **6188** self-exits 0. All 4,094 window-history rows are reviewed: seventy pre-readiness samples keep both host and children hidden; exact owned readiness precedes one child-show operation. Both ordinary-frame HUD pixel tests pass. Exploration completes **68,799 observed frames / zero unsafe views / zero blocked warnings**, nine captures; all fifteen PNGs and unchanged player identity are verified. |

The concealed profile adds no render request; its existing nine explicit captures remain separate from ordinary HUD checks. It qualifies its transparent, offscreen owned-window protocol. The historical **never-shown presentation 1/3 failure** remains a distinct failed profile in the [concealed-rendering report](concealed-native-rendering-2026-10-16.md). The original **234/235** camera failure is likewise retained, alongside its subsequent unchanged-fixture GREEN and full **238/238** Editor repeat. Neither native profile is an ordinary Release playthrough.

## Audio evidence and limits

The [closed audio union](../../artifacts/goal-20261014/minimum-travel-native10-audio-union-independent-review-20261016.json) verifies **4,493 samples** from two closed 600-second histories with **99.1272817 s actual overlap**, covering both complete native wrapper/UTF intervals and every case interval. All sampled HRESULTs are zero and all three default roles remain active. Maximum combined sample-start gap is **1.8399571 s**; this is discrete sampling, not continuous observation.

During Native10, between **16:14:05.2430444 and 16:14:05.5255186 UTC**, all three defaults change from Realtek to the HDMI endpoint (`ccb131…`), which becomes active. Another HDMI endpoint becomes active by **16:14:06.0785763**. Native3's later interval uses active HDMI throughout. Neither runtime has a detected FMOD/native-fatal marker or requested crash dump. The Native10 runtime audit's initial pending-audio status is closed only by this separate union review. This observed successful interval does not establish the selected Unity stream, stream continuity, physical audibility or general device-change recovery; earlier FMOD/no-sound failures remain unresolved.

## One-hour run and delivery frontier

At this checkpoint, root's ordinary Release PID **52996**, session **96988**, is running from approximately **16:29:37 UTC** under the [frozen launch recipe](../../artifacts/goal-20261014/minimum-travel-release-soak-preparation-20261016/recipe.json). It uses receipt `fc5be09f…`, helper SHA-256 `a4272d1eef8e013ef144c7e920bf41a3d6e8c9ae0f9abb53f6717485a6960148` and the supported fixed `ordinary-release-soak-3600` profile. The **3,600-second observation / 3,720-second qualification-phase deadline** is not yet completed; normal owned-window shutdown, final identity, logs, crash artifacts and closed audio coverage remain pending. The [profile report](native-soak-profile-2026-10-16.md) defines the clock boundary and resource limits.

No fresh ZIP or recorder pins have been promoted. Current delivery remains the separately qualified **`90c5c9c` / ZIP `dfdd39cc…`** described in the [prior Release report](throne-columns-release-2026-10-16.md). These results do not resolve the historical approximately 55-minute native shutdown crash cause, human 2–3-minute pacing/wayfinding, art acceptance or hardware performance.

## Independent receipt identities

| Receipt | SHA-256 |
| --- | --- |
| Ordinary Release build review | `9981f39cbe77add5a97312337be04fe97f10e53e149a22ca6d3b4c85c5569ff2` |
| [Ordinary import approval](../../artifacts/goal-20261014/minimum-travel-release-build-20261016/identity/import-review.approved.json) | `2ad3aa432e80ec2a99ec0bfbd0012eeca0bcffa435d8e441823d05f65cc69e6b` |
| Ordinary completion inventory | `09a5b3ed0215a3eac4892b0a1731062d10b23f4daa4754cac4610221230834d6` |
| Ordinary BUILD-INFO | `fc5be09f2d0063142838113472b843ab0336124add50f695d4499391dc6cef1a` |
| Native10 build review | `f7001c8866f858ff339b9debc676cd17ddb3cbd642c0bbdce06b3eda91d749d3` |
| Native10 runtime review | `e394b54e2ffc988bf8ac5653bfaba699bd4877168e2e370fa414180e0921f97b` |
| Concealed3 build review | `d167883e47b55cc43443d4bf08cae4738e94f7851a7bdd977424a83f47cb3e6b` |
| Concealed3 runtime review | `33d1f5a1397d1f79997bd39a427a30b2e39a99e8a2145262fc3fce0f3cb9ee13` |
| Closed Native10/Native3 audio union | `f373d658407470299abeda7d50abf0e66caba36ea3419bcf57f48c4bbecc378c` |
