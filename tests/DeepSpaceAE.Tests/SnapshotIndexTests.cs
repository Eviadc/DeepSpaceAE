using System;
using System.Collections.Generic;
using DeepSpaceAE.Core;
using Xunit;

namespace DeepSpaceAE.Tests;

public sealed class SnapshotIndexTests
{
    [Fact]
    public void SameItemIdWithDifferentStateRemainsTwoDistinctInstances()
    {
        var intact = new ItemInstance(Guid.NewGuid(), "battery", "charge=100");
        var depleted = new ItemInstance(Guid.NewGuid(), "battery", "charge=0");
        var index = new SnapshotIndex(new[] {
            new ContainerSnapshot("a", new[] { intact }),
            new ContainerSnapshot("b", new[] { depleted }) });

        Assert.Equal(2, index.ByInstance.Count);
        Assert.Same(intact, index.ByInstance[intact.InstanceId].Item);
        Assert.Same(depleted, index.ByInstance[depleted.InstanceId].Item);
        Assert.Equal("a", index.ByInstance[intact.InstanceId].ContainerId);
        Assert.Equal("b", index.ByInstance[depleted.InstanceId].ContainerId);
    }

    [Fact]
    public void SameItemIdAndStateStillDoesNotImplySameInstance()
    {
        var first = new ItemInstance(Guid.NewGuid(), "battery", "charge=100");
        var second = new ItemInstance(Guid.NewGuid(), "battery", "charge=100");
        var index = new SnapshotIndex(new[] { new ContainerSnapshot("a", new[] { first, second }) });
        Assert.Equal(2, index.ByInstance.Count);
    }

    [Fact]
    public void AmbiguousInstanceInTwoContainersIsRejected()
    {
        var item = new ItemInstance(Guid.NewGuid(), "battery", "charge=100");
        Assert.Throws<ArgumentException>(() => new SnapshotIndex(new[] {
            new ContainerSnapshot("a", new[] { item }),
            new ContainerSnapshot("b", new[] { item }) }));
    }

    [Fact]
    public void DuplicateContainerIdentityIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new SnapshotIndex(new[] {
            new ContainerSnapshot("a", Array.Empty<ItemInstance>()),
            new ContainerSnapshot("a", Array.Empty<ItemInstance>()) }));
    }

    [Fact]
    public void SnapshotDoesNotTrackLaterListChanges()
    {
        var item = new ItemInstance(Guid.NewGuid(), "battery", "charge=100");
        var mutable = new List<ItemInstance> { item };
        var snapshot = new ContainerSnapshot("a", mutable);
        mutable.Clear();
        Assert.Same(item, Assert.Single(snapshot.Items));
    }

    [Fact]
    public void EmptyInstanceIdentityIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ItemInstance(Guid.Empty, "battery", "charge=100"));
    }
}
