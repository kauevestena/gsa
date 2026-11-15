# Evidence — T-F08-02 (Fleet Shop, Reputation Discounts, XP Skill Tracks & Starter Recovery)

- **Estado:** Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #10](https://github.com/kauevestena/gsa/pull/10) (SHA testado `bf2ddd590bcebe58739160b63dbb9c6a27933c74`, merge `d8d7dc0`, execução [43679717318](https://github.com/kauevestena/gsa/actions/runs/43679717318), jobs Linux `132707485195` e Windows `132707484916`); ver [resumo CI](../M3-wave1-ci/summary.json).
- **Code:**
  - [PlatformProfile.cs](../../../src/Gsa.Core/Mobility/PlatformProfile.cs): Added `LocomotionMode.Bicycle` (`Level=1`, `6 m/s`, `400` virtual storage, `2400 s`, `400 Coords`), `LocomotionMode.Scooter` (`Level=2`, `12 m/s`, `800` virtual storage, `3000 s`, `1200 Coords`), and `ForMode`.
  - [FleetShopAndProgression.cs](../../../src/Gsa.Missions/Economy/FleetShopAndProgression.cs): `FleetShopCatalog` & `FleetProgressionManager` implement story prerequisite gates (`HIST-02` Bicycle, `HIST-03` Scooter), global reputation discount `min(15%, rep / 1000)`, LiDAR upgrade gate below Level 4 (`shop.error.lidar_requires_level_4`), 6 `SkillTrackKind` tracks up to Rank 3 (`500 XP` per point), processing/calibration reduction helpers (`10%/rank`), and free starter Handcart recovery with low-balance (`< 50 Coords`) subsidy signaling.
- **Tests:** [FleetShopAndProgressionTests.cs](../../../tests/Gsa.Missions.Tests/FleetShopAndProgressionTests.cs) (`Gsa.Missions.Tests`).

