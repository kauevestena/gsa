# Evidência T-F03-07 — Adicionar picape 4x4

## Escopo implementado
- Slot adicional `slot_lidar_b` (nível mínimo 5) em [RackAndMountCalibration.cs](../../../src/Gsa.Core/Sensors/Calibration/RackAndMountCalibration.cs).
- `PickupFourByFourRigCoordinator` e `PickupFourByFourSlopeSimulator` em [PickupFourByFourMobility.cs](../../../src/Gsa.Core/Mobility/PickupFourByFourMobility.cs):
  - Configuração de rig de nível 5 com dois LiDARs montados em `slot_lidar_a` e `slot_lidar_b`.
  - Simulação de tração 4x4, aderência em terra seca/molhada, declividade de encosta e proteção por colisão sólida (`RackImpactEvent`) evitando atravessar obstáculos.
  - Replay seco/molhado na encosta da Serra (`RunSerraSlopeReplay`).

## Verificação
- Teste automatizado em [M6SerraPickupPairedLidarAndEventsTests.cs](../../../tests/Gsa.Core.Tests/M6SerraPickupPairedLidarAndEventsTests.cs).
- Especificação em [F03-mobilidade/tasks.md](../../../specs/features/F03-mobilidade/tasks.md).

## Estado
Implementada e em verificação para o marco M6.

