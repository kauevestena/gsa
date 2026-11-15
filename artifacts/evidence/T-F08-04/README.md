# Evidence — T-F08-04 (Integrar serviços, seguro e frota avançada)

## Escopo
Matriz completa de serviços operacionais (`RefuelOrCorn` `10/50`, manutenção `1%` por `10` pontos de dano, apólice de seguro `2%`, franquia de sinistro `10%`, instalação de base `20`, armazenamento temporário `1%`, flanelinha `10`, escolta `10%`, disfarce `200`, autorização em área controlada `5%`), aplicação de multas de trânsito (`20/30/40 Coords`) com abatimento via `CareerLedger`, fluxo de sinistro total restaurando veículo e sensores segurados sem recuperar lotes pendentes perdidos em trânsito no `BatchCustodyStore`, e persistência via `ServiceInsuranceSnapshot`.

## Arquivos de Implementação e Testes
- [FleetServicesAndInsurance.cs](../../../src/Gsa.Missions/Economy/FleetServicesAndInsurance.cs)
- [M4EconomyServicesAndCosmeticsTests.cs](../../../tests/Gsa.Missions.Tests/M4EconomyServicesAndCosmeticsTests.cs) (`T_F08_04_ServiceFeeMatrixInsuranceSinistroCustodyLossFinesAndSnapshotWorkDeterministically`)

## Execução e Verificação
- `dotnet test Gsa.Domain.slnf --no-restore`: `Gsa.Missions.Tests` validou todas as 10 cotações da matriz de serviços, contratação de seguro `2%`, sinistro com franquia `10%` e perda irreversível apenas de lotes pendentes, multas e round-trip JSON do snapshot.

## Estado
Implementada; em revisão delegada à CI.

