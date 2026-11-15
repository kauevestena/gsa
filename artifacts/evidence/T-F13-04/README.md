# Evidence — T-F13-04 (Integração da oficina de calibração às passadas múltiplas de LiDAR)

## Escopo
Serviço completo de alinhamento de faixas LiDAR multi-passadas (`LidarStripAlignmentWorkshopService`, `TechnicalLidarCalibrationSession`): extração de correspondências ponto-a-plano e sincronismo temporal (`ExtrinsicPlaneCorrespondence`, `TimeSyncCorrespondence`) exclusivamente a partir de raios brutos `RawLidarRayMeasurement` e trajetória estimada (zero acesso a `PhysicalTruthMount` ou oracle), solução conjunta 7-DOF e validação via `CalibrationWorkshopAndSolvers`, redução comprovada do RMSE ponto-a-plano antes/depois, paridade científica Arcade vs Técnico com pilha de undo, atualização versionada no `ModularSensorRack`, taxa idempotente com desconto por habilidade e exportação de `LidarCalibrationRecipe` reprocessável.

## Implementação
- [LidarStripAlignmentWorkshopService.cs](../../../src/Gsa.Missions/Office/LidarStripAlignmentWorkshopService.cs)

## Testes e Verificação
- [M5CloudCqOfficeLidarWorkshopAndSabotageTests.cs](../../../tests/Gsa.Missions.Tests/M5CloudCqOfficeLidarWorkshopAndSabotageTests.cs) (`T_F13_04_LidarStripAlignmentWorkshopArcadeTechnicalParityUndoAndDiscrepancyReduction`)

## Estado
Aceita — PR #17 mergeado em main (88b1c23b90ba27d95ace458f1920400af66bb4c8) após aprovação nos jobs Linux (132739514446) e Windows (132739514694) da execução CI 43690357152; ver [resumo CI](../M5-wave2-ci/summary.json).

