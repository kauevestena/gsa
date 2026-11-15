# Evidence — T-F07-02 (Simplified Arcade & Technical Office Processing Parity)

- **Estado:** Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #10](https://github.com/kauevestena/gsa/pull/10) (SHA testado `bf2ddd590bcebe58739160b63dbb9c6a27933c74`, merge `d8d7dc0`, execução [43679717318](https://github.com/kauevestena/gsa/actions/runs/43679717318), jobs Linux `132707485195` e Windows `132707484916`); ver [resumo CI](../M3-wave1-ci/summary.json).
- **Code:**
  - [OfficeProcessingPipeline.cs](../../../src/Gsa.Missions/Office/OfficeProcessingPipeline.cs): Added `OfficeJobPreview` and `SimplifiedOfficeWorkstation` (`BuildCostAndTimePreview`, `BuildArcadeAssistedRecipe`, `BuildTechnicalManualRecipe`) implementing `600 s/batch` reduced by `10%/rank` on `SkillTrackKind.Processing`, Arcade 5% contract base fee (waived on subsidized Calm contracts when `BalanceCoords < 50`), Technical `0 Coords` fee, and deterministic operator parity so equivalent sample/feature/mask selections yield identical `QualityReport` and `PaymentQuote` outputs in both modes.
- **Tests:** [SimplifiedOfficeModesTests.cs](../../../tests/Gsa.Missions.Tests/SimplifiedOfficeModesTests.cs) (`Gsa.Missions.Tests`).

