# Evidência T-F02-05 — Produzir Serra e terrenos especiais

## Escopo implementado
- `SerraWorldAssembler` em [SerraRegionAndSpecialTerrains.cs](../../../src/Gsa.Core/World/SerraRegionAndSpecialTerrains.cs):
  - Expansão da cidade para 400 células em 4 km² (`BuildSerraExpandedManifest`) preservando todos os IDs do MVP e do Ato 1.
  - Adição da região `serra`, conexão ao túnel Porto–Serra (`port_serra_tunnel_connection`), abóbadas/paredes de contenção, marcos de divisa (`serra_divisa_marco_*`), postes de energia (`serra_utility_pole_*`), taludes (`serra_slope_cut_*`) e obstáculos rochosos (`serra_rock_obstacle_*`).
  - Mapa semântico de terrenos (`BuildSerraTerrainMap`) com classes `Asphalt`, `DirtTrack`, `SteepDirtSlope`, `DenseCanopyCorridor` e `TunnelGalleryConnection`, mantendo altimetria ortométrica $H = h - N_g$.
  - Rotas dedicadas da picape e variantes calmas (`PickupRoutes`), além de verificação de segurança para streaming (`EvaluateStreamingCaptureSafety`).

## Verificação
- Teste automatizado em [M6SerraPickupPairedLidarAndEventsTests.cs](../../../tests/Gsa.Core.Tests/M6SerraPickupPairedLidarAndEventsTests.cs).
- Especificação em [F02-mundo/tasks.md](../../../specs/features/F02-mundo/tasks.md).

## Estado
Implementada e em verificação para o marco M6.

