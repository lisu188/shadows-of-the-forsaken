# Project instructions

## Authoritative scope

Implement the game and level described in `Shadows of the Forsaken.docx`. The document is the source of truth, including its flow chart, top-down map and visual references. The owner's explicit later instructions override it. README is a traceable summary and status report, not a replacement design.

Read the DOCX before designing gameplay or level connections. `python3 tools/export_design.py` extracts paragraph text but does NOT expose embedded diagrams or maps. Inspect those separately before claiming layout fidelity.

The target is a short, dark gothic-castle action-adventure level: a calm courtyard introduction, a single initial threat, rune/lever environmental puzzles, a throne-room miniboss, a cursed library, secret passage/catacombs, a final fight, an optional secret and an exit. The document targets approximately 2–3 minutes on the basic route with optional secrets extending play. Preserve the distinction between mandatory progression and optional content. Do not invent extensive unrelated systems.

### Diagrams matter

Section 6's flow chart explicitly includes a miniboss and a final fight, which must not be reduced to a single initial enemy because the prose is briefer. Its main route is courtyard -> first fight/puzzle -> throne-room miniboss -> catacombs/final fight -> exit. It also connects a secret exit to the throne room.

Section 8's map separates the first encounter and puzzle spaces, marks a secret lever in the catacombs, and shows an upper-left bonus-room branch and a final fight/scene area. The library is specified in prose but has no separate map label. Do not silently omit it or assert that the two differently placed secret areas are identical. Document explicit decisions resolving these ambiguities before fixing the level layout. Do not infer a required cutscene from the word 'scene'.

## Implementation

- Tie each feature and acceptance check to a DOCX section. Label unspecified mechanics/balance values as implementation choices.
- Keep `6000.0.24f1` and package versions pinned unless the owner requests an upgrade or a verified requirement necessitates it.
- Preserve existing script names, serialized fields and `.meta` GUIDs unless a migration is included.
- Never commit generated `Library`, `Temp`, `Logs`, `Obj`, `UserSettings`, build output, credentials or local editor state.
- Commit `.meta` files for new Unity assets and folders. Do not rewrite binary art or the original design document without a concrete need.
- Keep deterministic gameplay rules separable from Unity presentation so they can be tested headlessly; do not replace Unity integration tests with mocked claims.
- Update README's actual status with each completed feature. A technical playground is not the final level.

## Verification

Run `python3 -m unittest discover -s tests -p 'test_*.py' -v` for the current documentation tooling. Add regression tests for new logic. Validate scene/component integration in the pinned Unity editor when available.

Never report tests that were skipped as passed. Distinguish documentation tests, headless C# tests, Unity EditMode/PlayMode tests, player builds and manual visual checks. Report unexecuted checks explicitly. Do not claim the 2–3 minute target is achieved without a timed playthrough.
