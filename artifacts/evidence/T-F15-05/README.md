# Evidence — T-F15-05 (LiDAR de engenharia da Van e otimização do subsistema de captura)

## Escopo
Aquisição LiDAR de engenharia Nível 6 (`L6`, `16.000` raios primários/s), ray caster espacialmente particionado (`CreateSpatiallyCulledRayCaster`) cientificamente idêntico ao baseline (`MaxPointDriftMetres < 1e-12`), poda temporal segura de snapshots dinâmicos (`ResolveDynamicSnapshots`), agendamento físico a 5 Hz invariável a 20/60/120 FPS e invariância do resumo SHA-256 científico da octree sob consultas visuais LOD (`250k`, `50k`, `10k`) e despejo LRU.

## Implementação
- [EngineeringVanLidarAndAcquisitionOptimizer.cs](../../../src/Gsa.Core/Sensors/Lidar/EngineeringVanLidarAndAcquisitionOptimizer.cs) (`EngineeringVanLidarAcquisitionOptimizer`)

## Testes e Verificação
- [M7Wave1CityVanLidarAndPavementTests.cs](../../../tests/Gsa.Core.Tests/M7Wave1CityVanLidarAndPavementTests.cs) (`T_F15_05_EngineeringVanLidar16kRaysSpatialCullingDynamicSnapshotsFpsInvarianceAndOctreeCqInvariance`)

## Estado
Implementada nesta branch; em revisão delegada à CI.

