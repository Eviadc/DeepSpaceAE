using System;
using System.Linq;
using DeepSpaceAE.Core;
using DeepSpaceAE.Core.Network;
using Xunit;

namespace DeepSpaceAE.Tests;

public sealed class DeviceRelayTests
{
    [Theory]
    [InlineData(NetworkPart.Terminal)]
    [InlineData(NetworkPart.DiskDrive)]
    [InlineData(NetworkPart.ItemInterface)]
    [InlineData(NetworkPart.FluidInterface)]
    [InlineData(NetworkPart.EnergyReceiver)]
    [InlineData(NetworkPart.WirelessConnector)]
    public void IndependentDeviceCarriesNetworkToItsOtherSide(NetworkPart part)
    {
        var nodes = new[] { new NetworkNode("core", NetworkPart.Core),
            new NetworkNode("device", part, part == NetworkPart.DiskDrive ? "device-disk" : null),
            new NetworkNode("wire", NetworkPart.Pipeline), new NetworkNode("remote", NetworkPart.Terminal) };
        var edges = new[] { new NetworkLink("core", "device"), new NetworkLink("device", "wire"), new NetworkLink("wire", "remote") };
        var evaluation = new NetworkEvaluation(PhysicalNetworkProjection.Build(nodes, edges));
        Assert.Equal(DeviceStatus.Online, evaluation.Devices["device"].Status);
        Assert.Equal(DeviceStatus.Online, evaluation.Devices["remote"].Status);
        Assert.Equal(new[] { "core", "device", "wire", "remote" }, evaluation.Devices["remote"].Route);
        Assert.Equal(2, evaluation.EndpointCounts["core"]);
    }

    [Fact]
    public void AdjacentDevicesRelayWithoutAnyCableAndExposeTheRealStorageOnce()
    {
        var nodes = new[] { new NetworkNode("core", NetworkPart.Core), new NetworkNode("input", NetworkPart.ItemInterface),
            new NetworkNode("drive", NetworkPart.DiskDrive, "disk"), new NetworkNode("fluid", NetworkPart.FluidInterface),
            new NetworkNode("terminal", NetworkPart.Terminal) };
        var edges = new[] { new NetworkLink("core", "input"), new NetworkLink("input", "drive"),
            new NetworkLink("drive", "fluid"), new NetworkLink("fluid", "terminal") };
        var topology = PhysicalNetworkProjection.Build(nodes, edges);
        var item = new ItemInstance(Guid.NewGuid(), "cargo", "state");
        var catalog = new TerminalCatalog(topology, "terminal", new[] { new ContainerSnapshot("disk", new[] { item }) });
        Assert.All(catalog.Network.Devices.Values, d => Assert.Equal(DeviceStatus.Online, d.Status));
        Assert.Equal(item.InstanceId, Assert.Single(catalog.Index.ByInstance).Key);
        Assert.Equal(4, catalog.Network.EndpointCounts["core"]);
        Assert.Equal(6, catalog.Network.NightCosts["core"]);
    }

    [Fact]
    public void RelayLoopDoesNotDuplicateDevicesOrLoseItsAlternatePath()
    {
        var nodes = new[] { new NetworkNode("core", NetworkPart.Core), new NetworkNode("left", NetworkPart.ItemInterface),
            new NetworkNode("right", NetworkPart.FluidInterface), new NetworkNode("terminal", NetworkPart.Terminal) };
        var edges = new[] { new NetworkLink("core", "left"), new NetworkLink("left", "terminal"),
            new NetworkLink("core", "right"), new NetworkLink("right", "terminal"), new NetworkLink("left", "right") };
        foreach (var observed in new[] { edges, edges.Reverse().ToArray(), edges.Skip(1).ToArray() })
        {
            var evaluation = new NetworkEvaluation(PhysicalNetworkProjection.Build(nodes, observed));
            Assert.All(evaluation.Devices.Values, d => Assert.Equal(DeviceStatus.Online, d.Status));
            Assert.Equal(3, evaluation.Devices.Count);
            Assert.Equal(3, evaluation.EndpointCounts["core"]);
            Assert.Equal(5, evaluation.NightCosts["core"]);
            Assert.All(evaluation.Devices.Values, d => Assert.Equal(d.Route.Count, d.Route.Distinct().Count()));
        }
    }

    [Fact]
    public void ControllerNetworksJoinedThroughADeviceConflictAsOneNetwork()
    {
        var nodes = new[] { new NetworkNode("left", NetworkPart.Core), new NetworkNode("right", NetworkPart.Core),
            new NetworkNode("drive", NetworkPart.DiskDrive, "disk"), new NetworkNode("terminal", NetworkPart.Terminal) };
        var topology = PhysicalNetworkProjection.Build(nodes, new[] { new NetworkLink("left", "drive"),
            new NetworkLink("drive", "right"), new NetworkLink("right", "terminal") });
        var evaluation = new NetworkEvaluation(topology);
        Assert.All(evaluation.Devices.Values, d => Assert.Equal(DeviceStatus.ConflictingCores, d.Status));
        Assert.Empty(evaluation.ComponentCore);
        Assert.Empty(evaluation.NightCosts);
    }
}
