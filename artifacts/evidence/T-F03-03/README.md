# Evidence — T-F03-03 (Bicycle Profile, Corridor Rules, Autonomy & Resource Gauges)

- **Estado:** Implementada e verificada localmente (`dotnet test Gsa.Domain.slnf --no-restore`, `python3 specs/validate.py`, `python3 scripts/verify.py --exports host`); em revisão técnica delegada à CI no branch `codex/m3-wave2-mobility-weather-calm-cameras`.
- **Code & deliverables:**
  - [PlatformProfile.cs](../../../src/Gsa.Core/Mobility/PlatformProfile.cs) and [LocomotionSimulator.cs](../../../src/Gsa.Core/Mobility/LocomotionSimulator.cs): Bicycle (`6.0 m/s`, `400` storage, `2400 s` autonomy) profile, `SwitchVehiclePlatform`, finite autonomy countdown, and storage capacity enforcement prior to exceeding limits (`CaptureStopReason.VirtualStorageFull`, `CaptureStopReason.AutonomyDepleted`).
  - [BicycleAndScooterMobility.cs](../../../src/Gsa.Core/Mobility/BicycleAndScooterMobility.cs): `MvpVehicleMobilityController`, `CorridorAccessSpec`, `CorridorAccessEvaluation`, and `PlatformResourceGaugeSnapshot` enforcing sidewalk/alley/cycleway/road rules and live autonomy/storage/damage gauges.
- **Tests:** [BicycleAndScooterMobilityTests.cs](../../../tests/Gsa.Core.Tests/BicycleAndScooterMobilityTests.cs) (`Gsa.Core.Tests`).

