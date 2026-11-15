# Evidence — T-F03-03 (Bicycle Profile, Corridor Rules, Autonomy & Resource Gauges)

- **Estado:** Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #11](https://github.com/kauevestena/gsa/pull/11) (SHA testado `fe78e056a897bccac46ad30d21f82206625f33de`, merge `10697f3`, execução [43680847031](https://github.com/kauevestena/gsa/actions/runs/43680847031), jobs Linux `132710912429` e Windows `132710912254`); ver [resumo CI](../M3-wave2-ci/summary.json).
- **Code & deliverables:**
  - [PlatformProfile.cs](../../../src/Gsa.Core/Mobility/PlatformProfile.cs) and [LocomotionSimulator.cs](../../../src/Gsa.Core/Mobility/LocomotionSimulator.cs): Bicycle (`6.0 m/s`, `400` storage, `2400 s` autonomy) profile, `SwitchVehiclePlatform`, finite autonomy countdown, and storage capacity enforcement prior to exceeding limits (`CaptureStopReason.VirtualStorageFull`, `CaptureStopReason.AutonomyDepleted`).
  - [BicycleAndScooterMobility.cs](../../../src/Gsa.Core/Mobility/BicycleAndScooterMobility.cs): `MvpVehicleMobilityController`, `CorridorAccessSpec`, `CorridorAccessEvaluation`, and `PlatformResourceGaugeSnapshot` enforcing sidewalk/alley/cycleway/road rules and live autonomy/storage/damage gauges.
- **Tests:** [BicycleAndScooterMobilityTests.cs](../../../tests/Gsa.Core.Tests/BicycleAndScooterMobilityTests.cs) (`Gsa.Core.Tests`).

