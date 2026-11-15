# Evidence — T-F04-04 (Environmental Effects, Stopped Cleaning, Rolling Shutter & Overlap)

- **Estado:** Implementada e verificada localmente (`dotnet test Gsa.Domain.slnf --no-restore`, `python3 specs/validate.py`, `python3 scripts/verify.py --exports host`); em revisão técnica delegada à CI no branch `codex/m3-wave2-mobility-weather-calm-cameras`.
- **Code & deliverables:**
  - [EnvironmentalCameraAndOverlap.cs](../../../src/Gsa.Core/Sensors/Camera/EnvironmentalCameraAndOverlap.cs): `LensContaminationTracker` accumulates rain/splatter obstruction and clears lens only after `>= 3 s` stopped (`< 0.1 m/s`), `RollingShutterMetrics` computes skew from image-plane speed and readout time, and `SurfaceFootprintOverlapCalculator` estimates 2D ENU support footprints (`SurfaceFootprint2DEnu`) and consecutive overlap while failing explicitly on missing geometry (`camera.overlap.insufficient_geometry`).
- **Tests:** [CameraEnvironmentOverlapAndRigsTests.cs](../../../tests/Gsa.Core.Tests/CameraEnvironmentOverlapAndRigsTests.cs) (`Gsa.Core.Tests`).

