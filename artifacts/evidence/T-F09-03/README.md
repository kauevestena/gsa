# Evidence — T-F09-03 (Erratic Drivers, Blitz Removal & Stray Dog Pursuits)

- **Estado:** Implementada e verificada localmente (`dotnet test Gsa.Domain.slnf --no-restore`, `python3 specs/validate.py`, `python3 scripts/verify.py --exports host`); em revisão técnica delegada à CI no branch `codex/m3-wave2-mobility-weather-calm-cameras`.
- **Code & deliverables:**
  - [UrbanHazardsAndCalmPolicy.cs](../../../src/Gsa.Core/Traffic/UrbanHazardsAndCalmPolicy.cs): `ErraticDriverSimulator` computes context spawn probabilities (`2%` daytime, `8%` nighttime, `12%` weekend near bars, capped at `12%`), periodic `±0.2 rad` steering perturbations, `±30%` speed shifts, `20%` intersection infraction checks, zigzag/horn/headlight cues, and blitz zone removal; `DogPursuitTracker` models `<= 30 m` trigger, `10 s` cap, `> 40 m` disengagement, and treat pacification.
- **Tests:** [CalendarWeatherAndHazardsTests.cs](../../../tests/Gsa.Core.Tests/CalendarWeatherAndHazardsTests.cs) (`Gsa.Core.Tests`).

