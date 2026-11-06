using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DeepSpaceAE.Core.Network;

public enum DeviceStatus { Online, NoCore, ConflictingCores, ChannelExhausted }

// Shared immutable path prefixes avoid quadratic route storage on long unlimited networks.
internal sealed class RoutePath
{
    internal readonly string Id;
    internal readonly RoutePath? Parent;
    internal readonly int Length;
    internal RoutePath(string id, RoutePath? parent = null) { Id = id; Parent = parent; Length = (parent?.Length ?? 0) + 1; }
}

public sealed class DeviceConnection
{
    private readonly RoutePath? path;
    private IReadOnlyList<string>? route;
    public DeviceStatus Status { get; }
    public string? CoreId { get; }
    public IReadOnlyList<string> Route
    {
        get
        {
            if (route != null) return route;
            var result = new string[path?.Length ?? 0];
            for (var cursor = path; cursor != null; cursor = cursor.Parent) result[cursor.Length - 1] = cursor.Id;
            return route = Array.AsReadOnly(result);
        }
    }
    internal DeviceConnection(DeviceStatus status, string? coreId = null, RoutePath? path = null)
    { Status = status; CoreId = coreId; this.path = path; }
}

/// <summary>Shares one residual reachability tree until a pipeline saturates; failed devices do not repeat BFS.</summary>
public sealed class NetworkEvaluation
{
    internal NetworkTopology Topology { get; }
    public IReadOnlyDictionary<string, DeviceConnection> Devices { get; }
    public IReadOnlyDictionary<string, int> ChannelsUsed { get; }
    public IReadOnlyDictionary<string, string> ComponentCore { get; }
    public IReadOnlyDictionary<string, int> NightCosts { get; }
    public int SearchPasses { get; private set; }
    public int VisitedTransportNodes { get; private set; }

    public NetworkEvaluation(NetworkTopology topology)
    {
        if (topology is null) throw new ArgumentNullException(nameof(topology));
        Topology = topology;
        var devices = new Dictionary<string, DeviceConnection>(StringComparer.Ordinal);
        var used = topology.Nodes.Values.Where(n => !n.ConsumesChannel).ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        var membership = new Dictionary<string, string>(StringComparer.Ordinal);
        var costs = new Dictionary<string, int>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in topology.Nodes.Keys)
        {
            if (!visited.Add(start)) continue;
            var component = new List<NetworkNode>();
            var queue = new Queue<string>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                component.Add(topology.Nodes[id]);
                foreach (var neighbor in topology.Neighbors(id)) if (visited.Add(neighbor)) queue.Enqueue(neighbor);
            }
            var cores = component.Where(n => n.Part == NetworkPart.Core).ToArray();
            var endpoints = component.Where(n => n.ConsumesChannel)
                .OrderBy(n => n.Part == NetworkPart.Terminal ? 0 : 1).ThenBy(n => n.Id, StringComparer.Ordinal);
            if (cores.Length != 1)
            {
                foreach (var node in endpoints) devices.Add(node.Id, new DeviceConnection(cores.Length == 0 ? DeviceStatus.NoCore : DeviceStatus.ConflictingCores));
                continue;
            }
            var core = cores[0].Id;
            costs[core] = component.Sum(n => NightCost(n.Part));
            foreach (var n in component) membership.Add(n.Id, core);
            Dictionary<string, RoutePath>? paths = null;
            var order = new List<RoutePath>();
            foreach (var node in endpoints)
            {
                if (topology.Channels.Enabled && used[core] >= topology.Channels.CoreCapacity)
                { devices.Add(node.Id, new DeviceConnection(DeviceStatus.ChannelExhausted, core)); continue; }
                if (paths == null) paths = Reachable(topology, core, used, order);
                var neighbors = topology.Neighbors(node.Id); // Endpoints are single-connector leaves.
                if (neighbors.Count == 0 || !paths.TryGetValue(neighbors[0], out var parent))
                { devices.Add(node.Id, new DeviceConnection(DeviceStatus.ChannelExhausted, core)); continue; }
                devices.Add(node.Id, new DeviceConnection(DeviceStatus.Online, core, new RoutePath(node.Id, parent)));
                if (!topology.Channels.Enabled)
                {
                    used[parent.Id]++; // Fold subtree demand once, instead of walking every route.
                    continue;
                }
                for (var cursor = parent; cursor != null; cursor = cursor.Parent)
                {
                    used[cursor.Id]++;
                    if (topology.Nodes[cursor.Id].Part == NetworkPart.Pipeline && used[cursor.Id] == topology.Channels.PipelineCapacity)
                        paths = null; // Only successful saturation invalidates reachability.
                }
            }
            if (!topology.Channels.Enabled)
                for (int i = order.Count - 1; i >= 0; i--)
                    if (order[i].Parent is RoutePath parent) used[parent.Id] += used[order[i].Id];
        }
        ComponentCore = new ReadOnlyDictionary<string, string>(membership);
        NightCosts = new ReadOnlyDictionary<string, int>(costs);
        Devices = new ReadOnlyDictionary<string, DeviceConnection>(devices);
        ChannelsUsed = new ReadOnlyDictionary<string, int>(used);
    }

    public static int NightCost(NetworkPart part) => part == NetworkPart.Core ? 2 : part == NetworkPart.Pipeline || part == NetworkPart.EnergyReceiver ? 0 : 1;

    private Dictionary<string, RoutePath> Reachable(NetworkTopology topology, string core,
        Dictionary<string, int> used, List<RoutePath> order)
    {
        SearchPasses++;
        order.Clear();
        var paths = new Dictionary<string, RoutePath>(StringComparer.Ordinal) { [core] = new RoutePath(core) };
        var queue = new Queue<string>();
        queue.Enqueue(core);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            VisitedTransportNodes++;
            order.Add(paths[current]);
            foreach (var next in topology.Neighbors(current))
            {
                if (topology.Nodes[next].ConsumesChannel || paths.ContainsKey(next)) continue;
                if (topology.Channels.Enabled && topology.Nodes[next].Part == NetworkPart.Pipeline
                    && used[next] >= topology.Channels.PipelineCapacity) continue;
                paths.Add(next, new RoutePath(next, paths[current]));
                queue.Enqueue(next);
            }
        }
        return paths;
    }
}
