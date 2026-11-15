# Evidence — T-F03-06 (Kombi de mapeamento móvel, rack/armazenamento e contrato de túnel)

## Escopo
Configuração da plataforma Kombi (nível 4, `16 m/s`, `14.000 Coords`, `4.000` unidades virtuais a `1.000 retornos/unidade`, `3.600 s` / `60 min` de autonomia, desbloqueios `lidar_l4`, `dmi`, `imu_tatica`, `ppk`), validação de slots e orçamentos de potência/peso via `ModularSensorRack`, bloqueio de upgrades nível 5 no nível 4 (`L5` e heading duplo) e planejamento/simulação de corredor de túnel com pontos de descarga (`KombiSurveyRigCoordinator`).

## Implementação
- [KombiSurveyRigAndTunnelBudget.cs](../../../src/Gsa.Core/Mobility/KombiSurveyRigAndTunnelBudget.cs)
- [PlatformProfile.cs](../../../src/Gsa.Core/Mobility/PlatformProfile.cs)

## Testes e Verificação
- [M5KombiDmiKalmanPpkAndLidarOctreeTests.cs](../../../tests/Gsa.Core.Tests/M5KombiDmiKalmanPpkAndLidarOctreeTests.cs) (`T_F03_06_KombiProfileRigLimitsStorageConversionAndTunnelCorridorBudgetWorkDeterministically`)
- Executado `dotnet test Gsa.Domain.slnf --no-restore` e `python3 scripts/verify.py --exports host`: `211/211` em `Gsa.Core.Tests` (`415/415` no domínio completo: `18` Localization, `211` Core, `150` Missions, `36` Tools).

## Estado
Aceita — [PR #16](https://github.com/kauevestena/gsa/pull/16) (`19ef117ab401c4fc0eebe17b2402a2c3c4e986d2`, merge `13e3378fd3c2922be4af044de71e3c99c8d8b35f`), execução CI [43688685548](https://github.com/kauevestena/gsa/actions/runs/43688685548) (`Foundation (Linux x64)` job `132734498865` SUCCESS, `Foundation (Windows x64)` job `132734498698` SUCCESS), [resumo CI](../M5-wave1-ci/summary.json).


