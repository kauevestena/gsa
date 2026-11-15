# Evidence — T-F13-04 (Integração da oficina de calibração às passadas múltiplas de LiDAR)

## Escopo
Serviço completo de alinhamento de faixas LiDAR multi-passadas (`LidarStripAlignmentWorkshopService`, `TechnicalLidarCalibrationSession`): extração de correspondências ponto-a-plano e sincronismo temporal (`ExtrinsicPlaneCorrespondence`, `TimeSyncCorrespondence`) exclusivamente a partir de raios brutos `RawLidarRayMeasurement` e trajetória estimada (zero acesso a `PhysicalTruthMount` ou oracle), solução conjunta 7-DOF e validação via `CalibrationWorkshopAndSolvers`, redução comprovada do RMSE ponto-a-plano antes/depois, paridade científica Arcade vs Técnico com pilha de undo, atualização versionada no `ModularSensorRack`, taxa idempotente com desconto por habilidade e exportação de `LidarCalibrationRecipe` reprocessável.

## Implementação
- [LidarStripAlignmentWorkshopService.cs](../../../src/Gsa.Missions/Office/LidarStripAlignmentWorkshopService.cs)

## Testes e Verificação
- [M5CloudCqOfficeLidarWorkshopAndSabotageTests.cs](../../../tests/Gsa.Missions.Tests/M5CloudCqOfficeLidarWorkshopAndSabotageTests.cs) (`T_F13_04_LidarStripAlignmentWorkshopArcadeTechnicalParityUndoAndDiscrepancyReduction`)

## Estado
Implementada e em verificação para Onda 2 do marco M5.

