# Evidence — T-F14-04 (PPK com suavizador RTS, pontos de controle, isolamento CQ e heading duplo)

## Escopo
Processador PPK com remoção de erro comum via `BaseStationLog`, passagem Kalman direta + suavizador reverso de Rauch–Tung–Striebel (`C_k = P_{k|k} F_k^T P_{k+1|k}^-1`) recuperando precisão dentro de túneis, diagnóstico explícito quando o log da base está ausente ou incompatível sem melhoria fictícia, ajustamento de pontos de controle com detecção de outliers `3-sigma` e bloqueio estrito de checkpoints de CQ na estimação, além do sensor de heading por antena dupla (`1,0 m`, nível 5+, ativo parado e degradação para antena única sem fallback para verdade).

## Implementação
- [DmiKalmanAndPpkTrajectory.cs](../../../src/Gsa.Core/Sensors/Fusion/DmiKalmanAndPpkTrajectory.cs) (`DualAntennaHeadingSensor`, `PpkRtsSmootherAndControlAdjuster`)

## Testes e Verificação
- [M5KombiDmiKalmanPpkAndLidarOctreeTests.cs](../../../tests/Gsa.Core.Tests/M5KombiDmiKalmanPpkAndLidarOctreeTests.cs) (`T_F14_04_PpkRtsSmootherControlIsolation3SigmaOutliersAndDualAntennaHeadingWorkCorrectly`)
- Executado `dotnet test Gsa.Domain.slnf --no-restore` e `python3 scripts/verify.py --exports host`: `211/211` em `Gsa.Core.Tests` (`415/415` no domínio completo: `18` Localization, `211` Core, `150` Missions, `36` Tools).

## Estado
Aceita — [PR #16](https://github.com/kauevestena/gsa/pull/16) (`19ef117ab401c4fc0eebe17b2402a2c3c4e986d2`, merge `13e3378fd3c2922be4af044de71e3c99c8d8b35f`), execução CI [43688685548](https://github.com/kauevestena/gsa/actions/runs/43688685548) (`Foundation (Linux x64)` job `132734498865` SUCCESS, `Foundation (Windows x64)` job `132734498698` SUCCESS), [resumo CI](../M5-wave1-ci/summary.json).


