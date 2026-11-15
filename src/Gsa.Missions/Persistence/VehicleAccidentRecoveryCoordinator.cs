using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Missions.Contracts;

namespace Gsa.Missions.Persistence;

public sealed record AccidentRecoveryOutcome(
    bool Succeeded,
    Vector3d SafeRespawnPositionEnu,
    LocomotionMode RecoveredMode,
    int PendingBatchesLostCount,
    IReadOnlyList<string> PreservedProtectedBatchIds,
    ContractCommandResult? ContractIncidentResult,
    string SummaryKey);

/// <summary>Executes safe accident recovery: discards volatile pending batches while preserving protected custody and repositions at office.</summary>
public static class VehicleAccidentRecoveryCoordinator
{
    public static AccidentRecoveryOutcome RecoverFromAccident(
        double timestampSeconds,
        MvpVehicleMobilityController mobility,
        Vector3d safeOfficePositionEnu,
        BatchCustodyStore custodyStore,
        ContractStateMachine? activeContract = null)
    {
        if (!double.IsFinite(timestampSeconds) || timestampSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(timestampSeconds));
        ArgumentNullException.ThrowIfNull(mobility);
        ArgumentNullException.ThrowIfNull(custodyStore);

        var lostCount = custodyStore.HandleVehicleLoss(timestampSeconds);
        ContractCommandResult? contractIncident = null;
        if (activeContract is not null)
            contractIncident = activeContract.HandleFieldIncident(timestampSeconds, FieldIncidentKind.VehicleDestroyed);

        var recoveredMode = mobility.Simulator.MountedVehicleMode;
        mobility.ResetAfterSafeRecovery(safeOfficePositionEnu, recoveredMode);

        var preservedProtectedIds = custodyStore.AuthoritativeManifest.CommittedBatches
            .Select(c => c.BatchId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        return new AccidentRecoveryOutcome(
            Succeeded: true,
            SafeRespawnPositionEnu: safeOfficePositionEnu,
            RecoveredMode: recoveredMode,
            PendingBatchesLostCount: lostCount,
            PreservedProtectedBatchIds: preservedProtectedIds,
            ContractIncidentResult: contractIncident,
            SummaryKey: "mobility.recovery.safe_office_reposition");
    }
}

