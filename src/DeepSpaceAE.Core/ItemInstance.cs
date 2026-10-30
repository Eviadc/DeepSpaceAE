using System;

namespace DeepSpaceAE.Core;

/// <summary>Model identity, not a game object or a proven persistent game identifier.</summary>
public sealed class ItemInstance
{
    public Guid InstanceId { get; }
    public string ItemId { get; }
    // Opaque model data; never use this tag alone to infer real-game stackability.
    public string StateTag { get; }

    public ItemInstance(Guid instanceId, string itemId, string stateTag)
    {
        if (instanceId == Guid.Empty) throw new ArgumentException("Instance ID must be nonempty.", nameof(instanceId));
        if (string.IsNullOrWhiteSpace(itemId)) throw new ArgumentException("Item ID must be nonempty.", nameof(itemId));
        InstanceId = instanceId;
        ItemId = itemId;
        StateTag = stateTag ?? throw new ArgumentNullException(nameof(stateTag));
    }
}
