# Evidence — T-F16-01 (Territórios e identidades fictícias das cinco facções)

## Escopo
Implementação do catálogo das 5 facções estritamente fictícias (`SindicatoDoMarcoTorto`, `CartelDoCimento`, `MafiaDoMapaVelho`, `SociedadeDaTerraPlana`, `MapaFacilSa`) com motivos distintos, emblemas/paletas sem referência a grupos reais (`UsesRealWorldSymbols == false`), pistas narrativas, territórios em coordenadas ENU mapeáveis por distrito (`old_center`, `port`, `waterfront`, `hills`) e bloqueio permanente de armas do jogador (`PlayerWeaponsAllowed == false`).

## Implementação
- [FactionsAndTerritoryAttention.cs](../../../src/Gsa.Core/Traffic/FactionsAndTerritoryAttention.cs)
- [en_US.po](../../../game/localization/en_US.po) & [pt_BR.po](../../../game/localization/pt_BR.po)

## Testes e Verificação
- [M4FactionsAndAttentionTests.cs](../../../tests/Gsa.Core.Tests/M4FactionsAndAttentionTests.cs) (`T_F16_01_FiveFictionalFactionsTerritoriesCluesEmblemsAndPlanningMapAreValid`)
- Executado `dotnet test Gsa.Domain.slnf --no-restore`: `205/205` em `Gsa.Core.Tests` (`409/409` no domínio completo: `18` Localization, `205` Core, `150` Missions, `36` Tools).

## Estado
Aceita (`- [x] Aceita`) por revisão técnica delegada após CI Linux/Windows do [PR #15](https://github.com/kauevestena/gsa/pull/15) (commit testado `d1fe70a311606d002d9b5ee43f9f638aed2f9d43`, merge `8a0ddd42a460a33aa0882d889ec15bc4dfd5132e` em `2026-11-15T06:52:10Z`, run [`43687238085`](https://github.com/kauevestena/gsa/actions/runs/43687238085), jobs `132730158427` Linux e `132730158568` Windows); ver [resumo CI](../M4-wave3-ci/summary.json).

