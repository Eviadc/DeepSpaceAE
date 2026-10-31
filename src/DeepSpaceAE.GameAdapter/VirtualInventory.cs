using System;
using System.Collections.Generic;
using DeepSpaceAE.Core.Network;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

/// <summary>A native grid background with non-owning display tiles. No item can enter it.</summary>
internal sealed class VirtualInventory : GameGridInventory
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
    private Guid? selected;
    internal VirtualInventory(GameItem owner, int width, int height, bool ghost = false) : base(width, height)
    {
        Owner = owner; Ghost = ghost; SetMachineryStyle();
        rectTransform.anchorMin = rectTransform.anchorMax = rectTransform.pivot = new Vector2(0, 1);
        inner.rectTransform.anchorMin = inner.rectTransform.anchorMax = inner.rectTransform.pivot = new Vector2(0, 1);
        inner.rectTransform.anchoredPosition = Vector2.zero;
    }
    internal void Populate(VirtualInventoryLayout layout, int page, IReadOnlyDictionary<Guid, GameItem> live, IReadOnlyDictionary<Guid, GameInventory> observedSources)
    {
        ClearTiles(); Layout = layout; PageIndex = page; items = live; sources = observedSources;
        SetShape(layout.Width, layout.Height);
        foreach (var p in layout.Page(page)) AddTile(p.Footprint.Instance, p.X, p.Y, live[p.Footprint.Instance]);
    }
    internal void ClearTiles()
    {
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
        var image = TreeNodeRender.Instantiate().SetParent(tile.handler);
        var sprite = (item as GameItemElement)?.ResolveSpriteByName(item.spritePath);
        if (sprite != null) MachineryStyle.SetSimple(image, sprite, Color.white);
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0, 1);
        image.rectTransform.sizeDelta = new Vector2(shape.width * cell, shape.height * cell);
        float flip = shape.flipped ? 1 : 0;
        image.rectTransform.localScale = new Vector3(shape.flipped ? -1 : 1, 1, 1);
        image.rectTransform.pivot = shape.orientation == 0 ? new Vector2(flip, 1) : shape.orientation == 1 ? new Vector2(1 - flip, 1)
            : shape.orientation == 2 ? new Vector2(1 - flip, 0) : new Vector2(flip, 0);
        image.rectTransform.localEulerAngles = 90 * shape.orientation * Vector3.forward;
        image.rectTransform.anchoredPosition = Vector2.zero;
        int minX = int.MaxValue, minY = int.MaxValue;
        for (int ly = 0; ly < shape.height; ly++) for (int lx = 0; lx < shape.width; lx++)
            if (shape.GetLocal(lx, ly) == 1) { var (gx, gy) = shape.ForwardTransform(lx, ly); minX = Math.Min(minX, gx); minY = Math.Min(minY, gy); }
        // Native hit targets follow occupied cells, including holes in irregular items.
        for (int ly = 0; ly < shape.height; ly++) for (int lx = 0; lx < shape.width; lx++)
            if (shape.GetLocal(lx, ly) == 1)
            {
                var (gx, gy) = shape.ForwardTransform(lx, ly);
                var hit = TreeNodeRender.Instantiate().SetParent(tile.handler);
                // An actual occupied-cell hit surface; it remains raycastable when the
                // item's sprite has transparent pixels. Never use a rectangular item clone.
                MachineryStyle.SetFlat(hit, new Color(1, 1, 1, 0.01f));
                hit.rectTransform.anchorMin = hit.rectTransform.anchorMax = hit.rectTransform.pivot = new Vector2(0, 1);
                MachineryStyle.Place(hit, (gx - minX) * cell, (gy - minY) * cell, cell, cell);
                hit.InitEventHandler();
                tile.Hits.Add(hit);
            }
        if (item.unitCount > 1)
        {
            var count = new MachineryLabel(item.unitCount.ToString());
            count.handler.SetParent(tile.handler); count.rectTransform.anchoredPosition = new Vector2(0, -(shape.globalHeight * cell - count.heightPixels));
        }
        tiles.Add(tile.handler);
        itemTiles.Add(id, tile); tile.Select(selected == id);
    }
    internal void Select(Guid? instance)
    {
        if (selected == instance) return;
        if (selected.HasValue && itemTiles.TryGetValue(selected.Value, out var previous)) previous.Select(false);
        selected = instance;
        if (selected.HasValue && itemTiles.TryGetValue(selected.Value, out var next)) next.Select(true);
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
    public override bool SupportsGroupPlacement => false;
    public override bool UncheckedAccept(GameItem item) => false;
    public override bool Expel(GameItem item) => false;
    public override bool MayHaveValidInventorySlot(GameItem item) => item != null;
    public override SlotMarker TryFindOneValidInventorySlot(GameItem item) => new SlotMarker { item = item, inventory = this };
    public override SlotMarker TryInventorySlot(GameItem item, int requestedNum, Vector2 position, GridShape shape, TagSystem state)
    {
        var (x,y) = Cell(position);
        return new SlotMarker { item = item, inventory = this, itemGridShape = shape, index = y * 9 + x,
            numTransfer = x >= 0 && y >= 0 && x < inventoryShape.width && y < inventoryShape.height && requestedNum >= item.unitCount ? item.unitCount : 0 };
    }
    internal sealed class ItemTile : EmptyPixelElement, TooltipListener
    {
        private readonly GameItem item;
        internal readonly VirtualInventory Surface;
        internal readonly Guid Instance;
        internal readonly List<TreeNodeRender> Hits = new();
        internal ItemTile(VirtualInventory surface, Guid instance, GameItem item, int width, int height) : base(width, height)
        { Surface = surface; Instance = instance; this.item = item; }
        internal void Select(bool selected)
        { foreach (var hit in Hits) MachineryStyle.SetFlat(hit, selected ? new Color(0.3f, 0.9f, 0.6f, 0.22f) : new Color(1, 1, 1, 0.01f)); }
        public bool HasTooltip() => true;
        public RichText GetTooltip() => item is GameItemElement element ? element.GetTooltip() : new RichTextBuilder().Add(item.name).Build();
        public RichText GetTooltipAdvanced() => item is GameItemElement element ? element.GetTooltipAdvanced() : GetTooltip();
    }
}
