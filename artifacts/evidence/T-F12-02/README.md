# Evidência — T-F12-02 (Implementar save e recuperação)

Estado: implementado em `codex/m2-wave2-stars-economy-persistence-settlement`; em revisão (aguardando CI do PR #7).

## Escopo implementado
- `SaveGameManager` (`SaveGameState`, `SaveFileEnvelope`, `RestoredCareerRuntime`) em `src/Gsa.Missions/Persistence/SaveGameManager.cs`, além de `ContractInstanceSnapshot` (`ExportSnapshot`/`FromSnapshot` + `[JsonPolymorphic]`) e `[JsonConstructor]` em `Vector3d`/`Quaterniond`/`Bounds2DEnu`:
  - Save agregado (relógios, RNG, ledger, progressão, equipamentos/montagem, custódia e contratos) em envelope `schema_version = 1` com SHA-256 e gravação atômica com rotação `current.save.json` / `previous.save.json`.
  - Carga mantém lotes pendentes como `Buffered` (nunca os promove para protegidos); save manual é bloqueado durante transferência atômica (`Offloading`).
  - JSON corrompido, falha de checksum ou `schema_version` futura emitem diagnóstico sem tocar no arquivo original e oferecem `RecoverFromPreviousGeneration()`; `RecordConfirmedVehicleLossAndAutosave` persiste perdas na geração corrente.

## Reprodução
```sh
dotnet test Gsa.Domain.slnf --filter SaveGameManagerTests --no-restore
```

