using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core;
using DeepSpaceAE.Core.Network;

namespace DeepSpaceAE.Tests.Simulation;

internal sealed record TerminalMoveResult(string? AccessFailure, MoveOutcome? Movement);

// Development/test host only. No fake game API, grid assumptions, or real inventory mutations.
internal sealed class NetworkPrototype
{
    public static readonly Guid ChargedId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid EmptyId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static readonly Guid OreId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    public static readonly Guid HiddenId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    public NetworkTopology Topology { get; private set; }
    public Dictionary<string, InMemoryContainer> Storage { get; } = new(StringComparer.Ordinal);
    public InMemoryContainer Player { get; } = new("player", 8);

    public NetworkPrototype(ChannelSettings? channels = null)
    {
        Topology = new NetworkTopology(new[] {
            new NetworkNode("core", NetworkPart.Core),
            new NetworkNode("trunk", NetworkPart.Pipeline),
            new NetworkNode("terminal", NetworkPart.Terminal),
            new NetworkNode("bus-a", NetworkPart.ItemStorageBus, "locker-a"),
            new NetworkNode("bus-b", NetworkPart.ItemStorageBus, "locker-b") }, new[] {
            new NetworkLink("core", "trunk"), new NetworkLink("trunk", "terminal"),
            new NetworkLink("trunk", "bus-a"), new NetworkLink("trunk", "bus-b") }, channels);
        Storage.Add("locker-a", new InMemoryContainer("locker-a", 4));
        Storage.Add("locker-b", new InMemoryContainer("locker-b", 4));
        Storage.Add("unconnected", new InMemoryContainer("unconnected", 4));
        Storage["locker-a"].Seed(new ItemInstance(ChargedId, "battery", "charge=100"));
        Storage["locker-a"].Seed(new ItemInstance(EmptyId, "battery", "charge=0"));
        Storage["locker-b"].Seed(new ItemInstance(OreId, "ore", "grade=normal"));
        Storage["unconnected"].Seed(new ItemInstance(HiddenId, "hidden", "not network storage"));
    }

    public TerminalCatalog Read() => new(Topology, "terminal", Storage.Values.Select(c => c.Snapshot()));

    public void Link(string a, string b, bool connected)
    {
        if (!Topology.Nodes.ContainsKey(a) || !Topology.Nodes.ContainsKey(b))
            throw new ArgumentException("Both link endpoints must exist.");
        var link = new NetworkLink(a, b);
        var links = Topology.Links.Where(l => l.A != link.A || l.B != link.B).ToList();
        if (connected) links.Add(link);
        Topology = new NetworkTopology(Topology.Nodes.Values, links, Topology.Channels);
    }

    public void SetChannels(ChannelSettings settings) => Topology = new NetworkTopology(Topology.Nodes.Values, Topology.Links, settings);

    public TerminalMoveResult Withdraw(Guid instanceId)
    {
        var view = Read(); // Re-evaluate topology and current source observations before every simulated write.
        if (view.Connection.Status != DeviceStatus.Online) return new("TerminalOffline", null);
        if (!view.Index.ByInstance.TryGetValue(instanceId, out var location)) return new("NotVisible", null);
        return new(null, Storage[location.ContainerId].MoveOneTo(Player, instanceId));
    }

    public TerminalMoveResult Deposit(string busId, Guid instanceId)
    {
        var view = Read();
        if (view.Connection.Status != DeviceStatus.Online) return new("TerminalOffline", null);
        if (!Topology.Nodes.TryGetValue(busId, out var node) || node.Part != NetworkPart.ItemStorageBus)
            return new("NotStorageBus", null);
        var bus = view.Network.Devices[busId];
        if (bus.Status != DeviceStatus.Online || bus.CoreId != view.Connection.CoreId) return new("BusOffline", null);
        if (!Storage.TryGetValue(node.ContainerId!, out var destination)) return new("MissingContainer", null);
        return new(null, Player.MoveOneTo(destination, instanceId));
    }
}
