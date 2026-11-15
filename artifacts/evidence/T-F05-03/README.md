# Evidence — T-F05-03 (DGNSS Base Rental, Differential Correction & Baseline Decorrelation)

- **Estado:** Implementada; em revisão delegada à CI no PR de M3 Wave 1 (branch `codex/m3-wave1-old-center-shop-dgnss-office`).
- **Code:**
  - [GnssObservation.cs](../../../src/Gsa.Core/Sensors/Gnss/GnssObservation.cs) & [GnssProfile.cs](../../../src/Gsa.Core/Sensors/Gnss/GnssProfile.cs): Added `GnssSolutionKind.Dgnss` and `GnssProfile.Dgnss` (`0.5 m` base sigma).
  - [DgnssBaseStationAndCorrection.cs](../../../src/Gsa.Core/Sensors/Gnss/DgnssBaseStationAndCorrection.cs): `DgnssBaseSimulator` records base logs over open-sky survey marks (`MinimumOpenness >= 0.8`) inside `[OperatingWindowStartSeconds, OperatingWindowEndSeconds]`; `DgnssRoverCorrector` cancels shared common error while preserving local rover multipath, enforcing `0.02 m/km` spatial decorrelation, and falling back to autonomous GNSS (`gnss.dgnss.base_unavailable`, `gnss.dgnss.outside_operating_window`, `gnss.dgnss.excessive_baseline_decorrelation`).
  - [DgnssRentalService.cs](../../../src/Gsa.Missions/Office/DgnssRentalService.cs): Contracts a DGNSS base session for `20 Coords` (with Calm low-balance `< 50 Coords` subsidy support and insufficient-funds rejection).
- **Tests:** [DgnssCorrectionTests.cs](../../../tests/Gsa.Core.Tests/DgnssCorrectionTests.cs) and [FleetShopAndProgressionTests.cs](../../../tests/Gsa.Missions.Tests/FleetShopAndProgressionTests.cs).

