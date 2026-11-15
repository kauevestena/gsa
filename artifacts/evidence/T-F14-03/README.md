# Evidence — T-F14-03 (Filtro de trajetória de 6 estados e redução de deriva em 100 seeds)

## Escopo
Filtro de Kalman de 6 estados `[px, py, pz, vx, vy, vz]` em dupla precisão (`F`, `B`, `Q` com densidades normativas `0,1 / 0,01 / 0,001 m²/s³` para `Mems / Tactical / Navigation`, forma de Joseph para covariância), atualizações de posição GNSS, velocidade DMI com restrição não holonômica/pista molhada e ZUPT, rastreamento de idade da solução GNSS e alertas de apagão/incerteza, além de suíte determinística de 100 seeds sem qualquer referência a `TruthSample` nas APIs de estimação.

## Implementação
- [DmiKalmanAndPpkTrajectory.cs](../../../src/Gsa.Core/Sensors/Fusion/DmiKalmanAndPpkTrajectory.cs) (`SixStateTrajectoryKalmanFilter`, `TrajectoryFusionBenchmark`)

## Testes e Verificação
- [M5KombiDmiKalmanPpkAndLidarOctreeTests.cs](../../../tests/Gsa.Core.Tests/M5KombiDmiKalmanPpkAndLidarOctreeTests.cs) (`T_F14_03_SixStateKalmanMonotonicImuGradesDmiZupt100SeedsAndNoTruthAccess`)
- Executado `dotnet test Gsa.Domain.slnf --no-restore` e `python3 scripts/verify.py --exports host`: `211/211` em `Gsa.Core.Tests` (`415/415` no domínio completo: `18` Localization, `211` Core, `150` Missions, `36` Tools).

## Estado
Aceita — [PR #16](https://github.com/kauevestena/gsa/pull/16) (`19ef117ab401c4fc0eebe17b2402a2c3c4e986d2`, merge `13e3378fd3c2922be4af044de71e3c99c8d8b35f`), execução CI [43688685548](https://github.com/kauevestena/gsa/actions/runs/43688685548) (`Foundation (Linux x64)` job `132734498865` SUCCESS, `Foundation (Windows x64)` job `132734498698` SUCCESS), [resumo CI](../M5-wave1-ci/summary.json).


