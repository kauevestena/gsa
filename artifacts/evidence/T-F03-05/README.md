# Evidence — T-F03-05 (Implementar carro com suspensão)

## Escopo
Modelagem física e de catálogo das plataformas automotivas Nível 3–6 (`Car`, `Kombi`, `Pickup`, `Van`), exclusão de veículos automotivos de ciclovias e calçadas, simulador de suspensão com 4 rodas (`CarSuspensionSimulator`), estabilização de pitch/roll em frenagem/curva/lombada e emissão determinística de eventos de impacto no rack (`RackImpactEvent`) com mitigação por habilidade de direção defensiva.

## Arquivos de Implementação e Testes
- [PlatformProfile.cs](../../../src/Gsa.Core/Mobility/PlatformProfile.cs)
- [BicycleAndScooterMobility.cs](../../../src/Gsa.Core/Mobility/BicycleAndScooterMobility.cs)
- [CarSuspensionMobility.cs](../../../src/Gsa.Core/Mobility/CarSuspensionMobility.cs)
- [M4WorldCar360AndRtkTests.cs](../../../tests/Gsa.Core.Tests/M4WorldCar360AndRtkTests.cs) (`T_F03_05_AutomotivePlatformsSuspensionBrakingCorneringBumpsAndRackImpactsBehaveRealistically`)

## Execução e Verificação
- `dotnet test Gsa.Domain.slnf --no-restore`: `Gsa.Core.Tests` validou exclusão viária em calçadas/ciclovias, forças e compressões das 4 rodas, frenagem, curva, lombada e limiares de impacto com e sem bônus de direção defensiva.

## Estado
Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #13](https://github.com/kauevestena/gsa/pull/13) (SHA testado `8591eb655b2a2bb593d752623a91f402f97aeb76`, merge `ba18ae339d025405275ac6363ab7ee1625a1cbff`, execução [43683993914](https://github.com/kauevestena/gsa/actions/runs/43683993914), jobs Linux `132720474455` e Windows `132720474804`); ver [resumo CI](../M4-wave1-ci/summary.json).

