# Evidence — T-F05-04 (Adicionar RTK e instalação de base)

## Escopo
Implementação de instalação de estação-base em marco geodésico (`BaseStationInstaller` exigindo modo `OnFoot`, distância `<= 2.0 m`, abertura celeste `>= 0.8` e ocupação de `5.0 s`), registro e preservação de logs históricos após desativação/bateria (`InstalledBaseStationState`), máquina de estados RTK (`RtkSolutionStateMachine`: fallback para `Autonomous` abaixo de abertura `0.4` ou além de `2 km`, `RtkFloat` `0.15 m`, `RtkFix` `0.03 m` após `10 s` com abertura `>= 0.7`), snapshot save/load com determinismo RNG/PPK e indicadores localizados no HUD (`hud.gnss.dgnss`, `hud.gnss.rtk_float`, `hud.gnss.rtk_fix`).

## Arquivos de Implementação e Testes
- [GnssObservation.cs](../../../src/Gsa.Core/Sensors/Gnss/GnssObservation.cs)
- [GnssProfile.cs](../../../src/Gsa.Core/Sensors/Gnss/GnssProfile.cs)
- [GnssSkyIntegration.cs](../../../src/Gsa.Core/Sensors/Gnss/GnssSkyIntegration.cs)
- [RtkAndBaseInstallation.cs](../../../src/Gsa.Core/Sensors/Gnss/RtkAndBaseInstallation.cs)
- [HudAndMapPresenter.cs](../../../src/Gsa.Missions/Presentation/HudAndMapPresenter.cs)
- [en_US.po](../../../game/localization/en_US.po) & [pt_BR.po](../../../game/localization/pt_BR.po)
- [M4WorldCar360AndRtkTests.cs](../../../tests/Gsa.Core.Tests/M4WorldCar360AndRtkTests.cs) (`T_F05_04_BaseStationSetupPreservesPastLogsAndRtkEnforcesThresholdsBaselineAndSnapshotRoundTrip`)
- [M4EconomyServicesAndCosmeticsTests.cs](../../../tests/Gsa.Missions.Tests/M4EconomyServicesAndCosmeticsTests.cs)

## Execução e Verificação
- `dotnet test Gsa.Domain.slnf --no-restore`: `Gsa.Core.Tests` e `Gsa.Missions.Tests` verificaram regras de instalação, transições float/fix/fallback, preservação de logs para PPK, round-trip determinístico e chaves localizadas do HUD.

## Estado
Implementada; em revisão delegada à CI.

