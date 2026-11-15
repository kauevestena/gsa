# Evidence — T-F04-05 (Helmet & Front/Rear Scooter Camera Rigs & Coverage Report)

- **Estado:** Implementada e verificada localmente (`dotnet test Gsa.Domain.slnf --no-restore`, `python3 specs/validate.py`, `python3 scripts/verify.py --exports host`); em revisão técnica delegada à CI no branch `codex/m3-wave2-mobility-weather-calm-cameras`.
- **Code & deliverables:**
  - [PlatformCameraRigs.cs](../../../src/Gsa.Core/Sensors/Camera/PlatformCameraRigs.cs): `PlatformCameraRigBuilder` defines `HelmetMount`, `ScooterFrontMount`, `ScooterRearMount`, and profiles (`BicycleHelmetProfile`, `ScooterFrontProfile`, `ScooterRearProfile`), registers them on the shared `SensorCaptureAgenda` via `CreateAgendaForPlatform`, and produces `PlatformRigCoverageReport` verifying unique sample IDs, monotonic timestamps, capacity bounds, and per-sensor overlap fractions.
- **Tests:** [CameraEnvironmentOverlapAndRigsTests.cs](../../../tests/Gsa.Core.Tests/CameraEnvironmentOverlapAndRigsTests.cs) (`Gsa.Core.Tests`).

