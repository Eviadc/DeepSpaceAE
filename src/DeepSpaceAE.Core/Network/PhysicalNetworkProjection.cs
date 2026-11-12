using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceAE.Core.Network;

/// <summary>Preserves connections through standalone devices; mounted buses remain leaves.</summary>
public static class PhysicalNetworkProjection
{
    public static NetworkTopology Build(IEnumerable<NetworkNode> nodes, IEnumerable<NetworkLink> adjacency)
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
            if (parts[edge.A].CanRelay && parts[edge.B].CanRelay) transportEdges.Add(edge);
        }
        var component = new Dictionary<string, int>(StringComparer.Ordinal);
        var componentId = 0;
        foreach (var start in parts.Values.Where(n => n.CanRelay).Select(n => n.Id))
        {
            if (component.ContainsKey(start)) continue;
            var queue = new Queue<string>();
            component.Add(start, componentId);
            queue.Enqueue(start);
            while (queue.Count > 0)
                foreach (var next in neighbors[queue.Dequeue()])
                    if (parts[next].CanRelay && component.TryAdd(next, componentId)) queue.Enqueue(next);
            componentId++;
        }
        var coreComponents = new HashSet<int>(parts.Values.Where(n => n.Part == NetworkPart.Core).Select(n => component[n.Id]));
        foreach (var endpoint in parts.Values.Where(n => !n.CanRelay))
        {
            // Only mounted buses need one attachment. Standalone devices already
            // belong to the transport graph, including all of their physical links.
            bool touchesCore = neighbors[endpoint.Id].Any(n => component.TryGetValue(n, out var c) && coreComponents.Contains(c));
            string? selected = null;
            int? contactComponent = null;
            bool ambiguous = false;
            foreach (var neighbor in neighbors[endpoint.Id])
            {
                if (!component.TryGetValue(neighbor, out var current)) continue;
                if (touchesCore && !coreComponents.Contains(current)) continue;
                if (contactComponent.HasValue && contactComponent != current) { ambiguous = true; break; }
                contactComponent = current;
                if (selected == null || StringComparer.Ordinal.Compare(neighbor, selected) < 0) selected = neighbor;
            }
            // Ambiguous physical contact with different transport components is offline, not a bridge.
            if (!ambiguous && selected != null) transportEdges.Add(new NetworkLink(endpoint.Id, selected));
        }
        return new NetworkTopology(parts.Values, transportEdges);
    }
}
