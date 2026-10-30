using System;
using System.Collections.Generic;
using DeepSpaceAE.Core;

namespace DeepSpaceAE.Tests.Simulation;

// TEST-ONLY. Capacity counts instances, not grid cells. Single-threaded, no game callbacks.
internal sealed class InMemoryContainer
{
    private readonly Dictionary<Guid, ItemInstance> items = new();
    private readonly int capacity;
    private readonly Func<ItemInstance, bool> accepts;
    public string Id { get; }
    public Guid? FailBeforeInsert { get; set; }
    public int Count => items.Count;

    public InMemoryContainer(string id, int capacity, Func<ItemInstance, bool>? accepts = null)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Id = id;
        this.capacity = capacity;
        this.accepts = accepts ?? (_ => true);
    }

    public void Seed(ItemInstance item)
    {
        if (items.Count >= capacity) throw new InvalidOperationException("Test seed exceeds capacity.");
        items.Add(item.InstanceId, item);
    }

    public bool Contains(Guid id) => items.ContainsKey(id);
    public ContainerSnapshot Snapshot() => new(Id, items.Values);

    public MoveOutcome MoveOneTo(InMemoryContainer destination, Guid instanceId)
    {
        if (ReferenceEquals(this, destination)) return MoveOutcome.SameContainer;
        if (!items.TryGetValue(instanceId, out var item)) return MoveOutcome.Missing;
        if (destination.items.ContainsKey(instanceId)) return MoveOutcome.DuplicateInstance;
        if (!destination.accepts(item)) return MoveOutcome.Rejected;
        if (destination.items.Count >= destination.capacity) return MoveOutcome.Full;
        if (destination.FailBeforeInsert == instanceId) return MoveOutcome.InjectedFailure;

        // Only the fake's synchronous dictionaries have these semantics. No cross-game transaction implied.
        destination.items.Add(instanceId, item);
        items.Remove(instanceId);
        return MoveOutcome.Moved;
    }
}

internal enum MoveOutcome { Moved, Missing, Full, Rejected, InjectedFailure, DuplicateInstance, SameContainer }
