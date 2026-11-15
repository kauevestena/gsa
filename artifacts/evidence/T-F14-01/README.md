# Evidence — T-F14-01 (IMU por faixa e estimador complementar de atitude)

## Escopo
Implementação dos perfis de IMU a `100 Hz` (`Ideal`, `Mems`, `Tactical`, `Navigation`) com parâmetros normativos de ruído branco, bias inicial, passeio aleatório por `sqrt(s)` e erro de escala, gravidade local `9.80665 m/s²`, simulador determinístico com estado persistível (`ImuSimulator`, `ImuSimulatorState`) e estimador complementar de atitude (`ComplementaryAttitudeEstimator`) com ganho `1 - exp(-dt/2s)` para nivelamento estacionário (`||f| - g| < 0.1 m/s²`) e fusão de yaw via `HeadingObservation` válido, sem acesso a amostras verdadeiras na assinatura pública do estimador.

## Implementação
- [ImuAndAttitudeEstimator.cs](../../../src/Gsa.Core/Sensors/Fusion/ImuAndAttitudeEstimator.cs)

## Testes e Verificação
- [M4RackImuAndUrbanHazardsTests.cs](../../../tests/Gsa.Core.Tests/M4RackImuAndUrbanHazardsTests.cs) (`T_F14_01_ImuGradesIntegrationGravityLevelingHeadingFusionAndNavVsMemsDrift`)
- Executado `dotnet test Gsa.Domain.slnf --no-restore`: `203/203` em `Gsa.Core.Tests` e `405/405` no domínio completo, e `python3 scripts/verify_architecture.py` (`valid: true`).

## Estado
Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #14](https://github.com/kauevestena/gsa/pull/14) (SHA testado `dd961522960f09c6cea723c086f9fa6fdb8c2e92`, merge `ecc1b7c2b3c52202c734c91f124c5443131936c3`, execução [43685741329](https://github.com/kauevestena/gsa/actions/runs/43685741329), jobs Linux `132725728029` e Windows `132725728185`); ver [resumo CI](../M4-wave2-ci/summary.json).

