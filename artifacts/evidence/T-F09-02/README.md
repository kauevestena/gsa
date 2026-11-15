# Evidence — T-F09-02 (Accelerated World Calendar, Weather & Grip)

- **Estado:** Implementada e verificada localmente (`dotnet test Gsa.Domain.slnf --no-restore`, `python3 specs/validate.py`, `python3 scripts/verify.py --exports host`); em revisão técnica delegada à CI no branch `codex/m3-wave2-mobility-weather-calm-cameras`.
- **Code & deliverables:**
  - [WorldCalendarAndWeather.cs](../../../src/Gsa.Core/Traffic/WorldCalendarAndWeather.cs): `WorldCalendarAndWeatherController` implements accelerated time (`1` physical s = `60` calendar s, initial `08:00`), pause freezing clocks, time-advance deadline callbacks, day/night/rush-hour/weekend states, surface adhesion (`1.0` dry / `0.6` rain), and `WeatherAndTimeSnapshot` + `ToCameraEnvironment(...)` export.
- **Tests:** [CalendarWeatherAndHazardsTests.cs](../../../tests/Gsa.Core.Tests/CalendarWeatherAndHazardsTests.cs) (`Gsa.Core.Tests`).

