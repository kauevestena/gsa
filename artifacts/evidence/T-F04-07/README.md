# Evidence — T-F04-07 (Captura e extração de perfil longitudinal de pavimento por câmera descendente e LiDAR)

## Escopo
Extração longitudinal por faixas transversais de `0,25 m` e estacas longitudinais de `1,0 m` combinando câmeras descendentes de pavimento e retornos LiDAR georreferenciados (`PavementProfileExtractor`), preservando lacunas por oclusão, obstrução ou baixa densidade de forma explícita (`IsExplicitGap = true`, valores nulos nunca fabricados) e integrando diretamente com o `PavementProfileEvaluator` de CQ com geração de áreas de recoleta.

## Implementação
- [PavementProfileExtractor.cs](../../../src/Gsa.Core/Sensors/Camera/PavementProfileExtractor.cs) (`PavementProfileExtractor`)
- [PointCloudAndPavementQualityEvaluator.cs](../../../src/Gsa.Missions/Quality/PointCloudAndPavementQualityEvaluator.cs) (`PavementProfileEvaluator`)

## Testes e Verificação
- [M7Wave1CityVanLidarAndPavementTests.cs](../../../tests/Gsa.Core.Tests/M7Wave1CityVanLidarAndPavementTests.cs) (`T_F04_07_DownwardPavementCameraAndLidarStationingPreservesExplicitGaps`)
- [M7Wave1PavementCqAndFullCityFactionsTests.cs](../../../tests/Gsa.Missions.Tests/M7Wave1PavementCqAndFullCityFactionsTests.cs) (`T_F04_07_Cq_ExtractedPavementStationsPassStrictThresholdsAndExplicitGapsAreRejectedNotFabricated`)

## Estado
Implementada nesta branch; em revisão delegada à CI.

