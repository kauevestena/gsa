# Evidence — T-F02-03 (Old Center MVP 500x500 m & Sector Streaming)

- **Estado:** Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #10](https://github.com/kauevestena/gsa/pull/10) (SHA testado `bf2ddd590bcebe58739160b63dbb9c6a27933c74`, merge `d8d7dc0`, execução [43679717318](https://github.com/kauevestena/gsa/actions/runs/43679717318), jobs Linux `132707485195` e Windows `132707484916`); ver [resumo CI](../M3-wave1-ci/summary.json).
- **Artifacts & code:**
  - [old-center.json](../../../content/regions/old-center.json): Added `commerce_street_signs` area, `commerce_sign_planes` surface, and `sign_01`..`sign_12` (`traffic_sign`) alongside the 20 facades, 12 manholes, and 8 sidewalk segments.
  - [OldCenterMvpSectorStreaming.cs](../../../src/Gsa.Core/World/OldCenterMvpSectorStreaming.cs): `OldCenterMvpWorldAssembler` builds the 500x500 m (`25` x 100x100 m sector) MVP scene while preserving all tutorial IDs (`facade_01..20`, `office_entry`, `unload_point`, survey control marks) plus park/wifi/calm zones and north unload point; `OldCenterSectorStreamer` enforces the 120 m capture safety band (`world.streaming.safety_band_unloaded`) and campaign-gated regional portals (`portal_porto` / `HIST-04`, `portal_orla` / `HIST-05`, `portal_serra` / `HIST-07`).
- **Tests:** [OldCenterMvpWorldTests.cs](../../../tests/Gsa.Core.Tests/OldCenterMvpWorldTests.cs) (`Gsa.Core.Tests`).

