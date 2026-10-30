using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DeepSpaceAE.Core;

public sealed class ItemLocation
{
    public string ContainerId { get; }
    public ItemInstance Item { get; }

    internal ItemLocation(string containerId, ItemInstance item)
    {
        ContainerId = containerId;
        Item = item;
    }
}

/// <summary>Read-only O(n) snapshot build, O(1) instance lookup. Not an authoritative inventory.</summary>
public sealed class SnapshotIndex
{
    public IReadOnlyDictionary<Guid, ItemLocation> ByInstance { get; }

    public SnapshotIndex(IEnumerable<ContainerSnapshot> containers)
    {
        if (containers is null) throw new ArgumentNullException(nameof(containers));
        var entries = new Dictionary<Guid, ItemLocation>();
        var containerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var container in containers)
        {
            if (container is null) throw new ArgumentException("Null container.", nameof(containers));
            if (!containerIds.Add(container.ContainerId))
                throw new ArgumentException("Duplicate container identity.", nameof(containers));
            foreach (var item in container.Items)
            {
                if (!entries.TryAdd(item.InstanceId, new ItemLocation(container.ContainerId, item)))
                    throw new ArgumentException("An instance was observed more than once; refusing ambiguous index.", nameof(containers));
            }
        }
        ByInstance = new ReadOnlyDictionary<Guid, ItemLocation>(entries);
    }
}
