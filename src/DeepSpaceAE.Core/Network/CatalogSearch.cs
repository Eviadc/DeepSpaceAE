using System;
using System.Collections.Generic;
using System.Linq;
namespace DeepSpaceAE.Core.Network;
public enum CatalogSort { Native, Name, Quantity }
public sealed class CatalogRecord
{
    public Guid Instance { get; }
    public string ItemId { get; }
    public string Name { get; }
    public IReadOnlyCollection<string> Tags { get; }
    internal IReadOnlyCollection<string> SearchTags { get; }
    public int Quantity { get; }
    internal string Text { get; }
    public CatalogRecord(Guid instance, string itemId, string name, IEnumerable<string> tags, int quantity = 1, IEnumerable<string>? tagNames = null)
    { Quantity = quantity; Instance = instance; ItemId = itemId; Name = name; Tags = Array.AsReadOnly(tags.Distinct(StringComparer.Ordinal).ToArray()); Text = name + " " + itemId; SearchTags = Array.AsReadOnly(Tags.Concat(tagNames ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray()); }
}
public sealed class CatalogSearch
{
    private readonly CatalogRecord[] rows;
    private readonly bool aggregateQuantity;
    private readonly Dictionary<string, List<CatalogRecord>> byTag = new(StringComparer.Ordinal);
    public IReadOnlyList<string> Tags { get; }
    public CatalogSearch(IEnumerable<CatalogRecord> records,bool aggregateQuantity = true)
    {
        this.aggregateQuantity=aggregateQuantity;
        rows = records.ToArray();
        foreach (var row in rows) foreach (var tag in row.Tags)
        {
            if (!byTag.TryGetValue(tag, out var list)) byTag.Add(tag, list = new List<CatalogRecord>());
            list.Add(row);
        }
        Tags = Array.AsReadOnly(byTag.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }
    private string? cachedQuery, cachedTag;
    private IReadOnlyList<CatalogRecord>? cachedResult;
    private CatalogSort cachedSort;
    public IReadOnlyList<CatalogRecord> Find(string query, string? tag = null, CatalogSort sort = CatalogSort.Native)
    {
        if (cachedResult != null && cachedQuery == query && cachedTag == tag && cachedSort == sort) return cachedResult;
        IEnumerable<CatalogRecord> candidates = tag == null ? rows : byTag.TryGetValue(tag, out var list) ? list : Array.Empty<CatalogRecord>();
        var words = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        cachedQuery = query; cachedTag = tag; cachedSort = sort;
        candidates = candidates.Where(r => words.All(w => w.StartsWith("#", StringComparison.Ordinal)
            ? r.SearchTags.Any(t => t.IndexOf(w.Substring(1), StringComparison.OrdinalIgnoreCase) >= 0)
            : r.Text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0));
        if (sort == CatalogSort.Name) candidates = candidates.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Instance);
        if (sort == CatalogSort.Quantity)
        {
            var matches = candidates.ToArray();
            if(aggregateQuantity)
            {
                var totals = matches.GroupBy(r => r.ItemId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(r => (long)r.Quantity), StringComparer.Ordinal);
                candidates = matches.OrderByDescending(r => totals[r.ItemId]).ThenBy(r => r.ItemId, StringComparer.Ordinal).ThenByDescending(r => r.Quantity);
            }
            else candidates=matches.OrderByDescending(r=>r.Quantity).ThenBy(r=>r.ItemId,StringComparer.Ordinal);
        }
        return cachedResult = Array.AsReadOnly(candidates.ToArray());
    }
}
