using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DeepSpaceAE.Core.Network;

public enum DeviceStatus { Online, NoCore, ConflictingCores, Unreachable }

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

/// <summary>One shared reachability tree per network; devices have no capacity limit.</summary>
public sealed class NetworkEvaluation
{
    internal NetworkTopology Topology { get; }
    public IReadOnlyDictionary<string, DeviceConnection> Devices { get; }
    public IReadOnlyDictionary<string, int> EndpointCounts { get; }
    public IReadOnlyDictionary<string, string> ComponentCore { get; }
    public IReadOnlyDictionary<string, int> NightCosts { get; }
    public int SearchPasses { get; private set; }
    public int VisitedTransportNodes { get; private set; }

    public NetworkEvaluation(NetworkTopology topology)
    {
        if (topology is null) throw new ArgumentNullException(nameof(topology));
        Topology = topology;
        var devices = new Dictionary<string, DeviceConnection>(StringComparer.Ordinal);
        var used = topology.Nodes.Values.Where(n => !n.IsEndpoint).ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
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
            var endpoints = component.Where(n => n.IsEndpoint)
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
                if (paths == null) paths = Reachable(topology, core, order);
                var neighbors = topology.Neighbors(node.Id); // Endpoints are single-connector leaves.
                if (neighbors.Count == 0 || !paths.TryGetValue(neighbors[0], out var parent))
                { devices.Add(node.Id, new DeviceConnection(DeviceStatus.Unreachable, core)); continue; }
                devices.Add(node.Id, new DeviceConnection(DeviceStatus.Online, core, new RoutePath(node.Id, parent)));
                used[parent.Id]++; // Fold endpoint counts once, without walking each route.
            }
            for (int i = order.Count - 1; i >= 0; i--)
                if (order[i].Parent is RoutePath parent) used[parent.Id] += used[order[i].Id];
        }
        ComponentCore = new ReadOnlyDictionary<string, string>(membership);
        NightCosts = new ReadOnlyDictionary<string, int>(costs);
        Devices = new ReadOnlyDictionary<string, DeviceConnection>(devices);
        EndpointCounts = new ReadOnlyDictionary<string, int>(used);
    }

    public static int NightCost(NetworkPart part) => part == NetworkPart.Core ? 2 : part == NetworkPart.Pipeline || part == NetworkPart.EnergyReceiver ? 0 : 1;

    private Dictionary<string, RoutePath> Reachable(NetworkTopology topology, string core,
        List<RoutePath> order)
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
                if (topology.Nodes[next].IsEndpoint || paths.ContainsKey(next)) continue;
                paths.Add(next, new RoutePath(next, paths[current]));
                queue.Enqueue(next);
            }
        }
        return paths;
    }
}
