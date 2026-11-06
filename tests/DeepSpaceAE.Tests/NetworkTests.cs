using System;
using System.IO;
using System.Linq;
using DeepSpaceAE.Core;
using DeepSpaceAE.Core.Network;
using DeepSpaceAE.Tests.Simulation;
using Xunit;

namespace DeepSpaceAE.Tests;

public sealed class NetworkTests
{
    [Fact]
    public void TerminalReadsOnlyConnectedBusContainersWithoutMergingStates()
    {
        var view = new NetworkPrototype().Read();
        Assert.Equal(DeviceStatus.Online, view.Connection.Status);
        Assert.Equal(3, view.Index.ByInstance.Count);
        Assert.Equal("charge=100", view.Index.ByInstance[NetworkPrototype.ChargedId].Item.StateTag);
        Assert.Equal("charge=0", view.Index.ByInstance[NetworkPrototype.EmptyId].Item.StateTag);
        Assert.False(view.Index.ByInstance.ContainsKey(NetworkPrototype.HiddenId));
        Assert.Equal(3, view.Network.ChannelsUsed["core"]);
        Assert.Equal(3, view.Network.ChannelsUsed["trunk"]);
    }

    [Fact]
    public void DisconnectingBusImmediatelyHidesItsItemsAndBlocksWithdraw()
    {
        var prototype = new NetworkPrototype();
        Assert.True(prototype.Read().Index.ByInstance.ContainsKey(NetworkPrototype.ChargedId));
        prototype.Link("trunk", "bus-a", false);
        Assert.Equal(DeviceStatus.NoCore, prototype.Read().Network.Devices["bus-a"].Status);
        Assert.Single(prototype.Read().Index.ByInstance);
        Assert.Equal("NotVisible", prototype.Withdraw(NetworkPrototype.ChargedId).AccessFailure);
        Assert.True(prototype.Storage["locker-a"].Contains(NetworkPrototype.ChargedId));
        Assert.Equal(0, prototype.Player.Count);
        prototype.Link("trunk", "bus-a", true);
        Assert.Equal(3, prototype.Read().Index.ByInstance.Count);
    }

    [Fact]
    public void DisconnectingCoreStopsTerminalAndBothWriteDirections()
    {
        var prototype = new NetworkPrototype();
        Assert.Equal(MoveOutcome.Moved, prototype.Withdraw(NetworkPrototype.ChargedId).Movement);
        prototype.Link("core", "trunk", false);
        Assert.Equal(DeviceStatus.NoCore, prototype.Read().Connection.Status);
        Assert.Empty(prototype.Read().Index.ByInstance);
        Assert.Equal("TerminalOffline", prototype.Withdraw(NetworkPrototype.EmptyId).AccessFailure);
        Assert.Equal("TerminalOffline", prototype.Deposit("bus-a", NetworkPrototype.ChargedId).AccessFailure);
        Assert.True(prototype.Player.Contains(NetworkPrototype.ChargedId));
    }

    [Theory]
    [InlineData(0, 32, DeviceStatus.ChannelExhausted, 0)]
    [InlineData(1, 32, DeviceStatus.Online, 0)]
    [InlineData(2, 32, DeviceStatus.Online, 2)]
    [InlineData(8, 1, DeviceStatus.Online, 0)]
    [InlineData(8, 2, DeviceStatus.Online, 2)]
    public void ChannelsEnforceCoreAndSharedPipelineCapacity(int pipe, int core, DeviceStatus terminal, int items)
    {
        var view = new NetworkPrototype(new ChannelSettings(coreCapacity: core, pipelineCapacity: pipe)).Read();
        Assert.Equal(terminal, view.Connection.Status);
        Assert.Equal(items, view.Index.ByInstance.Count);
        Assert.True(view.Network.ChannelsUsed["trunk"] <= pipe);
        Assert.True(view.Network.ChannelsUsed["core"] <= core);
        Assert.NotEqual(DeviceStatus.Online, view.Network.Devices["bus-b"].Status);
    }

    [Fact]
    public void DisablingChannelsPreservesConnectivityButRemovesCapacityLimits()
    {
        var prototype = new NetworkPrototype(new ChannelSettings(coreCapacity: 0, pipelineCapacity: 0));
        Assert.Equal(DeviceStatus.ChannelExhausted, prototype.Read().Connection.Status);
        prototype.SetChannels(new ChannelSettings(false, 0, 0));
        Assert.All(prototype.Read().Network.Devices.Values, device => Assert.Equal(DeviceStatus.Online, device.Status));
        Assert.Equal(3, prototype.Read().Index.ByInstance.Count);
    }

