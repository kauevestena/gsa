# Evidence — T-F13-01 (Rack modular e separação físico/estimado)

## Escopo
Modelagem de slots nomeados do rack modular de sensores (`RackSlotKind`, `RackCatalog`, `ModularSensorRack`) com validação de compatibilidade de tipo, capacidade elétrica e peso, travas por nível de plataforma (LiDAR nível 4+, dupla antena nível 5+, oficina offline nível 3+), separação estrita entre estado físico (`PhysicalTruthMount`) e estimado (`EstimatedCalibrationMount`), histórico imutável de revisões por timestamp e preservação de estimativa obsoleta quando um sensor muda de slot.

## Implementação
- [RackAndMountCalibration.cs](../../../src/Gsa.Core/Sensors/Calibration/RackAndMountCalibration.cs)

## Testes e Verificação
- [M4RackImuAndUrbanHazardsTests.cs](../../../tests/Gsa.Core.Tests/M4RackImuAndUrbanHazardsTests.cs) (`T_F13_01_ModularRackEnforcesSlotCompatibilityCapacityImmutabilityAndRelocationDrift`)
- Executado `dotnet test Gsa.Domain.slnf --no-restore`: `203/203` em `Gsa.Core.Tests` e `405/405` no domínio completo (`18` Localization, `203` Core, `148` Missions, `36` Tools), e `python3 scripts/verify_architecture.py` (`valid: true`).

## Estado
Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #14](https://github.com/kauevestena/gsa/pull/14) (SHA testado `dd961522960f09c6cea723c086f9fa6fdb8c2e92`, merge `ecc1b7c2b3c52202c734c91f124c5443131936c3`, execução [43685741329](https://github.com/kauevestena/gsa/actions/runs/43685741329), jobs Linux `132725728029` e Windows `132725728185`); ver [resumo CI](../M4-wave2-ci/summary.json).

