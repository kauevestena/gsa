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
Implementada; em revisão delegada à CI (aguardando validação no PR da Onda 2 de M4).

