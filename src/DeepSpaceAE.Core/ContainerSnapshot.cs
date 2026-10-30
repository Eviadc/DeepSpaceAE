using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceAE.Core;

/// <summary>A copied observation, without coordinates, mutation methods, or game references.</summary>
public sealed class ContainerSnapshot
{
    public string ContainerId { get; }
    public IReadOnlyList<ItemInstance> Items { get; }

    public ContainerSnapshot(string containerId, IEnumerable<ItemInstance> items)
    {
        if (string.IsNullOrWhiteSpace(containerId)) throw new ArgumentException("Container ID must be nonempty.", nameof(containerId));
        if (items is null) throw new ArgumentNullException(nameof(items));
        var copy = items.ToArray();
        if (copy.Any(item => item is null)) throw new ArgumentException("Null item in snapshot.", nameof(items));
        ContainerId = containerId;
        Items = Array.AsReadOnly(copy);
    }
}
