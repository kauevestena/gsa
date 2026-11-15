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
Implementada; em revisão delegada à CI (aguardando validação no PR #15 da Onda 3 de M4).

