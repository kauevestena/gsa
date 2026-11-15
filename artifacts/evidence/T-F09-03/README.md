# Evidence — T-F09-03 (Erratic Drivers, Blitz Removal & Stray Dog Pursuits)

- **Estado:** Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #11](https://github.com/kauevestena/gsa/pull/11) (SHA testado `fe78e056a897bccac46ad30d21f82206625f33de`, merge `10697f3`, execução [43680847031](https://github.com/kauevestena/gsa/actions/runs/43680847031), jobs Linux `132710912429` e Windows `132710912254`); ver [resumo CI](../M3-wave2-ci/summary.json).
- **Code & deliverables:**
  - [UrbanHazardsAndCalmPolicy.cs](../../../src/Gsa.Core/Traffic/UrbanHazardsAndCalmPolicy.cs): `ErraticDriverSimulator` computes context spawn probabilities (`2%` daytime, `8%` nighttime, `12%` weekend near bars, capped at `12%`), periodic `±0.2 rad` steering perturbations, `±30%` speed shifts, `20%` intersection infraction checks, zigzag/horn/headlight cues, and blitz zone removal; `DogPursuitTracker` models `<= 30 m` trigger, `10 s` cap, `> 40 m` disengagement, and treat pacification.
- **Tests:** [CalendarWeatherAndHazardsTests.cs](../../../tests/Gsa.Core.Tests/CalendarWeatherAndHazardsTests.cs) (`Gsa.Core.Tests`).

