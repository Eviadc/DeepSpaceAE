using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceAE.Core.Network;

/// <summary>Projects observed physical adjacency without letting leaf devices bridge networks.</summary>
public static class PhysicalNetworkProjection
{
    public static NetworkTopology Build(IEnumerable<NetworkNode> nodes, IEnumerable<NetworkLink> adjacency, ChannelSettings channels)
    {
        var parts = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var edges = adjacency.ToArray();
        if (edges.Any(e => !parts.ContainsKey(e.A) || !parts.ContainsKey(e.B)))
            throw new ArgumentException("Observed edge references an unknown node.", nameof(adjacency));
        var transportEdges = edges.Where(e => !parts[e.A].ConsumesChannel && !parts[e.B].ConsumesChannel).ToList();
        var transport = new NetworkTopology(parts.Values.Where(n => !n.ConsumesChannel), transportEdges, channels);
        var component = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var start in transport.Nodes.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (component.ContainsKey(start)) continue;
            var queue = new Queue<string>();
            component.Add(start, start);
            queue.Enqueue(start);
            while (queue.Count > 0)
                foreach (var next in transport.Neighbors(queue.Dequeue()))
                    if (component.TryAdd(next, start)) queue.Enqueue(next);
        }
        foreach (var endpoint in parts.Values.Where(n => n.ConsumesChannel))
        {
            var neighbors = edges.Where(e => e.A == endpoint.Id || e.B == endpoint.Id)
                .Select(e => e.A == endpoint.Id ? e.B : e.A).Where(id => component.ContainsKey(id))
                .Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray();
            // Ambiguous physical contact with different transport components is offline, not a bridge.
            if (neighbors.Length > 0 && neighbors.Select(id => component[id]).Distinct().Count() == 1)
                transportEdges.Add(new NetworkLink(endpoint.Id, neighbors[0]));
        }
        return new NetworkTopology(parts.Values, transportEdges, channels);
    }
}
