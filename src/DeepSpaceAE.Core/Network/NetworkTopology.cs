using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DeepSpaceAE.Core.Network;

public enum NetworkPart { Core, Pipeline, ItemStorageBus, Terminal, DiskDrive, ImportBus, ExportBus, EnergyReceiver }

/// <summary>Logical model node. No grid coordinates, game types or rotation assumptions.</summary>
public sealed class NetworkNode
{
    public string Id { get; }
    public NetworkPart Part { get; }
    public string? ContainerId { get; }
    public bool ConsumesChannel => Part != NetworkPart.Core && Part != NetworkPart.Pipeline;

    public NetworkNode(string id, NetworkPart part, string? containerId = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Node ID is required.", nameof(id));
        if (!Enum.IsDefined(typeof(NetworkPart), part)) throw new ArgumentOutOfRangeException(nameof(part));
        if ((part == NetworkPart.ItemStorageBus || part == NetworkPart.DiskDrive) && string.IsNullOrWhiteSpace(containerId))
            throw new ArgumentException("Storage device needs a model container ID.", nameof(containerId));
        if (part != NetworkPart.ItemStorageBus && part != NetworkPart.DiskDrive && containerId != null)
            throw new ArgumentException("Only storage devices attach containers.", nameof(containerId));
        Id = id;
        Part = part;
        ContainerId = containerId;
    }
}

public sealed class NetworkLink
{
    public string A { get; }
    public string B { get; }

    public NetworkLink(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) || a == b)
            throw new ArgumentException("A link needs two distinct, nonempty node IDs.");
        // Canonical form makes undirected links deterministic.
        A = StringComparer.Ordinal.Compare(a, b) < 0 ? a : b;
        B = A == a ? b : a;
    }
}

public sealed class ChannelSettings
{
    public bool Enabled { get; }
    public int CoreCapacity { get; }
    public int PipelineCapacity { get; }

    public ChannelSettings(bool enabled = true, int coreCapacity = 32, int pipelineCapacity = 8)
    {
        if (coreCapacity < 0) throw new ArgumentOutOfRangeException(nameof(coreCapacity));
        if (pipelineCapacity < 0) throw new ArgumentOutOfRangeException(nameof(pipelineCapacity));
        Enabled = enabled;
        CoreCapacity = coreCapacity;
        PipelineCapacity = pipelineCapacity;
    }
}

/// <summary>Immutable explicit connections; inventory placement belongs to the unverified adapter.</summary>
public sealed class NetworkTopology
{
    private readonly Dictionary<string, IReadOnlyList<string>> adjacent;
    public IReadOnlyDictionary<string, NetworkNode> Nodes { get; }
    public IReadOnlyList<NetworkLink> Links { get; }
    public ChannelSettings Channels { get; }

    public NetworkTopology(IEnumerable<NetworkNode> nodes, IEnumerable<NetworkLink> links, ChannelSettings? channels = null)
    {
        if (nodes is null) throw new ArgumentNullException(nameof(nodes));
        if (links is null) throw new ArgumentNullException(nameof(links));
        var byId = new Dictionary<string, NetworkNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (node is null || !byId.TryAdd(node.Id, node))
                throw new ArgumentException("Null or duplicate node identity.", nameof(nodes));
        }
        var neighbors = byId.Keys.ToDictionary(id => id, _ => new SortedSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var distinctLinks = new List<NetworkLink>();
        foreach (var link in links)
        {
            if (link is null || !byId.ContainsKey(link.A) || !byId.ContainsKey(link.B))
                throw new ArgumentException("Link references an unknown node.", nameof(links));
            if (!neighbors[link.A].Add(link.B)) continue;
            neighbors[link.B].Add(link.A);
            distinctLinks.Add(link);
        }
        // An explicit prototype rule: terminal/bus has one connector and cannot act as a cable.
        if (byId.Values.Any(n => n.ConsumesChannel && neighbors[n.Id].Count > 1))
            throw new ArgumentException("Terminal and storage bus nodes are leaves in this model.", nameof(links));
        adjacent = neighbors.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value.ToArray()), StringComparer.Ordinal);
        Nodes = new ReadOnlyDictionary<string, NetworkNode>(byId);
        Links = Array.AsReadOnly(distinctLinks.OrderBy(l => l.A, StringComparer.Ordinal).ThenBy(l => l.B, StringComparer.Ordinal).ToArray());
        Channels = channels ?? new ChannelSettings();
    }

    public IReadOnlyList<string> Neighbors(string id) => adjacent[id];
}
