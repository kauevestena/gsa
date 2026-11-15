# Evidence — T-F11-04 (Geopédia & MVP Bilingual Catalog Review)

- **Estado:** Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #12](https://github.com/kauevestena/gsa/pull/12) (SHA testado `45fb52b6a9036d64dadaab7ee27345b609ad51a3`, merge `b2fa25f`, execução [43682314395](https://github.com/kauevestena/gsa/actions/runs/43682314395), jobs Linux `132715376978` e Windows `132715377182`); ver [resumo CI](../M3-wave3-ci/summary.json).
- **Code & deliverables:**
  - [GeopediaAndMvpLocalizationCatalog.cs](../../../src/Gsa.Missions/Presentation/GeopediaAndMvpLocalizationCatalog.cs): `GeopediaCatalogCoordinator` delivers the 16-entry MVP Geopédia catalog (`MvpEntries`), milestone/concept unlocks (`UnlockForMilestone`, `UnlockByConceptId`), bilingual `en_US.po` / `pt_BR.po` + `glossary.csv` parity audit (`AuditBilingualCatalog`), and 150% UI scale layout validation (`EvaluateLongScreenLayout`).
  - [glossary.csv](../../../content/localization/glossary.csv): added the 5 MVP technical terms (`overlap`, `exposure_blur`, `control_vs_check`, `dgnss`, `projection`) to the bilingual glossary.
  - [en_US.po](../../../game/localization/en_US.po) & [pt_BR.po](../../../game/localization/pt_BR.po): completed bilingual entries for `geopedia.*`, `collectible.*`, `proc.*`, and `replay.*`.
- **Tests:** `GeopediaUnlocksAuditBilingualParityAnd150PercentScaleFit` in [M3MvpClosureAndGateTests.cs](../../../tests/Gsa.Missions.Tests/M3MvpClosureAndGateTests.cs) (`Gsa.Missions.Tests`).

