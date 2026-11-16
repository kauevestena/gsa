# Evidence — T-F03-08 (Van MMS configurada com suíte completa e aposentadoria de Gimbalina)

## Escopo
Plataforma Van MMS de Nível 6 (`18.0 m/s`, `16.000` unidades virtuais, `5400 s` de autonomia) equipada no `ModularSensorRack` com LiDAR duplo `L6` (`slot_lidar_a`, `slot_lidar_b`), câmera 360, câmeras descendentes duplas de pavimento (`slot_pavement_cam_a`, `slot_pavement_cam_b`), GNSS dupla antena, IMU de navegação e DMI de roda dentro do envelope de energia e massa, preservação completa dos veículos anteriores (níveis 1–5), replay instrumentado com parada por armazenamento/autonomia e remapeamento de captura, e aposentadoria feliz da Gimbalina como mascote no painel sem erro físico nem ferimentos (`Injured = false`, `Stress = 0.0`).

## Implementação
- [RackAndMountCalibration.cs](../../../src/Gsa.Core/Sensors/Calibration/RackAndMountCalibration.cs) (`RackSlotKind.PavementCamera`, `slot_pavement_cam_a`, `slot_pavement_cam_b`)
- [VanMmsRigAndMascotCoordinator.cs](../../../src/Gsa.Core/Mobility/VanMmsRigAndMascotCoordinator.cs) (`VanMmsRigCoordinator`, `GimbalinaDashboardMascotState`)

## Testes e Verificação
- [M7Wave1CityVanLidarAndPavementTests.cs](../../../tests/Gsa.Core.Tests/M7Wave1CityVanLidarAndPavementTests.cs) (`T_F03_08_EngineeringVanLevel6DualLidarPavementCamerasSuiteMascotRetirementFleetPreservationAndReplay`)

## Estado
Aceita — [PR #19](https://github.com/kauevestena/gsa/pull/19) (`172a23adce22ed4af548beffa02f44fae8af587b`, merge `e1ddbe58860593aa9c1dd76c1e054efb251eecc2`), execução CI [43743554214](https://github.com/kauevestena/gsa/actions/runs/43743554214) (`Foundation (Linux x64)` job `132901780478` SUCCESS, `Foundation (Windows x64)` job `132901780173` SUCCESS), [resumo CI](../M7-wave1-ci/summary.json).

