using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceAE.Core.Network;

public enum StorageAccess { ReadWrite, ExtractOnly, InsertOnly }

/// <summary>Ghost filters select types/tags, never combine item instances.</summary>
public sealed class StoragePolicy
{
    public int Priority { get; }
    public StorageAccess Access { get; }
    public bool Tags { get; }
    public bool Exclude { get; }
    public bool FilterExtraction { get; }
    public int Slots { get; }
    public IReadOnlyCollection<string> Keys { get; }
    private readonly HashSet<string> keys;
    public StoragePolicy(int priority, StorageAccess access, bool tags, bool exclude, bool filterExtraction,
        int capacityCards, IEnumerable<string> filters)
    {
        if (capacityCards < 0 || capacityCards > (tags ? 4 : 5)) throw new ArgumentOutOfRangeException(nameof(capacityCards));
        Priority = priority; Access = access; Tags = tags; Exclude = exclude; FilterExtraction = filterExtraction;
        Slots = 18 + 9 * capacityCards;
        keys = new HashSet<string>(filters.Take(Slots).Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.Ordinal);
        Keys = Array.AsReadOnly(keys.ToArray());
    }
    public bool Matches(string id, IEnumerable<string> tags)
    {
        if (keys.Count == 0) return true;
        bool match = Tags ? tags.Any(keys.Contains) : keys.Contains(id);
        return Exclude ? !match : match;
    }
    public bool CanInsert(string id, IEnumerable<string> tags) => Access != StorageAccess.ExtractOnly && Matches(id, tags);
    public bool CanExtract(string id, IEnumerable<string> tags) => Access != StorageAccess.InsertOnly && (!FilterExtraction || Matches(id, tags));
}

/// <summary>Compile the union once per container. Query visits matching keys, not every bus.</summary>
public sealed class ExtractionFilter
{
    private readonly HashSet<string> ids = new(StringComparer.Ordinal), tags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong[]> blockedIds = new(StringComparer.Ordinal), blockedTags = new(StringComparer.Ordinal);
    private readonly ulong[] scratch;
    private readonly bool all;
    private readonly int exclusions;
    public ExtractionFilter(IEnumerable<StoragePolicy> policies)
    {
        var negative = new List<StoragePolicy>();
        foreach (var p in policies)
        {
            if (p.Access == StorageAccess.InsertOnly) continue;
            if (!p.FilterExtraction || p.Keys.Count == 0) { all = true; break; }
            if (!p.Exclude) { foreach (var key in p.Keys) (p.Tags ? tags : ids).Add(key); }
            else negative.Add(p);
        }
        exclusions = negative.Count; scratch = new ulong[(exclusions + 63) / 64];
        for (int n = 0; n < negative.Count; n++)
        {
            var p = negative[n]; var index = p.Tags ? blockedTags : blockedIds;
            foreach (var key in p.Keys)
            {
                if (!index.TryGetValue(key, out var mask)) index.Add(key, mask = new ulong[scratch.Length]);
                mask[n / 64] |= 1UL << (n % 64);
            }
        }
    }
    // Main-thread observation object, not shared across concurrent writers.
    // One machine word suffices for the game's default 32-channel network.
    public bool Allows(string id, IReadOnlyCollection<string> itemTags)
    {
        if (all || ids.Contains(id)) return true;
        foreach (var tag in itemTags) if (tags.Contains(tag)) return true;
        if (exclusions == 0) return false;
        Array.Clear(scratch, 0, scratch.Length);
        void Mark(Dictionary<string, ulong[]> index, string key)
        {
            if (!index.TryGetValue(key, out var mask)) return;
            for (int word = 0; word < mask.Length; word++) scratch[word] |= mask[word];
        }
        Mark(blockedIds, id);
        foreach (var tag in itemTags) Mark(blockedTags, tag);
        for (int word = 0; word < scratch.Length; word++)
        {
            int bits = Math.Min(64, exclusions - word * 64);
            ulong full = bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;
            if (scratch[word] != full) return true; // At least one bus permits this instance.
        }
        return false;
    }
}

public static class StorageRouting
{
    public static IReadOnlyList<string> InsertionOrder(IReadOnlyDictionary<string, StoragePolicy> policies,
        string itemId, IReadOnlyCollection<string> tags) => policies.Where(p => p.Value.CanInsert(itemId, tags))
        .OrderByDescending(p => p.Value.Priority).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key).ToArray();
}
