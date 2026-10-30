using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core;
using DeepSpaceAE.Core.Network;
using Xunit;

namespace DeepSpaceAE.Tests;

public sealed class NetworkEvaluationParityTests
{
    [Fact]
    public void CatalogRejectsEvaluationFromAnotherObservation()
    {
        var (nodes, links) = LongNetwork(2);
        var first = PhysicalNetworkProjection.Build(nodes, links, new ChannelSettings());
        var second = PhysicalNetworkProjection.Build(nodes, links, new ChannelSettings(false));
        Assert.Throws<ArgumentException>(() => new TerminalCatalog(second, "d00000", Array.Empty<ContainerSnapshot>(), new NetworkEvaluation(first)));
    }
    [Fact]
    public void SharedSearchMatchesIndependentPerDeviceSearchOnRandomGraphs()
    {
        var random = new Random(16842);
        for (int sample = 0; sample < 160; sample++)
        {
            int count = random.Next(8, 30), cores = random.Next(3);
            var nodes = Enumerable.Range(0, count).Select(i => new NetworkNode($"p{i:D3}", i < cores ? NetworkPart.Core : NetworkPart.Pipeline)).ToList();
            var links = new List<NetworkLink>();
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    if (random.NextDouble() < 0.12) links.Add(new NetworkLink(nodes[i].Id, nodes[j].Id));
            for (int i = 0; i < 45; i++)
            {
                var kind = random.Next(3) == 0 ? NetworkPart.Terminal : NetworkPart.ItemStorageBus;
                var node = new NetworkNode($"d{i:D3}", kind, kind == NetworkPart.ItemStorageBus ? "box" + i : null);
                nodes.Add(node);
                links.Add(new NetworkLink(node.Id, nodes[random.Next(count)].Id));
            }
            foreach (bool limited in new[] { true, false })
            {
                var topology = new NetworkTopology(nodes, links, new ChannelSettings(limited, random.Next(16), random.Next(5)));
                var expected = Reference(topology);
                var actual = new NetworkEvaluation(topology);
                foreach (var pair in expected.Devices)
                {
                    Assert.Equal(pair.Value.Status, actual.Devices[pair.Key].Status);
                    Assert.Equal(pair.Value.Core, actual.Devices[pair.Key].CoreId);
                    Assert.Equal(pair.Value.Route, actual.Devices[pair.Key].Route);
                }
                foreach (var pair in expected.Used) Assert.Equal(pair.Value, actual.ChannelsUsed[pair.Key]);
            }
        }
    }

    [Fact]
    public void UnlimitedLongNetworkSharesOneTreeAndAggregatesUsage()
    {
        const int count = 5000;
        var (nodes, links) = LongNetwork(count);
        var actual = new NetworkEvaluation(PhysicalNetworkProjection.Build(nodes, links, new ChannelSettings(false)));
        Assert.Equal(1, actual.SearchPasses);
        Assert.Equal(count + 1, actual.VisitedTransportNodes);
        Assert.Equal(count, actual.ChannelsUsed["core"]);
        Assert.Equal(count, actual.ChannelsUsed["p00000"]);
        Assert.Equal(1, actual.ChannelsUsed["p04999"]);
        Assert.Equal(count + 2, actual.Devices["d04999"].Route.Count);
        Assert.All(actual.Devices.Values, device => Assert.Equal(DeviceStatus.Online, device.Status));
    }

    [Fact]
    public void SaturatedCutIsSearchedOnceInsteadOfOncePerFailedDevice()
    {
        var (nodes, links) = LongNetwork(3000);
        var actual = new NetworkEvaluation(PhysicalNetworkProjection.Build(nodes, links, new ChannelSettings(pipelineCapacity: 1)));
        Assert.Equal(1, actual.Devices.Values.Count(device => device.Status == DeviceStatus.Online));
        Assert.Equal(2, actual.SearchPasses);
        Assert.Equal(3002, actual.VisitedTransportNodes);
    }

    internal static (List<NetworkNode>, List<NetworkLink>) LongNetwork(int count)
    {
        var nodes = new List<NetworkNode> { new NetworkNode("core", NetworkPart.Core) };
        var edges = new List<NetworkLink>();
        for (int i = 0; i < count; i++)
        {
            var pipe = $"p{i:D5}";
            var device = $"d{i:D5}";
            nodes.Add(new NetworkNode(pipe, NetworkPart.Pipeline));
            nodes.Add(new NetworkNode(device, NetworkPart.Terminal));
            edges.Add(new NetworkLink(i == 0 ? "core" : $"p{i - 1:D5}", pipe));
            edges.Add(new NetworkLink(pipe, device));
        }
        return (nodes, edges);
    }

    // Independent baseline: the previous greedy rule, with fresh BFS for every device.
    private static (Dictionary<string, (DeviceStatus Status, string? Core, string[] Route)> Devices, Dictionary<string, int> Used) Reference(NetworkTopology topology)
    {
        var devices = new Dictionary<string, (DeviceStatus, string?, string[])>();
        var used = topology.Nodes.Values.Where(n => !n.ConsumesChannel).ToDictionary(n => n.Id, _ => 0);
        var seen = new HashSet<string>();
        foreach (var start in topology.Nodes.Keys)
        {
            if (!seen.Add(start)) continue;
            var component = new List<NetworkNode>();
            var queue = new Queue<string>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(topology.Nodes[current]);
                foreach (var neighbor in topology.Neighbors(current)) if (seen.Add(neighbor)) queue.Enqueue(neighbor);
            }
            var cores = component.Where(n => n.Part == NetworkPart.Core).ToArray();
            foreach (var node in component.Where(n => n.ConsumesChannel).OrderBy(n => n.Part == NetworkPart.Terminal ? 0 : 1).ThenBy(n => n.Id, StringComparer.Ordinal))
            {
                if (cores.Length != 1)
                { devices.Add(node.Id, (cores.Length == 0 ? DeviceStatus.NoCore : DeviceStatus.ConflictingCores, null, Array.Empty<string>())); continue; }
                var core = cores[0].Id;
                var previous = new Dictionary<string, string?> { [core] = null };
                queue.Enqueue(core);
                bool found = false;
                if (topology.Channels.Enabled && used[core] >= topology.Channels.CoreCapacity) queue.Clear();
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (current == node.Id) { found = true; queue.Clear(); break; }
                    if (topology.Nodes[current].ConsumesChannel) continue;
                    foreach (var next in topology.Neighbors(current))
                    {
                        if (previous.ContainsKey(next)) continue;
                        if (topology.Channels.Enabled && topology.Nodes[next].Part == NetworkPart.Pipeline && used[next] >= topology.Channels.PipelineCapacity) continue;
                        previous.Add(next, current);
                        queue.Enqueue(next);
                    }
                }
                if (!found) { devices.Add(node.Id, (DeviceStatus.ChannelExhausted, core, Array.Empty<string>())); continue; }
                var route = new List<string>();
                for (string? current = node.Id; current != null; current = previous[current]) route.Add(current);
                route.Reverse();
                foreach (var step in route) if (used.ContainsKey(step)) used[step]++;
                devices.Add(node.Id, (DeviceStatus.Online, core, route.ToArray()));
            }
        }
        return (devices, used);
    }
}
