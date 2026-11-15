# Evidence — T-F08-04 (Integrar serviços, seguro e frota avançada)

## Escopo
Matriz completa de serviços operacionais (`RefuelOrCorn` `10/50`, manutenção `1%` por `10` pontos de dano, apólice de seguro `2%`, franquia de sinistro `10%`, instalação de base `20`, armazenamento temporário `1%`, flanelinha `10`, escolta `10%`, disfarce `200`, autorização em área controlada `5%`), aplicação de multas de trânsito (`20/30/40 Coords`) com abatimento via `CareerLedger`, fluxo de sinistro total restaurando veículo e sensores segurados sem recuperar lotes pendentes perdidos em trânsito no `BatchCustodyStore`, e persistência via `ServiceInsuranceSnapshot`.

## Arquivos de Implementação e Testes
- [FleetServicesAndInsurance.cs](../../../src/Gsa.Missions/Economy/FleetServicesAndInsurance.cs)
- [M4EconomyServicesAndCosmeticsTests.cs](../../../tests/Gsa.Missions.Tests/M4EconomyServicesAndCosmeticsTests.cs) (`T_F08_04_ServiceFeeMatrixInsuranceSinistroCustodyLossFinesAndSnapshotWorkDeterministically`)

## Execução e Verificação
- `dotnet test Gsa.Domain.slnf --no-restore`: `Gsa.Missions.Tests` validou todas as 10 cotações da matriz de serviços, contratação de seguro `2%`, sinistro com franquia `10%` e perda irreversível apenas de lotes pendentes, multas e round-trip JSON do snapshot.

## Estado
Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #13](https://github.com/kauevestena/gsa/pull/13) (SHA testado `8591eb655b2a2bb593d752623a91f402f97aeb76`, merge `ba18ae339d025405275ac6363ab7ee1625a1cbff`, execução [43683993914](https://github.com/kauevestena/gsa/actions/runs/43683993914), jobs Linux `132720474455` e Windows `132720474804`); ver [resumo CI](../M4-wave1-ci/summary.json).

