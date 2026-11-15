# Evidência — T-F06-03 (Implementar CQ de fotos e inventário)

Estado: aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #6 (`876ca1d`, run `43674076883`); ver `artifacts/evidence/M2-wave1-ci/summary.json`.

## Escopo implementado
- `EvaluationReference` isolada em `Gsa.Missions.Quality` (verificado por reflexão contra qualquer acoplamento em `Gsa.Core`).
- `QualityEvaluator.Evaluate` + `QualityReport` (`schema_version = 1`): rasterização de área útil com recorte exato em células `2×2 m` e exclusões, deduplicação por `SampleId` e `TargetId`, falha determinística de submissões vazias (nunca `NaN`/`1.0`), `absolute_rmse` exigindo `>= 3` checkpoints (`insufficient_data` quando menor), `relative_rmse`, predicados binários `privacy_redacted`/`format_valid`/`all_required_features`/`overlap_ok`, `failure_reason_keys`, `recollection_areas` e avaliação por entregável para entrega parcial.

## Reprodução
```sh
dotnet test Gsa.Domain.slnf --filter QualityEvaluatorTests --no-restore
```

