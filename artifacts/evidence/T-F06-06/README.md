# Evidence — T-F06-06 (CQ de nuvem de pontos e interface de perfil de pavimento)

## Escopo
Avaliação científica de CQ para nuvem de pontos georreferenciada (`PointCloudQualityEvaluator`) em células úteis recortadas (`SurveySurfaceReference.RasterizeUsefulCells`), deduplicação estrita por `SampleId` e assinatura espaço-temporal sem inflação de densidade por reenvio, invariância garantida em relação ao orçamento visual de LOD (`PointCloudOctreeAndChunkStore.QueryVisualLod`), rejeição explícita de nuvem vazia e contraexemplos de oclusão com emissão de `RecollectionArea`, resíduos ponto-a-plano e precisão relativa entre checkpoints, além do contrato de CQ de pavimento longitudinal/transversal (`IPavementProfileEvaluator`, `PavementProfileEvaluator`).

## Implementação
- [PointCloudAndPavementQualityEvaluator.cs](../../../src/Gsa.Missions/Quality/PointCloudAndPavementQualityEvaluator.cs)

## Testes e Verificação
- [M5CloudCqOfficeLidarWorkshopAndSabotageTests.cs](../../../tests/Gsa.Missions.Tests/M5CloudCqOfficeLidarWorkshopAndSabotageTests.cs) (`T_F06_06_PointCloudDensityDeduplicationLodInvarianceOcclusionAndPavementEvaluator`)

## Estado
Aceita — PR #17 mergeado em main (88b1c23b90ba27d95ace458f1920400af66bb4c8) após aprovação nos jobs Linux (132739514446) e Windows (132739514694) da execução CI 43690357152; ver [resumo CI](../M5-wave2-ci/summary.json).

