# Project instructions

## Authoritative scope

Implement the game and level described in `Shadows of the Forsaken.docx`. The document is the source of truth, including its flow chart, top-down map and visual references. The owner's explicit later instructions override it. README is a traceable summary and status report, not a replacement design.

Read the DOCX before designing gameplay or level connections. `python3 tools/export_design.py` extracts paragraph text but does NOT expose embedded diagrams or maps. Inspect those separately before claiming layout fidelity.

The target is a short, dark gothic-castle action-adventure level: a calm courtyard introduction, a single initial threat, rune/lever environmental puzzles, a throne-room miniboss, a cursed library, secret passage/catacombs, a final fight, an optional secret and an exit. The document targets approximately 2–3 minutes on the basic route with optional secrets extending play. Preserve the distinction between mandatory progression and optional content. Do not invent extensive unrelated systems.

### Diagrams matter

Section 6's flow chart explicitly includes a miniboss and a final fight, which must not be reduced to a single initial enemy because the prose is briefer. Its main route is courtyard -> first fight/puzzle -> throne-room miniboss -> catacombs/final fight -> exit. It also connects a secret exit to the throne room.

Section 8's map separates the first encounter and puzzle spaces, marks a secret lever in the catacombs, and shows an upper-left bonus-room branch and a final fight/scene area. The library is specified in prose but has no separate map label. Do not silently omit it or assert that the two differently placed secret areas are identical. Document explicit decisions resolving these ambiguities before fixing the level layout. Do not infer a required cutscene from the word 'scene'.

### Implemented design decisions (#4)

Read `docs/design-decisions.md` and `docs/level-contract.json` before implementing the next gameplay issue. They record explicit implementation choices under the owner's instruction to select, implement and merge an issue; they are not additional text from the DOCX or a claim of separate owner review.

The library is the mandatory connector from the throne room to the catacombs. There is one optional bonus room: the catacomb lever unlocks its upper branch and a concealed return shortcut to the throne room, only after the library has been opened. The final arena is part of the catacombs; normal completion always requires the final fight. Preserve the lower puzzle/junction backtracking and do not invent a direct puzzle-to-throne passage through a map wall.

The JSON and Python validator are a design model, not the Unity progression implementation (#10), scene validation or a timed playthrough. A later change must keep decisions, contract, tests and README consistent; never auto-update the source DOCX hash merely to silence a failure.

### Runtime progression (#10)

Reuse `Assets/Progression/Core/LevelProgression.cs` and `LevelProgressionController` instead of inventing another progression state. Read `docs/progression-runtime.md`. Core rules are compared against the JSON in .NET tests; the MonoBehaviour lifecycle requires real Unity verification, currently dependent on #5 activation.

Use one scene-owned controller per game session, capture its SessionId when starting an action, and reject callbacks carrying an old session. Do not replace the token inside an old callback. A reset clears progress and notifies current observers; the physical reset of enemies, player and doors belongs to their scene components. Subscribe/unsubscribe consumers with their lifecycle. Do not mutate progression synchronously from Changed listeners or use a global/static singleton. All gameplay calls belong on Unity's main thread.

## Implementation

- Tie each feature and acceptance check to a DOCX section. Label unspecified mechanics/balance values as implementation choices.
- Keep `6000.6.3f1` and package versions pinned unless the owner requests an upgrade or a verified requirement necessitates it.
- Preserve existing script names, serialized fields and `.meta` GUIDs unless a migration is included.
- Never commit generated `Library`, `Temp`, `Logs`, `Obj`, `UserSettings`, build output, credentials or local editor state.
- Commit `.meta` files for new Unity assets and folders. Do not rewrite binary art or the original design document without a concrete need.
- Keep deterministic gameplay rules separable from Unity presentation so they can be tested headlessly; do not replace Unity integration tests with mocked claims.
- Update README's actual status with each completed feature. A technical playground is not the final level.

## Verification

Run `python3 tools/validate_level_contract.py` and `python3 -m unittest discover -s tests -p 'test_*.py' -v` for the current documentation and design tooling. Run `python3 tools/unity_validation.py project` for source integrity and `dotnet test tests/Progression/Progression.Tests.csproj --configuration Release` for real C# core tests. The .NET project compiles the same source as Unity; it does not compile or simulate MonoBehaviour. Add regression tests for new logic. Validate scene/component integration in the pinned Unity editor when available.

Never report tests that were skipped as passed. Distinguish documentation tests, headless C# tests, Unity EditMode/PlayMode tests, player builds and manual visual checks. Report unexecuted checks explicitly. Do not claim the 2–3 minute target is achieved without a timed playthrough.
