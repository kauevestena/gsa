# Evidence — T-F07-05 (Consolidação de jobs, receitas persistentes, idempotência e invalidação de caches derivados)

## Escopo
Gerenciamento consolidado de receitas e fila única do escritório avançado (`AdvancedOfficeProcessingAndCacheCoordinator`): persistência e recarga via snapshot em qualquer fase do job (`Queued`, `Running`, `Completed`, `Cancelled`, `FailedExpired`), retomada após save/reload com débito estritamente idempotente (`adv_office_job:{JobId}` no `CareerLedger`), preservação dos hashes SHA-256 dos lotes brutos em cancelamentos e reprocessamentos, e invalidação automática de caches derivados de octree/CQ (`TryGetValidDerivedCache`) diante de mudanças na calibração ou nos parâmetros da receita.

## Implementação
- [AdvancedOfficeProcessingAndCacheCoordinator.cs](../../../src/Gsa.Missions/Office/AdvancedOfficeProcessingAndCacheCoordinator.cs)

## Testes e Verificação
- [M5CloudCqOfficeLidarWorkshopAndSabotageTests.cs](../../../tests/Gsa.Missions.Tests/M5CloudCqOfficeLidarWorkshopAndSabotageTests.cs) (`T_F07_05_ConsolidatedOfficeJobsIdempotentReloadResumeCancelHashInvarianceAndCacheInvalidation`)

## Estado
Implementada e em verificação para Onda 2 do marco M5.

