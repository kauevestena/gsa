# Evidence — T-F12-03 (Scientific/Visual Replay & Quality Comparison CLI)

- **Estado:** Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #12](https://github.com/kauevestena/gsa/pull/12) (SHA testado `45fb52b6a9036d64dadaab7ee27345b609ad51a3`, merge `b2fa25f`, execução [43682314395](https://github.com/kauevestena/gsa/actions/runs/43682314395), jobs Linux `132715376978` e Windows `132715377182`); ver [resumo CI](../M3-wave3-ci/summary.json).
- **Code & deliverables:**
  - [ScientificAndVisualReplay.cs](../../../src/Gsa.Missions/Persistence/ScientificAndVisualReplay.cs): `ScientificReplayEngine` (`ReplayPackage`, `ScientificReplayResult`, `SampleVisualTrajectoryAtFps`, `BuildDidacticTruthOverlay`) separates scientific sampling from visual 30/60/120 fps trajectory interpolation, validates same-build reproducibility with SHA-256 hashes, and restricts didactic ground-truth overlays to replay mode; `QualityReportComparer` (`QualityReportComparisonResult`) compares two quality reports by status and numeric tolerance.
  - [ReplayAndQualityCliTool.cs](../../../src/Gsa.Tools/Replay/ReplayAndQualityCliTool.cs) & [ToolApplication.cs](../../../src/Gsa.Tools/ToolApplication.cs): exposed `replay-core` and `quality-report` CLI subcommands.
- **Tests:**
  - `ScientificReplayIsInvariantAcross30_60_120FpsAndRejectsFutureVersionsOrNonReplayTruth` in [M3MvpClosureAndGateTests.cs](../../../tests/Gsa.Missions.Tests/M3MvpClosureAndGateTests.cs) (`Gsa.Missions.Tests`).
  - `ReplayCoreQualityReportAndRunM3GateCliCommandsExecuteEndToEnd` in [M3ReplayAndGateCliTests.cs](../../../tests/Gsa.Tools.Tests/M3ReplayAndGateCliTests.cs) (`Gsa.Tools.Tests`).

