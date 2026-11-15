# Evidence — T-F12-03 (Scientific/Visual Replay & Quality Comparison CLI)

- **Estado:** Implementada; em revisão delegada à CI no branch `codex/m3-wave3-mvp-closure-and-gate`.
- **Code & deliverables:**
  - [ScientificAndVisualReplay.cs](../../../src/Gsa.Missions/Persistence/ScientificAndVisualReplay.cs): `ScientificReplayEngine` (`ReplayPackage`, `ScientificReplayResult`, `SampleVisualTrajectoryAtFps`, `BuildDidacticTruthOverlay`) separates scientific sampling from visual 30/60/120 fps trajectory interpolation, validates same-build reproducibility with SHA-256 hashes, and restricts didactic ground-truth overlays to replay mode; `QualityReportComparer` (`QualityReportComparisonResult`) compares two quality reports by status and numeric tolerance.
  - [ReplayAndQualityCliTool.cs](../../../src/Gsa.Tools/Replay/ReplayAndQualityCliTool.cs) & [ToolApplication.cs](../../../src/Gsa.Tools/ToolApplication.cs): exposed `replay-core` and `quality-report` CLI subcommands.
- **Tests:**
  - `ScientificReplayIsInvariantAcross30_60_120FpsAndRejectsFutureVersionsOrNonReplayTruth` in [M3MvpClosureAndGateTests.cs](../../../tests/Gsa.Missions.Tests/M3MvpClosureAndGateTests.cs) (`Gsa.Missions.Tests`).
  - `ReplayCoreQualityReportAndRunM3GateCliCommandsExecuteEndToEnd` in [M3ReplayAndGateCliTests.cs](../../../tests/Gsa.Tools.Tests/M3ReplayAndGateCliTests.cs) (`Gsa.Tools.Tests`).

