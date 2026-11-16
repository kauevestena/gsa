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
Aceita — [PR #19](https://github.com/kauevestena/gsa/pull/19) (`172a23adce22ed4af548beffa02f44fae8af587b`, merge `e1ddbe58860593aa9c1dd76c1e054efb251eecc2`), execução CI [43743554214](https://github.com/kauevestena/gsa/actions/runs/43743554214) (`Foundation (Linux x64)` job `132901780478` SUCCESS, `Foundation (Windows x64)` job `132901780173` SUCCESS), [resumo CI](../M7-wave1-ci/summary.json).

