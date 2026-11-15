# Evidence — T-F14-02 (DMI de roda a 20 Hz, deslizamento em pista molhada e ZUPT)

## Escopo
Simulação de DMI a `20 Hz` (`sigma = 0,02 m/s`, erro de escala inicial `0,005`), escorregamento limitado a `20%` em pista molhada com multiplicação da variância longitudinal por `25x` (`sigma = 0,10 m/s`), detector estacionário ZUPT exigindo `2,0 s` contínuos dos três critérios simultâneos (`|v_dmi| < 0,05 m/s`, `||omega|| < 0,01 rad/s`, `|||f|| - g| < 0,1 m/s²`), atualização `sigma = 0,01 m/s` com estimativa de viés sem acesso à verdade e rejeição de falsas paradas por travamento de roda.

## Implementação
- [DmiKalmanAndPpkTrajectory.cs](../../../src/Gsa.Core/Sensors/Fusion/DmiKalmanAndPpkTrajectory.cs) (`WheelDmiSimulator`, `StationaryZuptDetector`)

## Testes e Verificação
- [M5KombiDmiKalmanPpkAndLidarOctreeTests.cs](../../../tests/Gsa.Core.Tests/M5KombiDmiKalmanPpkAndLidarOctreeTests.cs) (`T_F14_02_DmiNoiseScaleWetSlip25xVarianceAndZupt2SecondThreeCriteriaRejectFalseStops`)
- Executado `dotnet test Gsa.Domain.slnf --no-restore` e `python3 scripts/verify.py --exports host`: `211/211` em `Gsa.Core.Tests` (`415/415` no domínio completo: `18` Localization, `211` Core, `150` Missions, `36` Tools).

## Estado
Aceita — [PR #16](https://github.com/kauevestena/gsa/pull/16) (`19ef117ab401c4fc0eebe17b2402a2c3c4e986d2`, merge `13e3378fd3c2922be4af044de71e3c99c8d8b35f`), execução CI [43688685548](https://github.com/kauevestena/gsa/actions/runs/43688685548) (`Foundation (Linux x64)` job `132734498865` SUCCESS, `Foundation (Windows x64)` job `132734498698` SUCCESS), [resumo CI](../M5-wave1-ci/summary.json).


