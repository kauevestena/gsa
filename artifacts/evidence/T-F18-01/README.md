# Evidence — T-F18-01 (Golden Eggs, Survey Landmarks & Projection Cards)

- **Estado:** Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #12](https://github.com/kauevestena/gsa/pull/12) (SHA testado `45fb52b6a9036d64dadaab7ee27345b609ad51a3`, merge `b2fa25f`, execução [43682314395](https://github.com/kauevestena/gsa/actions/runs/43682314395), jobs Linux `132715376978` e Windows `132715377182`); ver [resumo CI](../M3-wave3-ci/summary.json).
- **Code & deliverables:**
  - [MvpCollectiblesCoordinator.cs](../../../src/Gsa.Missions/Activities/MvpCollectiblesCoordinator.cs): `MvpCollectiblesCoordinator` manages daily golden egg spots (`GoldenEggSpot`), the 8-landmark city catalog with 2 MVP-active landmarks (`SurveyLandmarkDefinition`), 6 projection cards (`ProjectionCardDefinition`), save/reload snapshot state (`CollectibleStateSnapshot`), idempotent rewards via `CareerLedger.RecordActivityReward`, Geopédia article unlocks, and persisted blocked-area spawns.
  - [LedgerAndWallet.cs](../../../src/Gsa.Missions/Economy/LedgerAndWallet.cs): added `RecordActivityReward` for idempotent collectible payouts without bypassing contract accounting.
- **Tests:** `CollectiblesRewardOnceAcrossSaveReloadPreserveBlockedAreasAndCatalogEightLandmarks` in [M3MvpClosureAndGateTests.cs](../../../tests/Gsa.Missions.Tests/M3MvpClosureAndGateTests.cs) (`Gsa.Missions.Tests`).

