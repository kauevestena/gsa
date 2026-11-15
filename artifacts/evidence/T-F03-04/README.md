# Evidence — T-F03-04 (Scooter Profile, Damage & Safe Accident Recovery)

- **Estado:** Implementada e verificada localmente (`dotnet test Gsa.Domain.slnf --no-restore`, `python3 specs/validate.py`, `python3 scripts/verify.py --exports host`); em revisão técnica delegada à CI no branch `codex/m3-wave2-mobility-weather-calm-cameras`.
- **Code & deliverables:**
  - [LocomotionSimulator.cs](../../../src/Gsa.Core/Mobility/LocomotionSimulator.cs) & [BicycleAndScooterMobility.cs](../../../src/Gsa.Core/Mobility/BicycleAndScooterMobility.cs): Scooter profile (`12.0 m/s`, `800` storage, `3000 s` autonomy), road-lane rules, impact damage tracking (`RackShockDeltaVThresholdMetresPerSecond = 3.0`, `DestroyedDamageThresholdPoints = 100.0`), and `RepositionSafeAfterRecovery` / `ResetAfterSafeRecovery`.
  - [VehicleAccidentRecoveryCoordinator.cs](../../../src/Gsa.Missions/Persistence/VehicleAccidentRecoveryCoordinator.cs): `VehicleAccidentRecoveryCoordinator.RecoverFromAccident` integrates `BatchCustodyStore.HandleVehicleLoss` and `ContractStateMachine.HandleFieldIncident(..., FieldIncidentKind.VehicleDestroyed)` so volatile unstored pending batches are discarded while committed/protected batches survive across save/reload.
- **Tests:** [BicycleAndScooterMobilityTests.cs](../../../tests/Gsa.Core.Tests/BicycleAndScooterMobilityTests.cs) (`Gsa.Core.Tests`) & [VehicleAccidentRecoveryTests.cs](../../../tests/Gsa.Missions.Tests/VehicleAccidentRecoveryTests.cs) (`Gsa.Missions.Tests`).

