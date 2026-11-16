# Player-driven training loop

Implemented as an integration correction after M7 Wave 1. This does not accept a new campaign milestone or close pending human/graphics/performance gates.

Local verification on 2026-11-16, Linux x64:

- `python3 specs/validate.py`: passed, 76 documents / 105 tasks.
- `dotnet test Gsa.Domain.slnf --no-restore -m:1`: 433 passed (219 Core, 18 Localization, 160 Missions, 36 Tools).
- `DOTNET_PROCESSOR_COUNT=1 python3 scripts/verify.py --exports host`: 22 checks passed, pinned package locks unchanged; Linux native export passed headless.
- Both the scene and the exported executable printed `GSA_PLAYABLE_TRAINING_SMOKE_OK samples=7 targets=3 rework=true reload=true balance=384`.
- The training-specific tests were repeated after save protection changes: 2/2 passed.

The integration smoke uses the same button callbacks and physics raycasts as the interactive scene. It repositions the handcart between captures for determinism; keyboard navigation, graphical layout and human playtesting are not claimed as verified.

See [training documentation](../../../docs/live-training.md) for the route, architecture, save behavior and limits. Remote CI acceptance must refer to the PR's actual tested SHA; no CI result is presumed here.
