# Evidence — T-F15-03 (Octree em dupla precisão, chunks com SHA-256, cache LRU <= 256 MiB e LOD <= 250k)

## Escopo
Armazenamento espacial em dupla precisão ENU (`PointCloudOctreeAndChunkStore`) com octree (`<= 4.096` amostras por folha, profundidade máxima `16` e paginação de overflow sem descarte), chunks imutáveis de até `65.536` amostras com checksum SHA-256 verificado na recarga, cache LRU limitada a `<= 256 MiB`, orçamento visual LOD limitado a `<= 250.000` pontos sem alterar amostras científicas de CQ e invalidação automática de índices derivados ao atualizar a revisão da nuvem.

## Implementação
- [KombiProfilerAndOctreeStore.cs](../../../src/Gsa.Core/Sensors/Lidar/KombiProfilerAndOctreeStore.cs) (`PointCloudOctreeAndChunkStore`)

## Testes e Verificação
- [M5KombiDmiKalmanPpkAndLidarOctreeTests.cs](../../../tests/Gsa.Core.Tests/M5KombiDmiKalmanPpkAndLidarOctreeTests.cs) (`T_F15_03_DoublePrecisionOctreeChunksSha256Lru256MiBVisualLod250kAndRevisionInvalidation`)
- Executado `dotnet test Gsa.Domain.slnf --no-restore` e `python3 scripts/verify.py --exports host`: `211/211` em `Gsa.Core.Tests` (`415/415` no domínio completo: `18` Localization, `211` Core, `150` Missions, `36` Tools).

## Estado
Implementada e em verificação para Onda 1 do marco M5.

