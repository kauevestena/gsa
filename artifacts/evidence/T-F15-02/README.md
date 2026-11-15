# Evidence — T-F15-02 (Perfilador LiDAR na Kombi e georreferenciamento físico por raio)

## Escopo
Aquisição contínua por perfilador LiDAR com timestamp físico individual por raio (`t = t_raio + delta_t`), interpolação linear de posição e SLERP de atitude sobre `EstimatedBodyTrajectory`, aplicação da cadeia completa `p_map = r_body(t) + R_body_to_map(t) * (lever_arm + R_sensor_to_body * p_sensor)`, preservação de oclusões, atenuação por incidência `max(0,1, cos theta)` e refletância de material, espaçamento longitudinal dependente da velocidade, rejeição de raios fora da janela de trajetória e correção da mesma nuvem bruta após calibração/PPK sem acesso à verdade.

## Implementação
- [KombiProfilerAndOctreeStore.cs](../../../src/Gsa.Core/Sensors/Lidar/KombiProfilerAndOctreeStore.cs) (`LidarRayGeoreferencer`, `KombiLidarProfilerAcquisition`)

## Testes e Verificação
- [M5KombiDmiKalmanPpkAndLidarOctreeTests.cs](../../../tests/Gsa.Core.Tests/M5KombiDmiKalmanPpkAndLidarOctreeTests.cs) (`T_F15_02_KombiProfilerPerRayGeoreferencingOcclusionSpeedSpacingOutOfWindowAndReprocessingWork`)
- Executado `dotnet test Gsa.Domain.slnf --no-restore` e `python3 scripts/verify.py --exports host`: `211/211` em `Gsa.Core.Tests` (`415/415` no domínio completo: `18` Localization, `211` Core, `150` Missions, `36` Tools).

## Estado
Aceita — [PR #16](https://github.com/kauevestena/gsa/pull/16) (`19ef117ab401c4fc0eebe17b2402a2c3c4e986d2`, merge `13e3378fd3c2922be4af044de71e3c99c8d8b35f`), execução CI [43688685548](https://github.com/kauevestena/gsa/actions/runs/43688685548) (`Foundation (Linux x64)` job `132734498865` SUCCESS, `Foundation (Windows x64)` job `132734498698` SUCCESS), [resumo CI](../M5-wave1-ci/summary.json).


