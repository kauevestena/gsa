# Evidence — T-F18-01 (Golden Eggs, Survey Landmarks & Projection Cards)

- **Estado:** Implementada; em revisão delegada à CI no branch `codex/m3-wave3-mvp-closure-and-gate`.
- **Code & deliverables:**
  - [MvpCollectiblesCoordinator.cs](../../../src/Gsa.Missions/Activities/MvpCollectiblesCoordinator.cs): `MvpCollectiblesCoordinator` manages daily golden egg spots (`GoldenEggSpot`), the 8-landmark city catalog with 2 MVP-active landmarks (`SurveyLandmarkDefinition`), 6 projection cards (`ProjectionCardDefinition`), save/reload snapshot state (`CollectibleStateSnapshot`), idempotent rewards via `CareerLedger.RecordActivityReward`, Geopédia article unlocks, and persisted blocked-area spawns.
  - [LedgerAndWallet.cs](../../../src/Gsa.Missions/Economy/LedgerAndWallet.cs): added `RecordActivityReward` for idempotent collectible payouts without bypassing contract accounting.
- **Tests:** `CollectiblesRewardOnceAcrossSaveReloadPreserveBlockedAreasAndCatalogEightLandmarks` in [M3MvpClosureAndGateTests.cs](../../../tests/Gsa.Missions.Tests/M3MvpClosureAndGateTests.cs) (`Gsa.Missions.Tests`).

