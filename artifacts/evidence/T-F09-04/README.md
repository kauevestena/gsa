# Evidence — T-F09-04 (Calm Contracts & Persistent Calm City Protection)

- **Estado:** Implementada e verificada localmente (`dotnet test Gsa.Domain.slnf --no-restore`, `python3 specs/validate.py`, `python3 scripts/verify.py --exports host`); em revisão técnica delegada à CI no branch `codex/m3-wave2-mobility-weather-calm-cameras`.
- **Code & deliverables:**
  - [UrbanHazardsAndCalmPolicy.cs](../../../src/Gsa.Core/Traffic/UrbanHazardsAndCalmPolicy.cs): `CalmPolicyCoordinator` unifies Calm City toggle and calm-contract hazard filtering, cancels pending hostile events (`ClearAllActiveAndPendingHazards`), suppresses active dog pursuits (`SuppressByCalmPolicy`), and exposes `IsProtectionActive` / `ProtectionIndicatorKey` (`hud.calm_protection.active` / `hud.calm_protection.inactive`) without penalizing economic progression.
- **Tests:** [CalendarWeatherAndHazardsTests.cs](../../../tests/Gsa.Core.Tests/CalendarWeatherAndHazardsTests.cs) (`Gsa.Core.Tests`).

