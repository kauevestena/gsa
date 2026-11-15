# Evidence — T-F05-03 (DGNSS Base Rental, Differential Correction & Baseline Decorrelation)

- **Estado:** Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #10](https://github.com/kauevestena/gsa/pull/10) (SHA testado `bf2ddd590bcebe58739160b63dbb9c6a27933c74`, merge `d8d7dc0`, execução [43679717318](https://github.com/kauevestena/gsa/actions/runs/43679717318), jobs Linux `132707485195` e Windows `132707484916`); ver [resumo CI](../M3-wave1-ci/summary.json).
- **Code:**
  - [GnssObservation.cs](../../../src/Gsa.Core/Sensors/Gnss/GnssObservation.cs) & [GnssProfile.cs](../../../src/Gsa.Core/Sensors/Gnss/GnssProfile.cs): Added `GnssSolutionKind.Dgnss` and `GnssProfile.Dgnss` (`0.5 m` base sigma).
  - [DgnssBaseStationAndCorrection.cs](../../../src/Gsa.Core/Sensors/Gnss/DgnssBaseStationAndCorrection.cs): `DgnssBaseSimulator` records base logs over open-sky survey marks (`MinimumOpenness >= 0.8`) inside `[OperatingWindowStartSeconds, OperatingWindowEndSeconds]`; `DgnssRoverCorrector` cancels shared common error while preserving local rover multipath, enforcing `0.02 m/km` spatial decorrelation, and falling back to autonomous GNSS (`gnss.dgnss.base_unavailable`, `gnss.dgnss.outside_operating_window`, `gnss.dgnss.excessive_baseline_decorrelation`).
  - [DgnssRentalService.cs](../../../src/Gsa.Missions/Office/DgnssRentalService.cs): Contracts a DGNSS base session for `20 Coords` (with Calm low-balance `< 50 Coords` subsidy support and insufficient-funds rejection).
- **Tests:** [DgnssCorrectionTests.cs](../../../tests/Gsa.Core.Tests/DgnssCorrectionTests.cs) and [FleetShopAndProgressionTests.cs](../../../tests/Gsa.Missions.Tests/FleetShopAndProgressionTests.cs).

