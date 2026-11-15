# Evidence — T-F04-04 (Environmental Effects, Stopped Cleaning, Rolling Shutter & Overlap)

- **Estado:** Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #11](https://github.com/kauevestena/gsa/pull/11) (SHA testado `fe78e056a897bccac46ad30d21f82206625f33de`, merge `10697f3`, execução [43680847031](https://github.com/kauevestena/gsa/actions/runs/43680847031), jobs Linux `132710912429` e Windows `132710912254`); ver [resumo CI](../M3-wave2-ci/summary.json).
- **Code & deliverables:**
  - [EnvironmentalCameraAndOverlap.cs](../../../src/Gsa.Core/Sensors/Camera/EnvironmentalCameraAndOverlap.cs): `LensContaminationTracker` accumulates rain/splatter obstruction and clears lens only after `>= 3 s` stopped (`< 0.1 m/s`), `RollingShutterMetrics` computes skew from image-plane speed and readout time, and `SurfaceFootprintOverlapCalculator` estimates 2D ENU support footprints (`SurfaceFootprint2DEnu`) and consecutive overlap while failing explicitly on missing geometry (`camera.overlap.insufficient_geometry`).
- **Tests:** [CameraEnvironmentOverlapAndRigsTests.cs](../../../tests/Gsa.Core.Tests/CameraEnvironmentOverlapAndRigsTests.cs) (`Gsa.Core.Tests`).

