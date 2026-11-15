# Evidence — T-F07-05 (Consolidação de jobs, receitas persistentes, idempotência e invalidação de caches derivados)

## Escopo
Gerenciamento consolidado de receitas e fila única do escritório avançado (`AdvancedOfficeProcessingAndCacheCoordinator`): persistência e recarga via snapshot em qualquer fase do job (`Queued`, `Running`, `Completed`, `Cancelled`, `FailedExpired`), retomada após save/reload com débito estritamente idempotente (`adv_office_job:{JobId}` no `CareerLedger`), preservação dos hashes SHA-256 dos lotes brutos em cancelamentos e reprocessamentos, e invalidação automática de caches derivados de octree/CQ (`TryGetValidDerivedCache`) diante de mudanças na calibração ou nos parâmetros da receita.

## Implementação
- [AdvancedOfficeProcessingAndCacheCoordinator.cs](../../../src/Gsa.Missions/Office/AdvancedOfficeProcessingAndCacheCoordinator.cs)

## Testes e Verificação
- [M5CloudCqOfficeLidarWorkshopAndSabotageTests.cs](../../../tests/Gsa.Missions.Tests/M5CloudCqOfficeLidarWorkshopAndSabotageTests.cs) (`T_F07_05_ConsolidatedOfficeJobsIdempotentReloadResumeCancelHashInvarianceAndCacheInvalidation`)

## Estado
Aceita — PR #17 mergeado em main (88b1c23b90ba27d95ace458f1920400af66bb4c8) após aprovação nos jobs Linux (132739514446) e Windows (132739514694) da execução CI 43690357152; ver [resumo CI](../M5-wave2-ci/summary.json).

