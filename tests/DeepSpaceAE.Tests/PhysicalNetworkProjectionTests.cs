using System;
using System.Linq;
using DeepSpaceAE.Core.Network;
using Xunit;

namespace DeepSpaceAE.Tests;

public sealed class PhysicalNetworkProjectionTests
{
    [Theory]
    [InlineData(NetworkPart.Terminal)]
    [InlineData(NetworkPart.DiskDrive)]
    [InlineData(NetworkPart.ItemInterface)]
    [InlineData(NetworkPart.FluidInterface)]
    [InlineData(NetworkPart.ItemStorageBus)]
    [InlineData(NetworkPart.ImportBus)]
    [InlineData(NetworkPart.ExportBus)]
    [InlineData(NetworkPart.EnergyReceiver)]
    public void CoreAdjacentDeviceStaysOnlineAfterDanglingCableIsAdded(NetworkPart part)
    {
        var nodes = new[] { new NetworkNode("core", NetworkPart.Core),
            new NetworkNode("device", part, part == NetworkPart.DiskDrive || part == NetworkPart.ItemStorageBus ? "storage" : null),
            new NetworkNode("a-stub", NetworkPart.Pipeline), new NetworkNode("b-stub", NetworkPart.Pipeline),
            new NetworkNode("remote", NetworkPart.Terminal) };
        var before = new[] { new NetworkLink("core", "device") };
        Assert.Equal(DeviceStatus.Online, new NetworkEvaluation(PhysicalNetworkProjection.Build(nodes, before)).Devices["device"].Status);
        var after = before.Concat(new[] { new NetworkLink("device", "a-stub"),
            new NetworkLink("a-stub", "b-stub"), new NetworkLink("b-stub", "remote") }).ToArray();
        foreach (bool reverse in new[] { false, true })
        {
            var topology = PhysicalNetworkProjection.Build(reverse ? nodes.Reverse() : nodes, reverse ? after.Reverse() : after);
            var evaluation = new NetworkEvaluation(topology);
            Assert.Equal(DeviceStatus.Online, evaluation.Devices["device"].Status);
            Assert.Equal("core", evaluation.Devices["device"].CoreId);
            bool relay = part != NetworkPart.ItemStorageBus && part != NetworkPart.ImportBus && part != NetworkPart.ExportBus;
            Assert.Contains("core", topology.Neighbors("device"));
            Assert.Equal(relay ? 2 : 1, topology.Neighbors("device").Count);
            Assert.Equal(relay ? DeviceStatus.Online : DeviceStatus.NoCore, evaluation.Devices["remote"].Status);
            Assert.Equal(relay, evaluation.ComponentCore.ContainsKey("a-stub"));
            Assert.Equal(relay ? 2 : 1, evaluation.EndpointCounts["core"]);
        }
    }

    [Fact]
    public void CableConnectedDeviceConnectsAllItsBranches()
    {
        var nodes = new[] { new NetworkNode("core", NetworkPart.Core), new NetworkNode("live", NetworkPart.Pipeline),
            new NetworkNode("stub-a", NetworkPart.Pipeline), new NetworkNode("stub-b", NetworkPart.Pipeline),
            new NetworkNode("terminal", NetworkPart.Terminal) };
        var topology = PhysicalNetworkProjection.Build(nodes, new[] { new NetworkLink("core", "live"),
            new NetworkLink("live", "terminal"), new NetworkLink("terminal", "stub-a"), new NetworkLink("terminal", "stub-b") });
        Assert.Equal(new[] { "live", "stub-a", "stub-b" }, topology.Neighbors("terminal"));
        var evaluation = new NetworkEvaluation(topology);
        Assert.Equal(DeviceStatus.Online, evaluation.Devices["terminal"].Status);
        Assert.Equal("core", evaluation.ComponentCore["stub-a"]);
        Assert.Equal("core", evaluation.ComponentCore["stub-b"]);
    }

