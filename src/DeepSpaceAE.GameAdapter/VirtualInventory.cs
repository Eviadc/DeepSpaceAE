using System;
using System.Collections.Generic;
using DeepSpaceAE.Core.Network;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

/// <summary>A native grid background with non-owning display tiles. No item can enter it.</summary>
internal sealed class VirtualInventory : GameGridScrollableInventory
{
    internal readonly GameItem Owner;
    internal readonly bool Ghost;
    internal int PageIndex;
    internal VirtualInventoryLayout? Layout;
    internal Action<int, GameItem>? ConfigureFilter;
    internal Action? Redraw;
    private IReadOnlyDictionary<Guid, GameItem> items = new Dictionary<Guid, GameItem>();
    private IReadOnlyDictionary<Guid, GameInventory> sources = new Dictionary<Guid, GameInventory>();
    private readonly List<TreeNodeRender> tiles = new();
    private readonly Dictionary<Guid, ItemTile> itemTiles = new();
    internal VirtualInventory(GameItem owner, int width, int height, bool ghost = false) : base(width, height, height, 0)
    {
        Owner = owner; Ghost = ghost;
        rectTransform.anchorMin = rectTransform.anchorMax = rectTransform.pivot = new Vector2(0, 1);
        inner.rectTransform.anchorMin = inner.rectTransform.anchorMax = inner.rectTransform.pivot = new Vector2(0, 1);
        inner.rectTransform.anchoredPosition = Vector2.zero;
    }
    internal void Populate(VirtualInventoryLayout layout, int page, IReadOnlyDictionary<Guid, GameItem> live, IReadOnlyDictionary<Guid, GameInventory> observedSources)
    {
        ClearTiles(); Layout = layout; PageIndex = page; items = live; sources = observedSources;
        SetShape(layout.Width, 12, Math.Max(13, layout.Height));
        RefreshVisible();
    }
    private bool visibilityQueued;
    internal void QueueVisibility()
    {
        if (Ghost || visibilityQueued) return;
        visibilityQueued = true;
        LetterboxManager.RunNextUpdate(() => {
            visibilityQueued = false;
            if (handler != null && handler.gameObject.activeInHierarchy) RefreshVisible();
        });
    }
    internal void RefreshVisible()
    {
        if (Ghost || Layout == null || ItemMouseDragHandler.current?.IsDraggingItem == true
            || ItemMultiSelectHandler.current?.IsGroupDragging == true) return;
        int top = (int)Math.Round(inner.rectTransform.anchoredPosition.y / UISettings.current.gridSize);
        var visible = Layout.Visible(top, GetVisibleHeight() + 1);
        var keep = new HashSet<Guid>();
        foreach (var placement in visible) keep.Add(placement.Footprint.Instance);
        var removed = new List<Guid>();
        foreach (var entry in itemTiles) if (!keep.Contains(entry.Key)) removed.Add(entry.Key);
        foreach (var id in removed)
        {
            var tile = itemTiles[id]; tile.DisposeView();
            tile.handler.gameObject.SetActive(false); UnityEngine.Object.Destroy(tile.handler.gameObject);
            tiles.Remove(tile.handler); itemTiles.Remove(id);
        }
        foreach (var placement in visible)
            if (!itemTiles.ContainsKey(placement.Footprint.Instance))
                AddTile(placement.Footprint.Instance, placement.X, placement.Y, items[placement.Footprint.Instance]);
    }
    internal void ClearTiles()
    {
        foreach (var tile in itemTiles.Values) tile.DisposeView();
        foreach (var tile in tiles) { tile.gameObject.SetActive(false); UnityEngine.Object.Destroy(tile.gameObject); }
        tiles.Clear();
        itemTiles.Clear();
    }
    private void AddTile(Guid id, int x, int y, GameItem item)
    {
        int cell = UISettings.current.gridSize;
        var shape = item.modifiedShape;
        var tile = new ItemTile(this, id, item, shape.globalWidth * cell, shape.globalHeight * cell);
        tile.handler.SetParent(inner);
        tile.rectTransform.anchorMin = tile.rectTransform.anchorMax = tile.rectTransform.pivot = new Vector2(0, 1);
        MachineryStyle.Place(tile.handler, x * cell, y * cell, shape.globalWidth * cell, shape.globalHeight * cell);
        // Independent native UI nodes resolve to the one real item. The source renderer is untouched.
        tiles.Add(tile.handler);
        itemTiles.Add(id, tile);
        tile.CreateView();
    }
    internal void GhostTile(int index, Sprite? sprite, string description, Action clear)
    {
        int cell = UISettings.current.gridSize;
        var button = new MachineryButton("", clear, cell, cell);
        if (sprite != null) { var image = TreeNodeRender.Instantiate().SetParent(button.handler); MachineryStyle.SetSimple(image, sprite, Color.white); MachineryStyle.Place(image, 1, 1, cell - 2, cell - 2); }
        button.SetDescription(description);
        button.handler.SetParent(inner);
        MachineryStyle.Place(button.handler, index % 9 * cell, index / 9 * cell, cell, cell);
        tiles.Add(button.handler);
    }
    internal (int X, int Y) Cell(Vector2 position)
    {
        var local = inner.rectTransform.InverseTransformPoint(position);
        int cell = UISettings.current.gridSize;
        return ((int)Math.Floor(local.x / cell), (int)Math.Floor(-local.y / cell));
    }
    internal GameItem? At(Vector2 position)
    {
        if (Ghost || Layout == null) return null;
        var (x, y) = Cell(position); var id = Layout.At(PageIndex, x, y);
        return id.HasValue && items.TryGetValue(id.Value, out var item) ? item : null;
    }
    internal GameInventory? SourceAt(Vector2 position)
    {
        if (Layout == null) return null;
        var (x, y) = Cell(position); var id = Layout.At(PageIndex, x, y);
        return id.HasValue && sources.TryGetValue(id.Value, out var source) ? source : null;
    }
    // False selects native sequential bulk insertion, not spatial formation placement.
    public override bool SupportsGroupPlacement => false;
    public override bool UncheckedAccept(GameItem item) => false;
    public override bool Expel(GameItem item) => false;
    public override bool MayHaveValidInventorySlot(GameItem item) => item != null;
    public override SlotMarker TryFindOneValidInventorySlot(GameItem item) => new SlotMarker { item = item, inventory = this, numTransfer = Ghost ? 0 : item.unitCount };
    public override SlotMarker TryInventorySlot(GameItem item, int requestedNum, Vector2 position, GridShape shape, TagSystem state)
    {
        var (x,y) = Cell(position);
        return new SlotMarker { item = item, inventory = this, itemGridShape = shape, index = y * 9 + x,
            numTransfer = x >= 0 && y >= 0 && x < width && y < height && requestedNum >= item.unitCount ? item.unitCount : 0 };
    }
    internal sealed class ItemTile : EmptyPixelElement, TooltipListener
    {
        private readonly GameItem item;
        private static readonly Dictionary<GameItemElement, ItemTile> views = new();
        private TreeNodeRender? mirror;
        private bool queued, disposed;
        internal void CreateView()
        {
            if (item is not GameItemElement element) return;
            views.Add(element, this);
            Rebuild(element);
        }
        internal static void Changed(GameItemElement element)
        {
            if (!views.TryGetValue(element, out var tile) || tile.queued || tile.disposed) return;
            tile.queued = true;
            LetterboxManager.RunNextUpdate(() => {
                tile.queued = false;
                if (!tile.disposed && tile.handler != null && !element.IsDestroyed()) tile.Rebuild(element);
            });
        }
        private void Rebuild(GameItemElement element)
        {
            // Instantiate only native render prefabs. No GameItem constructor, Unity object
            // clone, source callbacks or source transform mutation is involved.
            if (mirror == null) mirror = NativeItemMirror.Copy(element.middle, handler, element);
            else NativeItemMirror.Sync(element.middle, mirror);
            mirror.rectTransform.anchoredPosition = Vector2.zero;
        }
        internal void DisposeView()
        {
            disposed = true;
            if (item is GameItemElement element) views.Remove(element);
            // The enclosing layout owns only mirror nodes. Nothing needs to be returned.
        }
        internal readonly VirtualInventory Surface;
        internal readonly Guid Instance;
        internal ItemTile(VirtualInventory surface, Guid instance, GameItem item, int width, int height) : base(width, height)
        { Surface = surface; Instance = instance; this.item = item; }
        public bool HasTooltip() => true;
        public RichText GetTooltip() => item is GameItemElement element ? element.GetTooltip() : new RichTextBuilder().Add(item.name).Build();
        public RichText GetTooltipAdvanced() => item is GameItemElement element ? element.GetTooltipAdvanced() : GetTooltip();
    }
}
