# Evidence — T-F19-03 (Dual-Cohort MVP Gate & M3 Closure CLI)

- **Estado:** Implementada; em revisão delegada à CI no branch `codex/m3-wave3-mvp-closure-and-gate`.
- **Code & deliverables:**
  - [M3PlaytestAndGateValidator.cs](../../../src/Gsa.Missions/Diagnostics/M3PlaytestAndGateValidator.cs): `M3PlaytestAndGateValidator` combines automated technical verification (`HIST-01`..`HIST-03` campaign, 500/500 procedural seeds, low-balance calm guarantee, 30/60/120 fps replay invariance, bilingual parity audit, collectible idempotency, and all 16 M3 tasks) with dual Gamer/Student cohort playtest evaluation (`>=4/5` complete/correct, `>=3/5` wish to continue, student cohort linking `>=2` technical effects). Reports `playtest_gate_status: pending_human_playtest_cohort` when no external human cohort sessions are supplied.
  - [M3GateTool.cs](../../../src/Gsa.Tools/Campaign/M3GateTool.cs), [ToolApplication.cs](../../../src/Gsa.Tools/ToolApplication.cs), & [check_bootstrap.py](../../../scripts/check_bootstrap.py): wired `run-m3-gate` into CLI and bootstrap verification.
  - Generated report: [m3-gate-report.json](m3-gate-report.json) (`technical_loop_status: passed`, `missions_passed: 3`, `procedural_seeds_passed: 500`, `tasks_linked: 16`, `playtest_gate_status: pending_human_playtest_cohort`).
- **Tests:**
  - `MvpCampaignRunsHist01To03ProceduralGeneratorPasses500SeedsAndM3DualGateEvaluatesCohorts` in [M3MvpClosureAndGateTests.cs](../../../tests/Gsa.Missions.Tests/M3MvpClosureAndGateTests.cs) (`Gsa.Missions.Tests`).
  - `ReplayCoreQualityReportAndRunM3GateCliCommandsExecuteEndToEnd` in [M3ReplayAndGateCliTests.cs](../../../tests/Gsa.Tools.Tests/M3ReplayAndGateCliTests.cs) (`Gsa.Tools.Tests`).