    [Fact]
    public void CorelessCableCannotHideAConflictingCoreComponent()
    {
        var nodes = new[] { new NetworkNode("left", NetworkPart.Core), new NetworkNode("right", NetworkPart.Core),
            new NetworkNode("stub", NetworkPart.Pipeline), new NetworkNode("terminal", NetworkPart.Terminal) };
        var topology = PhysicalNetworkProjection.Build(nodes, new[] { new NetworkLink("left", "right"),
            new NetworkLink("left", "terminal"), new NetworkLink("terminal", "stub") });
        Assert.Equal(DeviceStatus.ConflictingCores, new NetworkEvaluation(topology).Devices["terminal"].Status);
    }

    [Fact]
    public void DeviceTouchingTwoPipelinesPreservesBothConnectionsWithoutDoubleCounting()
    {
        var nodes = new[] { new NetworkNode("core", NetworkPart.Core), new NetworkNode("a", NetworkPart.Pipeline),
            new NetworkNode("b", NetworkPart.Pipeline), new NetworkNode("terminal", NetworkPart.Terminal) };
        var edges = new[] { new NetworkLink("core", "a"), new NetworkLink("a", "b"),
            new NetworkLink("terminal", "a"), new NetworkLink("terminal", "b") };
        var topology = PhysicalNetworkProjection.Build(nodes, edges);
        Assert.Equal(new[] { "a", "b" }, topology.Neighbors("terminal"));
        Assert.Equal(DeviceStatus.Online, new NetworkEvaluation(topology).Devices["terminal"].Status);
        Assert.Equal(1, new NetworkEvaluation(topology).EndpointCounts["core"]);
    }

    [Fact]
    public void DeviceTouchingTwoControllerNetworksJoinsThemAndReportsConflict()
    {
        var nodes = new[] { new NetworkNode("left", NetworkPart.Core), new NetworkNode("right", NetworkPart.Core),
            new NetworkNode("terminal", NetworkPart.Terminal) };
        var topology = PhysicalNetworkProjection.Build(nodes, new[] {
            new NetworkLink("left", "terminal"), new NetworkLink("right", "terminal") });
        Assert.Equal(new[] { "left", "right" }, topology.Neighbors("terminal"));
        Assert.Equal(DeviceStatus.ConflictingCores, new NetworkEvaluation(topology).Devices["terminal"].Status);
    }

    [Fact]
    public void RelayingTerminalCanConnectTheCableHostingAStorageBus()
    {
        var nodes = new[] { new NetworkNode("core", NetworkPart.Core), new NetworkNode("terminal", NetworkPart.Terminal),
            new NetworkNode("wire", NetworkPart.Pipeline), new NetworkNode("bus", NetworkPart.ItemStorageBus, "box") };
        var topology = PhysicalNetworkProjection.Build(nodes, new[] {
            new NetworkLink("core", "terminal"), new NetworkLink("terminal", "wire"), new NetworkLink("wire", "bus") });
        var evaluated = new NetworkEvaluation(topology);
        Assert.Equal(DeviceStatus.Online, evaluated.Devices["terminal"].Status);
        Assert.Equal(DeviceStatus.Online, evaluated.Devices["bus"].Status);
    }

    [Fact]
    public void ReversingPhysicalObservationOrderDoesNotChangeProjection()
    {
        var nodes = new[] { new NetworkNode("core", NetworkPart.Core), new NetworkNode("a", NetworkPart.Pipeline),
            new NetworkNode("terminal", NetworkPart.Terminal) };
        var edges = new[] { new NetworkLink("core", "a"), new NetworkLink("a", "terminal"), new NetworkLink("core", "terminal") };
        var first = PhysicalNetworkProjection.Build(nodes, edges);
        var second = PhysicalNetworkProjection.Build(nodes.Reverse(), edges.Reverse());
        Assert.Equal(first.Links.Select(e => (e.A, e.B)), second.Links.Select(e => (e.A, e.B)));
    }

    [Fact]
    public void InvalidPhysicalObservationIsRejectedBeforeIndexing()
    {
        Assert.Throws<ArgumentException>(() => PhysicalNetworkProjection.Build(new[] { new NetworkNode("core", NetworkPart.Core) },
            new[] { new NetworkLink("core", "missing") }));
    }
}
