# Evidence — T-F07-03 (Pipeline avançado de processamento em escritório: PPK, checkpoints e filtro de outliers)

## Escopo
Orquestração ponta a ponta do processamento avançado de levantamento móvel (`AdvancedOfficeProcessingAndCacheCoordinator.ExecutePipeline`): suavização de trajetória PPK + ajustamento por pontos de controle com isolamento estrito de checkpoints reservados de CQ (`IsCheckpoint == true`) na etapa pós-ajuste, diagnóstico honesto de falha quando o log da base GNSS está ausente ou incompatível sem uso de oracle, e filtro espaço-temporal e de intensidade sem semântica de verdade (`PointCloudOutlierFilterSession` / `SpuriousPointFilterSpec`) com preview, desfazimento (`undo`) e recálculo automático de cobertura/densidade.

## Implementação
- [AdvancedOfficeProcessingAndCacheCoordinator.cs](../../../src/Gsa.Missions/Office/AdvancedOfficeProcessingAndCacheCoordinator.cs)

## Testes e Verificação
- [M5CloudCqOfficeLidarWorkshopAndSabotageTests.cs](../../../tests/Gsa.Missions.Tests/M5CloudCqOfficeLidarWorkshopAndSabotageTests.cs) (`T_F07_03_AdvancedOfficePipelinePpkCheckpointIsolationSpuriousFilterPreviewUndoAndMissingBase`)

## Estado
Aceita — PR #17 mergeado em main (88b1c23b90ba27d95ace458f1920400af66bb4c8) após aprovação nos jobs Linux (132739514446) e Windows (132739514694) da execução CI 43690357152; ver [resumo CI](../M5-wave2-ci/summary.json).

