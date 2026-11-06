using System;
using System.Linq;
using DeepSpaceAE.Core;
using DeepSpaceAE.Tests.Simulation;
using Xunit;

namespace DeepSpaceAE.Tests;

public sealed class SimulatedMoveTests
{
    private static ItemInstance Item(string state = "charge=100") => new(Guid.NewGuid(), "battery", state);

    [Fact]
    public void MovingOneInstanceDoesNotMergeOtherState()
    {
        var source = new InMemoryContainer("a", 2);
        var destination = new InMemoryContainer("b", 2);
        var charged = Item();
        var empty = Item("charge=0");
        source.Seed(charged);
        destination.Seed(empty);
        Assert.Equal(MoveOutcome.Moved, source.MoveOneTo(destination, charged.InstanceId));
        Assert.Equal(0, source.Count);
        Assert.Equal(2, destination.Count);
        var index = new SnapshotIndex(new[] { source.Snapshot(), destination.Snapshot() });
        Assert.Same(charged, index.ByInstance[charged.InstanceId].Item);
        Assert.Same(empty, index.ByInstance[empty.InstanceId].Item);
    }

    [Fact]
    public void FullDestinationLeavesItemInSource()
    {
        var source = new InMemoryContainer("a", 1);
        var destination = new InMemoryContainer("b", 0);
        var item = Item();
        source.Seed(item);
        Assert.Equal(MoveOutcome.Full, source.MoveOneTo(destination, item.InstanceId));
        Assert.True(source.Contains(item.InstanceId));
        Assert.Equal(0, destination.Count);
    }

    [Fact]
    public void RejectionLeavesItemInSource()
    {
        var source = new InMemoryContainer("a", 1);
        var destination = new InMemoryContainer("b", 1, _ => false);
        var item = Item();
        source.Seed(item);
        Assert.Equal(MoveOutcome.Rejected, source.MoveOneTo(destination, item.InstanceId));
        Assert.True(source.Contains(item.InstanceId));
        Assert.Equal(0, destination.Count);
    }

    [Fact]
    public void ReplayedRequestReturnsOriginalOutcomeWithoutMovingTwice()
    {
        var source = new InMemoryContainer("a", 1);
        var destination = new InMemoryContainer("b", 1);
        var item = Item();
        source.Seed(item);
        var session = new InMemoryMoveSession();
        var request = Guid.NewGuid();
        var first = session.Execute(request, source, destination, item.InstanceId);
        var replay = session.Execute(request, source, destination, item.InstanceId);
        Assert.Equal(MoveOutcome.Moved, Assert.Single(first));
        Assert.Same(first, replay);
        Assert.Equal(0, source.Count);
        Assert.Same(item, Assert.Single(destination.Snapshot().Items));
    }

    [Fact]
    public void ReusingRequestIdWithDifferentPayloadIsRejected()
    {
        var source = new InMemoryContainer("a", 2);
        var destination = new InMemoryContainer("b", 2);
        var first = Item();
        var second = Item("charge=0");
        source.Seed(first);
        source.Seed(second);
        var session = new InMemoryMoveSession();
        var request = Guid.NewGuid();
        session.Execute(request, source, destination, first.InstanceId);
        Assert.Throws<InvalidOperationException>(() => session.Execute(request, source, destination, second.InstanceId));
        Assert.True(source.Contains(second.InstanceId));
        Assert.Equal(1, destination.Count);
    }

    [Fact]
    public void PartialFailureReportsEachItemWithoutRollingBackEarlierSuccess()
    {
        var source = new InMemoryContainer("a", 3);
        var destination = new InMemoryContainer("b", 3);
        var first = Item();
        var failed = Item("charge=0");
        var last = Item("charge=50");
        foreach (var item in new[] { first, failed, last }) source.Seed(item);
        destination.FailBeforeInsert = failed.InstanceId;
        var session = new InMemoryMoveSession();
        var request = Guid.NewGuid();
        var payload = new[] { first.InstanceId, failed.InstanceId, last.InstanceId };
        var result = session.Execute(request, source, destination, payload);

        Assert.Equal(new[] { MoveOutcome.Moved, MoveOutcome.InjectedFailure, MoveOutcome.Moved }, result);
        Assert.Same(failed, Assert.Single(source.Snapshot().Items));
        Assert.True(destination.Contains(first.InstanceId));
        Assert.True(destination.Contains(last.InstanceId));
        var all = source.Snapshot().Items.Concat(destination.Snapshot().Items).ToArray();
        Assert.Equal(3, all.Length);
        Assert.Equal(3, all.Select(item => item.InstanceId).Distinct().Count());

        destination.FailBeforeInsert = null;
        Assert.Same(result, session.Execute(request, source, destination, payload));
        Assert.True(source.Contains(failed.InstanceId)); // Same request is not an implicit retry.
        Assert.Equal(MoveOutcome.Moved, Assert.Single(session.Execute(Guid.NewGuid(), source, destination, failed.InstanceId)));
    }

    [Fact]
    public void RepeatedItemInBatchDoesNotDuplicateIt()
    {
        var source = new InMemoryContainer("a", 1);
        var destination = new InMemoryContainer("b", 2);
        var item = Item();
        source.Seed(item);
        var results = new InMemoryMoveSession().Execute(Guid.NewGuid(), source, destination, item.InstanceId, item.InstanceId);
        Assert.Equal(new[] { MoveOutcome.Moved, MoveOutcome.Missing }, results);
        Assert.Equal(0, source.Count);
        Assert.Equal(1, destination.Count);
    }
}
