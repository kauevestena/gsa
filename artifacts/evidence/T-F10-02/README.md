# Evidence — T-F10-02 (MVP HIST-01..03 Playthrough & Five-Template Procedural Generator)

- **Estado:** Implementada; em revisão delegada à CI no branch `codex/m3-wave3-mvp-closure-and-gate`.
- **Code & deliverables:**
  - [MvpCampaignAndProceduralGenerator.cs](../../../src/Gsa.Missions/Campaign/MvpCampaignAndProceduralGenerator.cs): `MvpCampaignRunner` (`RunMvpThreeMissionPlaythrough`, `MvpThreeMissionPlaythroughReport`) runs the complete `HIST-01` -> `HIST-02` -> `HIST-03` campaign with save/reload between missions, idempotent cutscene skipping, announced rain handling, and disjoint control vs. checkpoint targets; `MvpProceduralContractGenerator` generates deterministic contracts across 5 templates (`PROC-FACHADAS`, `PROC-DRENAGEM`, `PROC-CALCADAS`, `PROC-PLACAS`, `PROC-PARQUE`), validates 100 seeds/template (`ValidateOneHundredSeedsPerTemplate`), emits `RouteWitnessCertificate`, and guarantees a calm starter-compatible offer at any balance/debt (`GenerateAvailableBoard`).
  - [catalog.json](../../../content/catalog.json) & [old-center.json](../../../content/regions/old-center.json): added MVP procedural clients (`map_company`, `insurance_co`) and `central_park_calm` calm park area/surface/benches.
- **Tests:** `MvpCampaignRunsHist01To03ProceduralGeneratorPasses500SeedsAndM3DualGateEvaluatesCohorts` in [M3MvpClosureAndGateTests.cs](../../../tests/Gsa.Missions.Tests/M3MvpClosureAndGateTests.cs) (`Gsa.Missions.Tests`).

