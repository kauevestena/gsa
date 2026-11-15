# Evidência T-F15-04 — Adicionar sensores pareados e múltiplos retornos

## Escopo implementado
- `PairedMultiReturnLidarSimulator` em [PairedMultiReturnLidarAcquisition.cs](../../../src/Gsa.Core/Sensors/Lidar/PairedMultiReturnLidarAcquisition.cs):
  - Aquisição pareada de nível 5 com 2×8 canais a 5 Hz usando a montagem e extrínsecos individuais de `slot_lidar_a` e `slot_lidar_b`.
  - Traçado de pulsos com múltiplos retornos (até 3 retornos por pulso, `VegetationAdvanceMetres`, `TransmissionFactor`, sem duplicação de `TargetId` e sem fabricação em lacunas).
  - Atenuação por ângulo de incidência (lei do cosseno) e efeitos de chuva (fator 0,8× de alcance e gotas espúrias).
  - Replay de aquisição em vegetação da Serra (`RunSerraVegetationReplay`).

## Verificação
- Teste automatizado em [M6SerraPickupPairedLidarAndEventsTests.cs](../../../tests/Gsa.Core.Tests/M6SerraPickupPairedLidarAndEventsTests.cs).
- Especificação em [F15-lidar/tasks.md](../../../specs/features/F15-lidar/tasks.md).

## Estado
Aceita — PR #18 (commit `61214b8`, merge `11bb19b`, execução CI `43692103902`: Linux `132744736874` SUCCESS, Windows `132744737031` SUCCESS). Ver [resumo M6 CI](../M6-ci/summary.json).

