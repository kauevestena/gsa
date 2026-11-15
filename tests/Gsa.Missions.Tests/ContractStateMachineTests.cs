using System.Text.Json.Nodes;
using Gsa.Missions.Contracts;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class ContractStateMachineTests
{
    private static ContractDefinition LoadContract(string fixture = "facades.json") =>
        ContractDefinition.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)), fixture);

    [Fact]
    public void HappyPathCompletesFullLifecycleAndDeliversIdempotently()
    {
        var def = LoadContract("facades.json");
        var machine = new ContractStateMachine(def);
        Assert.Equal(ContractState.Disponivel, machine.State);

        var accepted = machine.Accept(100.0, new HashSet<string> { "camera", "gnss_basic" }, new HashSet<string>());
        Assert.True(accepted.Succeeded);
        Assert.Equal(ContractState.Aceito, machine.State);
        Assert.NotNull(machine.Snapshot);
        Assert.Equal(100.0 + def.Deadline.DueAfterSeconds, machine.Snapshot!.DueAtSeconds);
        Assert.Equal(100.0 + def.Deadline.ExpiresAfterSeconds, machine.Snapshot.ExpiresAtSeconds);

        // Delivery before completion is rejected explicitly.
        var prematureDeliver = machine.Deliver(110.0);
        Assert.False(prematureDeliver.Succeeded);
        Assert.Equal(ContractCommandStatus.RejectedInvalidState, prematureDeliver.Status);

        Assert.True(machine.RecordPendingBatch(120.0, "batch_01").Succeeded);
        Assert.Equal(ContractState.Coletando, machine.State);
        Assert.True(machine.ProtectBatchAtUnloadPoint(200.0, "batch_01").Succeeded);
        Assert.True(machine.MarkReadyToProcess(210.0).Succeeded);
        Assert.Equal(ContractState.ProntoProcessar, machine.State);

        Assert.True(machine.StartProcessing(220.0).Succeeded);
        Assert.Equal(ContractState.Processando, machine.State);

        Assert.True(machine.CompleteProcessing(820.0).Succeeded);
        Assert.Equal(ContractState.EmCQ, machine.State);

        Assert.True(machine.RecordQualityEvaluation(820.0, fullPassed: true).Succeeded);
        Assert.Equal(ContractState.ProntoEntregar, machine.State);

        var deliver1 = machine.Deliver(830.0, "delivery_tx_1");
        Assert.Equal(ContractCommandStatus.Executed, deliver1.Status);
        Assert.Equal(ContractState.Entregue, machine.State);

        var deliverDuplicate = machine.Deliver(835.0, "delivery_tx_1");
        Assert.Equal(ContractCommandStatus.IdempotentNoOp, deliverDuplicate.Status);
        Assert.Equal(ContractState.Entregue, machine.State);
    }

    [Fact]
    public void DestructionAndFleeDiscardPendingFieldBatchesWithoutErasingProtectedBatches()
    {
        var def = LoadContract("inventory.json");
        var machine = new ContractStateMachine(def);
        Assert.True(machine.Accept(0.0, new HashSet<string> { "camera", "gnss_basic" }, new HashSet<string>()).Succeeded);

        // Batch 1 is collected and offloaded to protected custody; Batch 2 stays pending in the field.
        Assert.True(machine.RecordPendingBatch(10.0, "b_safe").Succeeded);
        Assert.True(machine.ProtectBatchAtUnloadPoint(20.0, "b_safe").Succeeded);
        Assert.True(machine.RecordPendingBatch(30.0, "b_pending").Succeeded);

        var incident = machine.HandleFieldIncident(40.0, FieldIncidentKind.GimbalinaFled);
        Assert.True(incident.Succeeded);
        Assert.Equal(ContractState.Coletando, machine.State);
        Assert.Single(machine.ProtectedBatches);
        Assert.Equal("b_safe", machine.ProtectedBatches[0].BatchId);
        Assert.Empty(machine.PendingFieldBatches);
        Assert.Single(machine.DiscardedBatches);
        Assert.Equal("b_pending", machine.DiscardedBatches[0].BatchId);

        // Processing succeeds using protected data even after field loss.
        Assert.True(machine.MarkReadyToProcess(50.0).Succeeded);
        Assert.True(machine.StartProcessing(60.0).Succeeded);
        Assert.True(machine.CompleteProcessing(100.0).Succeeded);

        // Fail full CQ, accept block_a deliverable in Retrabalho, deliver partial.
        Assert.True(machine.RecordQualityEvaluation(100.0, fullPassed: false, ["block_a"], "cq.failure.feature_completeness").Succeeded);
        Assert.Equal(ContractState.Retrabalho, machine.State);
        Assert.True(machine.CanDeliverPartial);

        Assert.True(machine.RequestPartialDelivery(120.0).Succeeded);
        Assert.Equal(ContractState.ProntoEntregar, machine.State);
        Assert.True(machine.Deliver(130.0).Succeeded);
        Assert.Equal(ContractState.EntregueParcial, machine.State);
    }

    [Theory]
    [InlineData(ContractState.Aceito)]
    [InlineData(ContractState.Coletando)]
    [InlineData(ContractState.ProntoProcessar)]
    [InlineData(ContractState.Processando)]
    [InlineData(ContractState.EmCQ)]
    [InlineData(ContractState.Retrabalho)]
    [InlineData(ContractState.ProntoEntregar)]
    public void ExpirationAppliesAcrossEveryActiveStateAndBlocksFurtherProgress(ContractState targetActiveState)
    {
        var def = LoadContract("facades.json");
        var machine = new ContractStateMachine(def);
        Assert.True(machine.Accept(0.0).Succeeded);

        if (targetActiveState != ContractState.Aceito)
        {
            Assert.True(machine.RecordPendingBatch(10.0, "b1").Succeeded);
            Assert.True(machine.ProtectBatchAtUnloadPoint(20.0, "b1").Succeeded);
        }
        if (targetActiveState is ContractState.ProntoProcessar or ContractState.Processando or ContractState.EmCQ or ContractState.Retrabalho or ContractState.ProntoEntregar)
            Assert.True(machine.MarkReadyToProcess(30.0).Succeeded);
        if (targetActiveState is ContractState.Processando or ContractState.EmCQ or ContractState.Retrabalho or ContractState.ProntoEntregar)
            Assert.True(machine.StartProcessing(40.0).Succeeded);
        if (targetActiveState is ContractState.EmCQ or ContractState.Retrabalho or ContractState.ProntoEntregar)
            Assert.True(machine.CompleteProcessing(50.0).Succeeded);
        if (targetActiveState == ContractState.Retrabalho)
            Assert.True(machine.RecordQualityEvaluation(60.0, fullPassed: false, primaryFailureKey: "cq.failure.area_coverage").Succeeded);
        if (targetActiveState == ContractState.ProntoEntregar)
            Assert.True(machine.RecordQualityEvaluation(60.0, fullPassed: true).Succeeded);

        Assert.Equal(targetActiveState, machine.State);

        var expiredAt = def.Deadline.ExpiresAfterSeconds + 1.0;
        var tick = machine.AdvanceClock(expiredAt);
        Assert.True(tick.Succeeded);
        Assert.Equal(ContractState.Expirado, machine.State);
        Assert.Equal("ContractExpired", Assert.Single(tick.EmittedEvents).EventType);

        var rejected = machine.Abandon(expiredAt + 5.0);
        Assert.False(rejected.Succeeded);
        Assert.Equal(ContractCommandStatus.RejectedExpired, rejected.Status);
    }

    [Fact]
    public void SnapshotRemainsImmutableEvenAfterSourceJsonReloadChanges()
    {
        var rawJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "facades.json"));
        var originalDef = ContractDefinition.Parse(rawJson, "facades.json");
        var machine = new ContractStateMachine(originalDef);
        Assert.True(machine.Accept(0.0).Succeeded);

        // Simulate hot-reload modifying catalog threshold & deadline in memory/disk.
        var editedNode = JsonNode.Parse(rawJson)!.AsObject();
        editedNode["deadline"]!["due_after_s"] = 50.0;
        editedNode["deadline"]!["expires_after_s"] = 100.0;
        var reloadedDef = ContractDefinition.Parse(editedNode.ToJsonString(), "facades_reloaded.json");
        Assert.Equal(100.0, reloadedDef.Deadline.ExpiresAfterSeconds);

        // Active accepted contract still retains original snapshot.
        Assert.Equal(originalDef.Deadline.ExpiresAfterSeconds, machine.Snapshot!.ExpiresAtSeconds);
        Assert.NotEqual(ContractState.Expirado, machine.AdvanceClock(500.0).State);
    }

    [Fact]
    public void PreconditionMissingCapabilityAndAbandonAreHandledCleanly()
    {
        var def = LoadContract("facades.json");
        var blocked = new ContractStateMachine(def);
        var missingCap = blocked.Accept(0.0, new HashSet<string> { "gnss_basic" }, new HashSet<string>());
        Assert.False(missingCap.Succeeded);
        Assert.Equal(ContractCommandStatus.RejectedPrecondition, missingCap.Status);
        Assert.Equal(ContractState.Disponivel, blocked.State);

        var abandoned = new ContractStateMachine(def);
        Assert.True(abandoned.Accept(0.0).Succeeded);
        Assert.True(abandoned.RecordPendingBatch(5.0, "b1").Succeeded);
        var abandonResult = abandoned.Abandon(15.0);
        Assert.True(abandonResult.Succeeded);
        Assert.Equal(ContractState.Abandonado, abandoned.State);
        Assert.Empty(abandoned.PendingFieldBatches);
        Assert.Single(abandoned.DiscardedBatches);
    }
}
