using System;
using System.Linq;
using DeepSpaceAE.Core.Network;
using Xunit;

namespace DeepSpaceAE.Tests;

public sealed class PhysicalNetworkProjectionTests
{
    [Fact]
    public void EndpointTouchingTwoPipelinesInSameComponentGetsOneConnection()
    {
        var nodes = new[] { new NetworkNode("core", NetworkPart.Core), new NetworkNode("a", NetworkPart.Pipeline),
            new NetworkNode("b", NetworkPart.Pipeline), new NetworkNode("terminal", NetworkPart.Terminal) };
        var edges = new[] { new NetworkLink("core", "a"), new NetworkLink("a", "b"),
            new NetworkLink("terminal", "a"), new NetworkLink("terminal", "b") };
        var topology = PhysicalNetworkProjection.Build(nodes, edges, new ChannelSettings());
        Assert.Equal("a", Assert.Single(topology.Neighbors("terminal")));
        Assert.Equal(DeviceStatus.Online, new NetworkEvaluation(topology).Devices["terminal"].Status);
        Assert.Equal(1, new NetworkEvaluation(topology).ChannelsUsed["core"]);
    }

    [Fact]
    public void EndpointTouchingSeparateNetworksDoesNotBridgeThem()
    {
        var nodes = new[] { new NetworkNode("left", NetworkPart.Core), new NetworkNode("right", NetworkPart.Core),
            new NetworkNode("terminal", NetworkPart.Terminal) };
        var topology = PhysicalNetworkProjection.Build(nodes, new[] {
            new NetworkLink("left", "terminal"), new NetworkLink("right", "terminal") }, new ChannelSettings());
        Assert.Empty(topology.Neighbors("terminal"));
        Assert.Equal(DeviceStatus.NoCore, new NetworkEvaluation(topology).Devices["terminal"].Status);
    }

    [Fact]
    public void TouchingTerminalAndBusCannotRelayChannels()
    {
        var nodes = new[] { new NetworkNode("core", NetworkPart.Core), new NetworkNode("terminal", NetworkPart.Terminal),
            new NetworkNode("bus", NetworkPart.ItemStorageBus, "box") };
        var topology = PhysicalNetworkProjection.Build(nodes, new[] {
            new NetworkLink("core", "terminal"), new NetworkLink("terminal", "bus") }, new ChannelSettings());
        var evaluated = new NetworkEvaluation(topology);
        Assert.Equal(DeviceStatus.Online, evaluated.Devices["terminal"].Status);
        Assert.Equal(DeviceStatus.NoCore, evaluated.Devices["bus"].Status);
    }

    [Fact]
    public void ReversingPhysicalObservationOrderDoesNotChangeProjection()
    {
        var nodes = new[] { new NetworkNode("core", NetworkPart.Core), new NetworkNode("a", NetworkPart.Pipeline),
            new NetworkNode("terminal", NetworkPart.Terminal) };
        var edges = new[] { new NetworkLink("core", "a"), new NetworkLink("a", "terminal"), new NetworkLink("core", "terminal") };
        var first = PhysicalNetworkProjection.Build(nodes, edges, new ChannelSettings());
        var second = PhysicalNetworkProjection.Build(nodes.Reverse(), edges.Reverse(), new ChannelSettings());
        Assert.Equal(first.Links.Select(e => (e.A, e.B)), second.Links.Select(e => (e.A, e.B)));
    }

    [Fact]
    public void InvalidPhysicalObservationIsRejectedBeforeIndexing()
    {
        Assert.Throws<ArgumentException>(() => PhysicalNetworkProjection.Build(new[] { new NetworkNode("core", NetworkPart.Core) },
            new[] { new NetworkLink("core", "missing") }, new ChannelSettings()));
    }
}
