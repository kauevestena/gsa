# Evidence — T-F09-02 (Accelerated World Calendar, Weather & Grip)

- **Estado:** Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #11](https://github.com/kauevestena/gsa/pull/11) (SHA testado `fe78e056a897bccac46ad30d21f82206625f33de`, merge `10697f3`, execução [43680847031](https://github.com/kauevestena/gsa/actions/runs/43680847031), jobs Linux `132710912429` e Windows `132710912254`); ver [resumo CI](../M3-wave2-ci/summary.json).
- **Code & deliverables:**
  - [WorldCalendarAndWeather.cs](../../../src/Gsa.Core/Traffic/WorldCalendarAndWeather.cs): `WorldCalendarAndWeatherController` implements accelerated time (`1` physical s = `60` calendar s, initial `08:00`), pause freezing clocks, time-advance deadline callbacks, day/night/rush-hour/weekend states, surface adhesion (`1.0` dry / `0.6` rain), and `WeatherAndTimeSnapshot` + `ToCameraEnvironment(...)` export.
- **Tests:** [CalendarWeatherAndHazardsTests.cs](../../../tests/Gsa.Core.Tests/CalendarWeatherAndHazardsTests.cs) (`Gsa.Core.Tests`).

