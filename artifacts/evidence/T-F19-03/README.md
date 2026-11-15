# Evidence — T-F19-03 (Dual-Cohort MVP Gate & M3 Closure CLI)

- **Estado:** Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #12](https://github.com/kauevestena/gsa/pull/12) (SHA testado `45fb52b6a9036d64dadaab7ee27345b609ad51a3`, merge `b2fa25f`, execução [43682314395](https://github.com/kauevestena/gsa/actions/runs/43682314395), jobs Linux `132715376978` e Windows `132715377182`); ver [resumo CI](../M3-wave3-ci/summary.json).
- **Code & deliverables:**
  - [M3PlaytestAndGateValidator.cs](../../../src/Gsa.Missions/Diagnostics/M3PlaytestAndGateValidator.cs): `M3PlaytestAndGateValidator` combines automated technical verification (`HIST-01`..`HIST-03` campaign, 500/500 procedural seeds, low-balance calm guarantee, 30/60/120 fps replay invariance, bilingual parity audit, collectible idempotency, and all 16 M3 tasks) with dual Gamer/Student cohort playtest evaluation (`>=4/5` complete/correct, `>=3/5` wish to continue, student cohort linking `>=2` technical effects). Reports `playtest_gate_status: pending_human_playtest_cohort` when no external human cohort sessions are supplied.
  - [M3GateTool.cs](../../../src/Gsa.Tools/Campaign/M3GateTool.cs), [ToolApplication.cs](../../../src/Gsa.Tools/ToolApplication.cs), & [check_bootstrap.py](../../../scripts/check_bootstrap.py): wired `run-m3-gate` into CLI and bootstrap verification.
  - Generated report: [m3-gate-report.json](m3-gate-report.json) (`technical_loop_status: passed`, `missions_passed: 3`, `procedural_seeds_passed: 500`, `tasks_linked: 16`, `playtest_gate_status: pending_human_playtest_cohort`).
- **Tests:**
  - `MvpCampaignRunsHist01To03ProceduralGeneratorPasses500SeedsAndM3DualGateEvaluatesCohorts` in [M3MvpClosureAndGateTests.cs](../../../tests/Gsa.Missions.Tests/M3MvpClosureAndGateTests.cs) (`Gsa.Missions.Tests`).
  - `ReplayCoreQualityReportAndRunM3GateCliCommandsExecuteEndToEnd` in [M3ReplayAndGateCliTests.cs](../../../tests/Gsa.Tools.Tests/M3ReplayAndGateCliTests.cs) (`Gsa.Tools.Tests`).

