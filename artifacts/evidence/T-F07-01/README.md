# Evidência — T-F07-01 (Criar processamento básico por receita)

Estado: implementado em `codex/m2-wave3-office-masks-hud-board`; em revisão (aguardando CI do PR #8).

## Escopo implementado
- `OfficeProcessingQueue` (`ProcessingRecipe`, `ProcessingQueueQuote`, `ProcessedProduct`, `ProcessingJob`) em `src/Gsa.Missions/Office/OfficeProcessingPipeline.cs` e `ContractStateMachine.CancelProcessing` em `src/Gsa.Missions/Contracts/ContractStateMachine.cs`:
  - Prévia de duração de calendário, taxa e risco de expiração (`PreviewJob`).
  - Fila de um job ativo por vez, débito idempotente por `JobId` (com subsídio em contrato calmo `< 50` Coords) e preservação dos hashes SHA-256 dos lotes brutos em `BatchCustodyStore`.
  - Avanço de tempo de calendário (`AdvanceJob`), falha explícita sem produto entregável ao expirar, cancelamento seguro descartando saídas temporárias e reprocessamento incrementando `Revision` com hash canônico de receita.

## Reprodução
```sh
dotnet test Gsa.Domain.slnf --filter OfficeProcessingTests --no-restore
```

