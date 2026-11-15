# Evidência — T-F12-01 (Implementar lotes e descarregamento atômico)

Estado: implementado em `codex/m2-wave2-stars-economy-persistence-settlement`; em revisão (aguardando CI do PR #7).

## Escopo implementado
- `BatchCustodyStore` (`SurveyBatchArtifact`, `CustodyManifest`, `CustodyStoreSnapshot`) em `src/Gsa.Missions/Persistence/BatchCustodyStore.cs`:
  - Serialização canônica com SHA-256 por lote (`Buffered -> Offloading -> Protected` ou `Lost`).
  - Estágio de escrita e validação de checksum antes do commit no `CustodyManifest` autoritativo.
  - `RecoverAfterCrash` reconcilia apenas pelo manifest cometido (revertendo `Offloading` não commitado para `Buffered`), `HandleVehicleLoss` descarta lotes ainda vinculados ao veículo preservando os protegidos, e descarregamento é idempotente por `BatchId`.

## Reprodução
```sh
dotnet test Gsa.Domain.slnf --filter BatchCustodyStoreTests --no-restore
```

