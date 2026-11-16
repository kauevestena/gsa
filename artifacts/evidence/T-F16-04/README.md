# Evidence — T-F16-04 (Pressão de facções e zonas seguras na cidade inteira)

## Escopo
Coordenação simultânea das 5 facções fictícias nas 4 regiões completas da cidade (`old_center`, `port`, `waterfront`, `serra`), garantia de áreas seguras protegidas (`Office`, `WifiHotspot`, `UnloadPoint`, `CalmTrainingPark`) com zero ganho de atenção, supressão de escalada e limpeza de eventos hostis, resolução não violenta integral sem impasse de combate ou armas por meio de escolta, disfarce de Kombi, turno alternativo, autorização legítima via `associacao_bairro`, detector de spoofing GNSS e registro de marcos deslocados, com paridade idêntica de rigor de CQ entre modo padrão e Cidade Calma.

## Implementação
- [FactionsAndTerritoryAttention.cs](../../../src/Gsa.Core/Traffic/FactionsAndTerritoryAttention.cs) (`FactionCatalog`, zonas `serra`)
- [FullCityFactionPressureCoordinator.cs](../../../src/Gsa.Missions/Economy/FullCityFactionPressureCoordinator.cs) (`FullCityFactionPressureCoordinator`)

## Testes e Verificação
- [M7Wave1PavementCqAndFullCityFactionsTests.cs](../../../tests/Gsa.Missions.Tests/M7Wave1PavementCqAndFullCityFactionsTests.cs) (`T_F16_04_ProtectedSafeZonesFourRegionsFiveFactionsNonViolentPlaythroughAndCalmCityParity`)

## Estado
Aceita — [PR #19](https://github.com/kauevestena/gsa/pull/19) (`172a23adce22ed4af548beffa02f44fae8af587b`, merge `e1ddbe58860593aa9c1dd76c1e054efb251eecc2`), execução CI [43743554214](https://github.com/kauevestena/gsa/actions/runs/43743554214) (`Foundation (Linux x64)` job `132901780478` SUCCESS, `Foundation (Windows x64)` job `132901780173` SUCCESS), [resumo CI](../M7-wave1-ci/summary.json).

