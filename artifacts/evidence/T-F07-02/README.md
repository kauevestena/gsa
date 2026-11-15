# Evidence — T-F07-02 (Simplified Arcade & Technical Office Processing Parity)

- **Estado:** Implementada; em revisão delegada à CI no PR de M3 Wave 1 (branch `codex/m3-wave1-old-center-shop-dgnss-office`).
- **Code:**
  - [OfficeProcessingPipeline.cs](../../../src/Gsa.Missions/Office/OfficeProcessingPipeline.cs): Added `OfficeJobPreview` and `SimplifiedOfficeWorkstation` (`BuildCostAndTimePreview`, `BuildArcadeAssistedRecipe`, `BuildTechnicalManualRecipe`) implementing `600 s/batch` reduced by `10%/rank` on `SkillTrackKind.Processing`, Arcade 5% contract base fee (waived on subsidized Calm contracts when `BalanceCoords < 50`), Technical `0 Coords` fee, and deterministic operator parity so equivalent sample/feature/mask selections yield identical `QualityReport` and `PaymentQuote` outputs in both modes.
- **Tests:** [SimplifiedOfficeModesTests.cs](../../../tests/Gsa.Missions.Tests/SimplifiedOfficeModesTests.cs) (`Gsa.Missions.Tests`).

