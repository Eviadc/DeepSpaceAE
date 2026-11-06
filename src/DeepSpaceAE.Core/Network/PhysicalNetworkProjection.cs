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
        var neighbors = parts.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        var transportEdges = new List<NetworkLink>();
        foreach (var edge in edges)
        {
            neighbors[edge.A].Add(edge.B);
            neighbors[edge.B].Add(edge.A);
            if (!parts[edge.A].ConsumesChannel && !parts[edge.B].ConsumesChannel) transportEdges.Add(edge);
        }
        var component = new Dictionary<string, int>(StringComparer.Ordinal);
        var componentId = 0;
        foreach (var start in parts.Values.Where(n => !n.ConsumesChannel).Select(n => n.Id))
        {
            if (component.ContainsKey(start)) continue;
            var queue = new Queue<string>();
            component.Add(start, componentId);
            queue.Enqueue(start);
            while (queue.Count > 0)
                foreach (var next in neighbors[queue.Dequeue()])
                    if (!parts[next].ConsumesChannel && component.TryAdd(next, componentId)) queue.Enqueue(next);
            componentId++;
        }
        foreach (var endpoint in parts.Values.Where(n => n.ConsumesChannel))
        {
            string? selected = null;
            int? contactComponent = null;
            bool ambiguous = false;
            foreach (var neighbor in neighbors[endpoint.Id])
            {
                if (!component.TryGetValue(neighbor, out var current)) continue;
                if (contactComponent.HasValue && contactComponent != current) { ambiguous = true; break; }
                contactComponent = current;
                if (selected == null || StringComparer.Ordinal.Compare(neighbor, selected) < 0) selected = neighbor;
            }
            // Ambiguous physical contact with different transport components is offline, not a bridge.
            if (!ambiguous && selected != null) transportEdges.Add(new NetworkLink(endpoint.Id, selected));
        }
        return new NetworkTopology(parts.Values, transportEdges, channels);
    }
}
