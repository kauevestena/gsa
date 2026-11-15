# Evidence — T-F13-01 (Rack modular e separação físico/estimado)

## Escopo
Modelagem de slots nomeados do rack modular de sensores (`RackSlotKind`, `RackCatalog`, `ModularSensorRack`) com validação de compatibilidade de tipo, capacidade elétrica e peso, travas por nível de plataforma (LiDAR nível 4+, dupla antena nível 5+, oficina offline nível 3+), separação estrita entre estado físico (`PhysicalTruthMount`) e estimado (`EstimatedCalibrationMount`), histórico imutável de revisões por timestamp e preservação de estimativa obsoleta quando um sensor muda de slot.

## Implementação
- [RackAndMountCalibration.cs](../../../src/Gsa.Core/Sensors/Calibration/RackAndMountCalibration.cs)

## Testes e Verificação
- [M4RackImuAndUrbanHazardsTests.cs](../../../tests/Gsa.Core.Tests/M4RackImuAndUrbanHazardsTests.cs) (`T_F13_01_ModularRackEnforcesSlotCompatibilityCapacityImmutabilityAndRelocationDrift`)
- Executado `dotnet test Gsa.Domain.slnf --no-restore`: `203/203` em `Gsa.Core.Tests` e `405/405` no domínio completo (`18` Localization, `203` Core, `148` Missions, `36` Tools), e `python3 scripts/verify_architecture.py` (`valid: true`).

## Estado
Implementada; em revisão delegada à CI (aguardando validação no PR da Onda 2 de M4).

