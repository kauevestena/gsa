# Evidence — T-F14-01 (IMU por faixa e estimador complementar de atitude)

## Escopo
Implementação dos perfis de IMU a `100 Hz` (`Ideal`, `Mems`, `Tactical`, `Navigation`) com parâmetros normativos de ruído branco, bias inicial, passeio aleatório por `sqrt(s)` e erro de escala, gravidade local `9.80665 m/s²`, simulador determinístico com estado persistível (`ImuSimulator`, `ImuSimulatorState`) e estimador complementar de atitude (`ComplementaryAttitudeEstimator`) com ganho `1 - exp(-dt/2s)` para nivelamento estacionário (`||f| - g| < 0.1 m/s²`) e fusão de yaw via `HeadingObservation` válido, sem acesso a amostras verdadeiras na assinatura pública do estimador.

## Implementação
- [ImuAndAttitudeEstimator.cs](../../../src/Gsa.Core/Sensors/Fusion/ImuAndAttitudeEstimator.cs)

## Testes e Verificação
- [M4RackImuAndUrbanHazardsTests.cs](../../../tests/Gsa.Core.Tests/M4RackImuAndUrbanHazardsTests.cs) (`T_F14_01_ImuGradesIntegrationGravityLevelingHeadingFusionAndNavVsMemsDrift`)
- Executado `dotnet test Gsa.Domain.slnf --no-restore`: `203/203` em `Gsa.Core.Tests` e `405/405` no domínio completo, e `python3 scripts/verify_architecture.py` (`valid: true`).

## Estado
Implementada; em revisão delegada à CI (aguardando validação no PR da Onda 2 de M4).

