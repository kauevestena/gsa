# Evidence — T-F11-04 (Geopédia & MVP Bilingual Catalog Review)

- **Estado:** Implementada; em revisão delegada à CI no branch `codex/m3-wave3-mvp-closure-and-gate`.
- **Code & deliverables:**
  - [GeopediaAndMvpLocalizationCatalog.cs](../../../src/Gsa.Missions/Presentation/GeopediaAndMvpLocalizationCatalog.cs): `GeopediaCatalogCoordinator` delivers the 16-entry MVP Geopédia catalog (`MvpEntries`), milestone/concept unlocks (`UnlockForMilestone`, `UnlockByConceptId`), bilingual `en_US.po` / `pt_BR.po` + `glossary.csv` parity audit (`AuditBilingualCatalog`), and 150% UI scale layout validation (`EvaluateLongScreenLayout`).
  - [glossary.csv](../../../content/localization/glossary.csv): added the 5 MVP technical terms (`overlap`, `exposure_blur`, `control_vs_check`, `dgnss`, `projection`) to the bilingual glossary.
  - [en_US.po](../../../game/localization/en_US.po) & [pt_BR.po](../../../game/localization/pt_BR.po): completed bilingual entries for `geopedia.*`, `collectible.*`, `proc.*`, and `replay.*`.
- **Tests:** `GeopediaUnlocksAuditBilingualParityAnd150PercentScaleFit` in [M3MvpClosureAndGateTests.cs](../../../tests/Gsa.Missions.Tests/M3MvpClosureAndGateTests.cs) (`Gsa.Missions.Tests`).

