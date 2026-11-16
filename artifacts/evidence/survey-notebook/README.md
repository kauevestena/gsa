# Live survey notebook

Adds map, target diagnostics, photo review, last processed CQ and a short guide to the interactive training scene. R or the sidebar button opens it. Read-only presentation uses recorded camera observations and the authoritative office report.

## Corrected report boundary

Previously, loading after a new recollection offload could rebuild CQ from all protected photos even though the recollection had not been processed. Load now selects the batch-protection events preceding the last processing event. Event sequence disambiguates offload/processing at the same simulation timestamp. Field progress and unprocessed-photo count remain separate from the last office revision.

## Verification (2026-11-16)

- Domain suite: 434 tests passed; training regressions cover single-view and low-parallax explanations, rejected/empty photos, custody status and the same-timestamp unprocessed-recollection reload case.
- `DOTNET_PROCESSOR_COUNT=1 python3 scripts/verify.py --exports host`: all 22 checks passed; locks unchanged; native Linux export and smoke passed.
- After visual fixes, repeated build (zero warnings), PO import/localization smoke, native export and exported-world smoke all passed.
- Required marker: `GSA_SURVEY_NOTEBOOK_SMOKE_OK photos=7 targets=3 report=passed sections=5 north_up=true`. The pipeline requires it from both scene and native executable.
- Inspected the actual 960 × 540 native Linux window in English and Portuguese: opened notebook, browsed tabs and a captured photo, unloaded/processed, checked failed CQ values, changed language and restored the session. Fixed notebook opacity and localized the fraction unit based on this inspection.
- Native-window inspection used synthetic training data only. Screenshots were reviewed during the desktop session; image files are not included in this evidence bundle.

This is a developer UI smoke, not acceptance by the M2/M3 human cohorts, a Windows graphical check, or reference-GPU performance approval. It does not complete advanced M7 overlays or Geopédia.
