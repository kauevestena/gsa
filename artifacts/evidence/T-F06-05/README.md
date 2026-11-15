# Evidência — T-F06-05 (Integrar retrabalho e liquidação idempotente)

Estado: aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #7 (`d5ba4ea`, run `43675243347`); ver `artifacts/evidence/M2-wave2-ci/summary.json`.

## Escopo implementado
- `ContractSettlementCoordinator` e `CareerProgressionState` em `src/Gsa.Missions/Contracts/ContractSettlementCoordinator.cs`:
  - `BuildMergedSubmission` reúne apenas lotes protegidos do contrato, deduplicando `SampleId`, `(SampleId, SurfaceId)`, `TargetId` e `CheckpointId`.
  - `EvaluateProtectedBatchesAndUpdateQc` executa o ciclo `Coletando/Retrabalho -> ProntoProcessar -> Processando -> EmCQ -> ProntoEntregar/Retrabalho`.
  - `SettleDelivery` garante idempotência ponta a ponta entre `ContractStateMachine` (`settlement:{ContractId}`), `CareerLedger` e `CareerProgressionState` (XP, reputação por cliente/região sem dupla contagem global, mínimo `0` por entidade, e desbloqueios).

## Reprodução
```sh
dotnet test Gsa.Domain.slnf --filter ContractSettlementTests --no-restore
```

