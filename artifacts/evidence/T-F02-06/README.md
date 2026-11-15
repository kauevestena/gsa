# Evidence — T-F02-06 (Montagem da cidade inteira e rede geodésica completa)

## Escopo
Integração completa das 4 regiões urbanas (~4 km², `400` células de `100 x 100 m`, preservação integral dos IDs anteriores) e da rede geodésica primária de 8 marcos separada dos checkpoints contratuais, com deslocamento clandestino preservando integralmente as coordenadas cadastrais originais e sem mutação de lotes históricos, mantendo conversão `LocalMapFrame.Default` (`< 1e-9 m`) e consistência geoidal `H = h - N_g` durante travessia multi-região com streaming de segurança (`600 m`).

## Implementação
- [FullCityAndGeodeticNetworkAssembler.cs](../../../src/Gsa.Core/World/FullCityAndGeodeticNetworkAssembler.cs) (`FullCityGeodeticNetwork`)

## Testes e Verificação
- [M7Wave1CityVanLidarAndPavementTests.cs](../../../tests/Gsa.Core.Tests/M7Wave1CityVanLidarAndPavementTests.cs) (`T_F02_06_FullCityFourKm2GeodeticNetworkDisplacementWithoutHistoricalMutationAndTraversal`)

## Estado
Implementada nesta branch; em revisão delegada à CI.

