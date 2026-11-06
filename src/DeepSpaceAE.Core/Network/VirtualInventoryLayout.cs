using System;
using System.Collections.Generic;
using System.Linq;
namespace DeepSpaceAE.Core.Network;

/// <summary>Display footprint only. Never owns, clones, rotates or moves a stored item.</summary>
public sealed class DisplayFootprint
{
    public Guid Instance { get; }
    public IReadOnlyList<GridCell> Cells { get; }
    public int Width { get; }
    public int Height { get; }
    public DisplayFootprint(Guid instance, IEnumerable<GridCell> cells)
    {
        Instance = instance;
        var copy = cells.Distinct().ToArray();
        if (copy.Length == 0 || copy.Any(c => c.X < 0 || c.Y < 0)) throw new ArgumentException("A normalized occupied footprint is required.");
        Width = checked(copy.Max(c => c.X) + 1); Height = checked(copy.Max(c => c.Y) + 1);
        Cells = Array.AsReadOnly(copy);
    }
}
public sealed class DisplayPlacement
{
    public DisplayFootprint Footprint { get; }
    public int Page { get; internal set; }
    internal int PageOffset;
    public int X { get; internal set; }
    public int Y { get; internal set; }
    internal DisplayPlacement(DisplayFootprint footprint, int page, int x, int y) { Footprint = footprint; Page = page; X = x; Y = y; }
}
/// <summary>Linear shelf packing; no optimal bin-packing search or pairwise item comparison.</summary>
public sealed class VirtualInventoryLayout
{
    public int Width { get; }
    public int Height { get; }
    public int PageCount => pages.Count;
    private readonly List<List<DisplayPlacement>> pages = new();
    private readonly Dictionary<Guid, DisplayPlacement> byInstance = new();
    private readonly Dictionary<(int Page, int X, int Y), Guid> occupied = new();
    public VirtualInventoryLayout(IEnumerable<DisplayFootprint> footprints, int width = 18, int height = 12, bool continuous = false)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
        var inputs = footprints.ToArray();
        Width = Math.Max(width, inputs.Length == 0 ? 1 : inputs.Max(f => f.Width));
        Height = Math.Max(height, inputs.Length == 0 ? 1 : inputs.Max(f => f.Height));
        if (continuous)
        {
            int cx = 0, cy = 0, ch = 0;
            foreach (var f in inputs)
            {
                if (cx + f.Width > Width) { cx = 0; cy = checked(cy + ch); ch = 0; }
                cx += f.Width; ch = Math.Max(ch, f.Height);
            }
            Height = Math.Max(Height, checked(cy + ch));
        }
        int page = 0, x = 0, y = 0, shelfHeight = 0;
        pages.Add(new List<DisplayPlacement>());
        foreach (var footprint in inputs)
        {
            if (x + footprint.Width > Width) { x = 0; y += shelfHeight; shelfHeight = 0; }
            if (y + footprint.Height > Height) { page++; x = y = shelfHeight = 0; pages.Add(new List<DisplayPlacement>()); }
            var placement = new DisplayPlacement(footprint, page, x, y) { PageOffset = pages[page].Count };
            byInstance.Add(footprint.Instance, placement); pages[page].Add(placement);
            foreach (var c in footprint.Cells) occupied.Add((page, x + c.X, y + c.Y), footprint.Instance);
            x += footprint.Width; shelfHeight = Math.Max(shelfHeight, footprint.Height);
        }
    }
    public IReadOnlyList<DisplayPlacement> Page(int page) => pages[page].AsReadOnly();
    public Guid? At(int page, int x, int y) => occupied.TryGetValue((page, x, y), out var id) ? id : (Guid?)null;
    // Query only occupied cells in the viewport; work is independent of total item count.
    public IReadOnlyList<DisplayPlacement> Visible(int top, int rows)
    {
        var found = new HashSet<Guid>(); var result = new List<DisplayPlacement>();
        int end = (int)Math.Min(Height, (long)top + rows);
        for (int y = Math.Max(0, top); y < end; y++) for (int x = 0; x < Width; x++)
            if (occupied.TryGetValue((0, x, y), out var id) && found.Add(id)) result.Add(byInstance[id]);
        return result.AsReadOnly();
    }
    public bool Reposition(Guid id, int page, int x, int y)
    {
        if (!byInstance.TryGetValue(id, out var p) || page < 0 || page >= pages.Count || x < 0 || y < 0
            || x + p.Footprint.Width > Width || y + p.Footprint.Height > Height) return false;
        foreach (var c in p.Footprint.Cells)
            if (occupied.TryGetValue((page, x + c.X, y + c.Y), out var other) && other != id) return false;
        foreach (var c in p.Footprint.Cells) occupied.Remove((p.Page, p.X + c.X, p.Y + c.Y));
        if (p.Page != page)
        {
            var old = pages[p.Page]; var last = old[old.Count - 1];
            old[p.PageOffset] = last; last.PageOffset = p.PageOffset; old.RemoveAt(old.Count - 1);
            p.PageOffset = pages[page].Count; pages[page].Add(p); p.Page = page;
        }
        p.X = x; p.Y = y;
        foreach (var c in p.Footprint.Cells) occupied.Add((page, x + c.X, y + c.Y), id);
        return true;
    }
}
