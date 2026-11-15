# Evidence — T-F16-04 (Pressão de facções e zonas seguras na cidade inteira)

## Escopo
Coordenação simultânea das 5 facções fictícias nas 4 regiões completas da cidade (`old_center`, `port`, `waterfront`, `serra`), garantia de áreas seguras protegidas (`Office`, `WifiHotspot`, `UnloadPoint`, `CalmTrainingPark`) com zero ganho de atenção, supressão de escalada e limpeza de eventos hostis, resolução não violenta integral sem impasse de combate ou armas por meio de escolta, disfarce de Kombi, turno alternativo, autorização legítima via `associacao_bairro`, detector de spoofing GNSS e registro de marcos deslocados, com paridade idêntica de rigor de CQ entre modo padrão e Cidade Calma.

## Implementação
- [FactionsAndTerritoryAttention.cs](../../../src/Gsa.Core/Traffic/FactionsAndTerritoryAttention.cs) (`FactionCatalog`, zonas `serra`)
- [FullCityFactionPressureCoordinator.cs](../../../src/Gsa.Missions/Economy/FullCityFactionPressureCoordinator.cs) (`FullCityFactionPressureCoordinator`)

## Testes e Verificação
- [M7Wave1PavementCqAndFullCityFactionsTests.cs](../../../tests/Gsa.Missions.Tests/M7Wave1PavementCqAndFullCityFactionsTests.cs) (`T_F16_04_ProtectedSafeZonesFourRegionsFiveFactionsNonViolentPlaythroughAndCalmCityParity`)

## Estado
Implementada nesta branch; em revisão delegada à CI.