    [Fact]
    public void MultipleCoresInOneComponentFailClosed()
    {
        var original = new NetworkPrototype();
        var topology = new NetworkTopology(original.Topology.Nodes.Values.Append(new NetworkNode("core-2", NetworkPart.Core)),
            original.Topology.Links.Append(new NetworkLink("core-2", "trunk")));
        var view = new TerminalCatalog(topology, "terminal", original.Storage.Values.Select(c => c.Snapshot()));
        Assert.All(view.Network.Devices.Values, d => Assert.Equal(DeviceStatus.ConflictingCores, d.Status));
        Assert.Empty(view.Index.ByInstance);
        Assert.All(view.Network.ChannelsUsed.Values, count => Assert.Equal(0, count));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConflictingTerminalCannotReadAnOnlineBusInAnotherNetwork(bool channels)
    {
        var original = new NetworkPrototype();
        var topology = new NetworkTopology(original.Topology.Nodes.Values.Concat(new[] {
            new NetworkNode("conflicting-core", NetworkPart.Core),
            new NetworkNode("separate-core", NetworkPart.Core),
            new NetworkNode("separate-bus", NetworkPart.ItemStorageBus, "unconnected") }),
            original.Topology.Links.Concat(new[] { new NetworkLink("conflicting-core", "trunk"),
                new NetworkLink("separate-core", "separate-bus") }), new ChannelSettings(channels));
        var view = new TerminalCatalog(topology, "terminal", original.Storage.Values.Select(c => c.Snapshot()));
        Assert.Equal(DeviceStatus.ConflictingCores, view.Connection.Status);
        Assert.Equal(DeviceStatus.Online, view.Network.Devices["separate-bus"].Status);
        Assert.Empty(view.Index.ByInstance);
        Assert.True(original.Storage["unconnected"].Contains(NetworkPrototype.HiddenId));
    }

    [Fact]
    public void IndependentNetworksDoNotLeakItemsToOtherTerminal()
    {
        var original = new NetworkPrototype();
        var topology = new NetworkTopology(original.Topology.Nodes.Values.Concat(new[] {
            new NetworkNode("core-other", NetworkPart.Core),
            new NetworkNode("bus-other", NetworkPart.ItemStorageBus, "unconnected") }),
            original.Topology.Links.Append(new NetworkLink("core-other", "bus-other")));
        var view = new TerminalCatalog(topology, "terminal", original.Storage.Values.Select(c => c.Snapshot()));
        Assert.Equal(DeviceStatus.Online, view.Network.Devices["bus-other"].Status);
        Assert.False(view.Index.ByInstance.ContainsKey(NetworkPrototype.HiddenId));
        Assert.Equal(3, view.Index.ByInstance.Count);
    }

    [Fact]
    public void TwoBusesOnOneContainerDoNotDoubleCountItsItems()
    {
        var original = new NetworkPrototype();
        var nodes = original.Topology.Nodes.Values.Select(n => n.Id == "bus-b"
            ? new NetworkNode("bus-b", NetworkPart.ItemStorageBus, "locker-a") : n);
        var view = new TerminalCatalog(new NetworkTopology(nodes, original.Topology.Links), "terminal",
            original.Storage.Values.Select(c => c.Snapshot()));
        Assert.Equal(2, view.Index.ByInstance.Count);
        Assert.Equal(3, view.Network.ChannelsUsed["core"]); // Both buses still reserve channels.
    }

    [Fact]
    public void MissingContainerIsReportedWithoutInventingOrCachingItems()
    {
        var prototype = new NetworkPrototype();
        Assert.Equal(3, prototype.Read().Index.ByInstance.Count);
        prototype.Storage.Remove("locker-a");
        Assert.Equal("locker-a", Assert.Single(prototype.Read().MissingContainerIds));
        Assert.Single(prototype.Read().Index.ByInstance);
    }

    [Fact]
    public void AllocationIsStableRegardlessOfNodeOrLinkInputOrder()
    {
        var original = new NetworkPrototype(new ChannelSettings(pipelineCapacity: 2));
        var reversed = new NetworkTopology(original.Topology.Nodes.Values.Reverse(), original.Topology.Links.Reverse(), original.Topology.Channels);
        var first = new NetworkEvaluation(original.Topology);
        var second = new NetworkEvaluation(reversed);
        foreach (var pair in first.Devices)
        {
            Assert.Equal(pair.Value.Status, second.Devices[pair.Key].Status);
            Assert.Equal(pair.Value.Route, second.Devices[pair.Key].Route);
        }
        Assert.Equal(DeviceStatus.Online, second.Devices["bus-a"].Status);
        Assert.Equal(DeviceStatus.ChannelExhausted, second.Devices["bus-b"].Status);
    }

    [Fact]
    public void RouteCanUseAlternativePipelineWhenShortestPipelineIsFull()
    {
        var topology = new NetworkTopology(new[] {
            new NetworkNode("core", NetworkPart.Core), new NetworkNode("a", NetworkPart.Pipeline),
            new NetworkNode("b", NetworkPart.Pipeline), new NetworkNode("q", NetworkPart.Pipeline),
            new NetworkNode("terminal", NetworkPart.Terminal), new NetworkNode("bus", NetworkPart.ItemStorageBus, "box") },
            new[] { new NetworkLink("core", "a"), new NetworkLink("core", "b"), new NetworkLink("a", "q"),
                new NetworkLink("b", "q"), new NetworkLink("a", "terminal"), new NetworkLink("q", "bus") },
            new ChannelSettings(pipelineCapacity: 1));
        var evaluated = new NetworkEvaluation(topology);
        Assert.Equal(new[] { "core", "a", "terminal" }, evaluated.Devices["terminal"].Route);
        Assert.Equal(new[] { "core", "b", "q", "bus" }, evaluated.Devices["bus"].Route);
        Assert.Equal(2, evaluated.ChannelsUsed["core"]);
    }

    [Fact]
    public void DuplicateLinksAreCanonicalAndUnknownNodesAreRejected()
    {
        var nodes = new[] { new NetworkNode("core", NetworkPart.Core), new NetworkNode("terminal", NetworkPart.Terminal) };
        var topology = new NetworkTopology(nodes, new[] { new NetworkLink("core", "terminal"), new NetworkLink("terminal", "core") });
        Assert.Single(topology.Links);
        Assert.Equal(1, new NetworkEvaluation(topology).ChannelsUsed["core"]);
        Assert.Throws<ArgumentException>(() => new NetworkTopology(nodes, new[] { new NetworkLink("unknown", "terminal") }));
    }

    [Fact]
    public void EndpointCannotSilentlyBecomeAPipelineBetweenNetworks()
    {
        var nodes = new[] { new NetworkNode("a", NetworkPart.Core), new NetworkNode("b", NetworkPart.Core),
            new NetworkNode("terminal", NetworkPart.Terminal) };
        Assert.Throws<ArgumentException>(() => new NetworkTopology(nodes,
            new[] { new NetworkLink("a", "terminal"), new NetworkLink("b", "terminal") }));
    }

    [Fact]
    public void TerminalWithdrawAndDepositPreserveExactInstanceAndRefreshIndex()
    {
        var prototype = new NetworkPrototype();
        var before = prototype.Read();
        var originalItem = before.Index.ByInstance[NetworkPrototype.ChargedId].Item;
        Assert.Equal(MoveOutcome.Moved, prototype.Withdraw(originalItem.InstanceId).Movement);
        Assert.Same(originalItem, Assert.Single(prototype.Player.Snapshot().Items));
        Assert.Equal(2, prototype.Read().Index.ByInstance.Count);
        Assert.Equal("NotVisible", prototype.Withdraw(originalItem.InstanceId).AccessFailure);
        Assert.Equal(MoveOutcome.Moved, prototype.Deposit("bus-b", originalItem.InstanceId).Movement);
        var after = prototype.Read();
        Assert.Same(originalItem, after.Index.ByInstance[originalItem.InstanceId].Item);
        Assert.Equal("locker-b", after.Index.ByInstance[originalItem.InstanceId].ContainerId);
        Assert.Equal("charge=0", after.Index.ByInstance[NetworkPrototype.EmptyId].Item.StateTag);
        Assert.Equal(3, after.Index.ByInstance.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DepositFailurePreservesCarriedItem(bool reject)
    {
        var prototype = new NetworkPrototype();
        prototype.Withdraw(NetworkPrototype.ChargedId);
        prototype.Storage["locker-b"] = new InMemoryContainer("locker-b", reject ? 4 : 0, _ => !reject);
        Assert.Equal(reject ? MoveOutcome.Rejected : MoveOutcome.Full, prototype.Deposit("bus-b", NetworkPrototype.ChargedId).Movement);
        Assert.True(prototype.Player.Contains(NetworkPrototype.ChargedId));
        Assert.Equal(0, prototype.Storage["locker-b"].Count);
    }

    [Fact]
    public void ChannelExhaustionAfterReadingBlocksWritesToBus()
    {
        var prototype = new NetworkPrototype();
        prototype.Withdraw(NetworkPrototype.ChargedId);
        prototype.SetChannels(new ChannelSettings(pipelineCapacity: 1));
        Assert.Equal("BusOffline", prototype.Deposit("bus-b", NetworkPrototype.ChargedId).AccessFailure);
        Assert.Equal("NotVisible", prototype.Withdraw(NetworkPrototype.EmptyId).AccessFailure);
        Assert.True(prototype.Player.Contains(NetworkPrototype.ChargedId));
    }

    [Fact]
    public void InteractiveConsoleCommandsUseTheCoreReadModelAndSimulatedContainers()
    {
        using var output = new StringWriter();
        using var input = new StringReader("list battery\ntake 1111\ninventory\nput 1111 bus-b\ndisconnect trunk bus-a\nlist\nchannels on 1\nlist\nchannels off\nconnect trunk bus-a\nlist\nquit\n");
        Assert.Equal(0, PrototypeTerminal.Run(input, output));
        var transcript = output.ToString();
        Assert.Contains("charge=100", transcript);
        Assert.Contains("charge=0", transcript);
        Assert.Equal(2, transcript.Split('\n').Count(line => line.Trim() == "ae> Moved"));
        Assert.Contains("Player instances: 1", transcript);
        Assert.Contains("visible=0", transcript);
        Assert.Contains("visible=3", transcript);
        Assert.DoesNotContain("@unconnected", transcript);
    }
}
