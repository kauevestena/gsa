# Evidence — T-F04-05 (Helmet & Front/Rear Scooter Camera Rigs & Coverage Report)

- **Estado:** Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #11](https://github.com/kauevestena/gsa/pull/11) (SHA testado `fe78e056a897bccac46ad30d21f82206625f33de`, merge `10697f3`, execução [43680847031](https://github.com/kauevestena/gsa/actions/runs/43680847031), jobs Linux `132710912429` e Windows `132710912254`); ver [resumo CI](../M3-wave2-ci/summary.json).
- **Code & deliverables:**
  - [PlatformCameraRigs.cs](../../../src/Gsa.Core/Sensors/Camera/PlatformCameraRigs.cs): `PlatformCameraRigBuilder` defines `HelmetMount`, `ScooterFrontMount`, `ScooterRearMount`, and profiles (`BicycleHelmetProfile`, `ScooterFrontProfile`, `ScooterRearProfile`), registers them on the shared `SensorCaptureAgenda` via `CreateAgendaForPlatform`, and produces `PlatformRigCoverageReport` verifying unique sample IDs, monotonic timestamps, capacity bounds, and per-sensor overlap fractions.
- **Tests:** [CameraEnvironmentOverlapAndRigsTests.cs](../../../tests/Gsa.Core.Tests/CameraEnvironmentOverlapAndRigsTests.cs) (`Gsa.Core.Tests`).

