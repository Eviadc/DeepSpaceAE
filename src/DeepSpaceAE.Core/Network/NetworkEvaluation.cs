using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DeepSpaceAE.Core.Network;

public enum DeviceStatus { Online, NoCore, ConflictingCores, ChannelExhausted }

public sealed class DeviceConnection
{
    public DeviceStatus Status { get; }
    public string? CoreId { get; }
    public IReadOnlyList<string> Route { get; }

    internal DeviceConnection(DeviceStatus status, string? coreId = null, string[]? route = null)
    {
        Status = status;
        CoreId = coreId;
        Route = Array.AsReadOnly(route ?? Array.Empty<string>());
    }
}

/// <summary>Deterministic greedy routing with residual per-core/per-pipeline channel budgets.</summary>
public sealed class NetworkEvaluation
{
    public IReadOnlyDictionary<string, DeviceConnection> Devices { get; }
    public IReadOnlyDictionary<string, int> ChannelsUsed { get; }

    public NetworkEvaluation(NetworkTopology topology)
    {
        if (topology is null) throw new ArgumentNullException(nameof(topology));
        var devices = new Dictionary<string, DeviceConnection>(StringComparer.Ordinal);
        var used = topology.Nodes.Values.Where(n => !n.ConsumesChannel).ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in topology.Nodes.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!visited.Add(start)) continue;
            var component = new List<NetworkNode>();
            var queue = new Queue<string>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                component.Add(topology.Nodes[id]);
                foreach (var neighbor in topology.Neighbors(id))
                    if (visited.Add(neighbor)) queue.Enqueue(neighbor);
            }
            var cores = component.Where(n => n.Part == NetworkPart.Core).ToArray();
            // Reserve terminals before buses; stable ordinal IDs decide contention within each kind.
            foreach (var node in component.Where(n => n.ConsumesChannel)
                .OrderBy(n => n.Part == NetworkPart.Terminal ? 0 : 1).ThenBy(n => n.Id, StringComparer.Ordinal))
            {
                if (cores.Length != 1)
                {
                    devices.Add(node.Id, new DeviceConnection(cores.Length == 0 ? DeviceStatus.NoCore : DeviceStatus.ConflictingCores));
                    continue;
                }
                var route = FindRoute(topology, cores[0].Id, node.Id, used);
                if (route is null)
                {
                    devices.Add(node.Id, new DeviceConnection(DeviceStatus.ChannelExhausted, cores[0].Id));
                    continue;
                }
                foreach (var id in route)
                    if (used.ContainsKey(id)) used[id]++;
                devices.Add(node.Id, new DeviceConnection(DeviceStatus.Online, cores[0].Id, route));
            }
        }
        Devices = new ReadOnlyDictionary<string, DeviceConnection>(devices);
        ChannelsUsed = new ReadOnlyDictionary<string, int>(used);
    }

    private static string[]? FindRoute(NetworkTopology topology, string coreId, string target, Dictionary<string, int> used)
    {
        if (topology.Channels.Enabled && used[coreId] >= topology.Channels.CoreCapacity) return null;
        var queue = new Queue<string>();
        var previous = new Dictionary<string, string?>(StringComparer.Ordinal) { [coreId] = null };
        queue.Enqueue(coreId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == target)
            {
                var route = new List<string>();
                for (string? id = target; id != null; id = previous[id]) route.Add(id);
                route.Reverse();
                return route.ToArray();
            }
            if (topology.Nodes[current].ConsumesChannel) continue;
            foreach (var next in topology.Neighbors(current))
            {
                if (previous.ContainsKey(next)) continue;
                if (topology.Channels.Enabled && topology.Nodes[next].Part == NetworkPart.Pipeline
                    && used[next] >= topology.Channels.PipelineCapacity) continue;
                previous.Add(next, current);
                queue.Enqueue(next);
            }
        }
        return null;
    }
}
