# Evidence — T-F09-04 (Calm Contracts & Persistent Calm City Protection)

- **Estado:** Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #11](https://github.com/kauevestena/gsa/pull/11) (SHA testado `fe78e056a897bccac46ad30d21f82206625f33de`, merge `10697f3`, execução [43680847031](https://github.com/kauevestena/gsa/actions/runs/43680847031), jobs Linux `132710912429` e Windows `132710912254`); ver [resumo CI](../M3-wave2-ci/summary.json).
- **Code & deliverables:**
  - [UrbanHazardsAndCalmPolicy.cs](../../../src/Gsa.Core/Traffic/UrbanHazardsAndCalmPolicy.cs): `CalmPolicyCoordinator` unifies Calm City toggle and calm-contract hazard filtering, cancels pending hostile events (`ClearAllActiveAndPendingHazards`), suppresses active dog pursuits (`SuppressByCalmPolicy`), and exposes `IsProtectionActive` / `ProtectionIndicatorKey` (`hud.calm_protection.active` / `hud.calm_protection.inactive`) without penalizing economic progression.
- **Tests:** [CalendarWeatherAndHazardsTests.cs](../../../tests/Gsa.Core.Tests/CalendarWeatherAndHazardsTests.cs) (`Gsa.Core.Tests`).

