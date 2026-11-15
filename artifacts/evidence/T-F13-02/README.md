# Evidence — T-F13-02 (Batidas descalibram montagem física)

## Escopo
Integração entre eventos de impacto com `delta-v >= 3.0 m/s` (escalado por `Pilotagem Defensiva` em +10%/posto via `CarSuspensionSimulator.ApplyCollisionImpact`) e `ImpactMisalignmentIntegrator.ApplyImpactToRack`, perturbando deterministicamente por seed a montagem física dos sensores dentro de `±0.03 m`, `±0.5°` e `±5 ms`, marcando inspeção/recalibração necessária sem revelar desvios verdadeiros na interface de sintomas (`ImpactPerturbationSymptom`).

## Implementação
- [RackAndMountCalibration.cs](../../../src/Gsa.Core/Sensors/Calibration/RackAndMountCalibration.cs)
- [CarSuspensionMobility.cs](../../../src/Gsa.Core/Mobility/CarSuspensionMobility.cs)

## Testes e Verificação
- [M4RackImuAndUrbanHazardsTests.cs](../../../tests/Gsa.Core.Tests/M4RackImuAndUrbanHazardsTests.cs) (`T_F13_02_HighDeltaVCollisionDeterministicallyPerturbsRackWithinBoundsWithoutLeakingTruth`)
- Executado `dotnet test Gsa.Domain.slnf --no-restore`: `203/203` em `Gsa.Core.Tests` e `405/405` no domínio completo, e `python3 scripts/verify_architecture.py` (`valid: true`).

## Estado
Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #14](https://github.com/kauevestena/gsa/pull/14) (SHA testado `dd961522960f09c6cea723c086f9fa6fdb8c2e92`, merge `ecc1b7c2b3c52202c734c91f124c5443131936c3`, execução [43685741329](https://github.com/kauevestena/gsa/actions/runs/43685741329), jobs Linux `132725728029` e Windows `132725728185`); ver [resumo CI](../M4-wave2-ci/summary.json).

