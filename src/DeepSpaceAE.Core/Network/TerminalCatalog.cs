using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceAE.Core.Network;

/// <summary>Fresh, read-only view of online buses in this terminal's single-core component.</summary>
public sealed class TerminalCatalog
{
    public NetworkEvaluation Network { get; }
    public DeviceConnection Connection { get; }
    public SnapshotIndex Index { get; }
    public IReadOnlyList<string> MissingContainerIds { get; }

    public TerminalCatalog(NetworkTopology topology, string terminalId, IEnumerable<ContainerSnapshot> snapshots)
    {
        if (topology is null) throw new ArgumentNullException(nameof(topology));
        if (snapshots is null) throw new ArgumentNullException(nameof(snapshots));
        if (!topology.Nodes.TryGetValue(terminalId, out var terminal) || terminal.Part != NetworkPart.Terminal)
            throw new ArgumentException("Select an existing terminal node.", nameof(terminalId));
        Network = new NetworkEvaluation(topology);
        Connection = Network.Devices[terminalId];
        var selected = new Dictionary<string, ContainerSnapshot>(StringComparer.Ordinal);
        var attached = new HashSet<string>(StringComparer.Ordinal);
        if (Connection.Status == DeviceStatus.Online)
        {
            foreach (var node in topology.Nodes.Values.Where(n => n.Part == NetworkPart.ItemStorageBus))
            {
                var bus = Network.Devices[node.Id];
                if (bus.Status == DeviceStatus.Online && bus.CoreId == Connection.CoreId) attached.Add(node.ContainerId!);
            }
            foreach (var snapshot in snapshots)
            {
                if (snapshot is null) throw new ArgumentException("Null container snapshot.", nameof(snapshots));
                if (attached.Contains(snapshot.ContainerId) && !selected.TryAdd(snapshot.ContainerId, snapshot))
                    throw new ArgumentException("Duplicate observed container identity.", nameof(snapshots));
            }
        }
        Index = new SnapshotIndex(selected.Values);
        MissingContainerIds = Array.AsReadOnly(attached.Except(selected.Keys).OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }
}
