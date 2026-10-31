using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using DeepSpaceAE.Core.Network;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

/// <summary>A native grid background with non-owning display tiles. No item can enter it.</summary>
internal sealed class VirtualInventory : GameGridScrollableInventory
{
    internal readonly GameItem Owner;
    internal int PageIndex;
    internal VirtualInventoryLayout? Layout;
    internal Action? Redraw;
    private IReadOnlyDictionary<Guid, GameItem> items = new Dictionary<Guid, GameItem>();
    private IReadOnlyDictionary<Guid, GameInventory> sources = new Dictionary<Guid, GameInventory>();
    private readonly List<TreeNodeRender> tiles = new();
    private readonly Dictionary<Guid, ItemTile> itemTiles = new();
    private IReadOnlyDictionary<Guid,LiquidSnapshot>? liquids;
    internal bool IsLiquid=>liquids!=null;
    internal VirtualInventory(GameItem owner, int width, int height) : base(width, height, height, 0)
    {
        Owner = owner;
        NativeWidgets.StorageBackground(this);
        rectTransform.anchorMin = rectTransform.anchorMax = rectTransform.pivot = new Vector2(0, 1);
        inner.rectTransform.anchorMin = inner.rectTransform.anchorMax = inner.rectTransform.pivot = new Vector2(0, 1);
        inner.rectTransform.anchoredPosition = Vector2.zero;
    }
    internal void Populate(VirtualInventoryLayout layout, int page, IReadOnlyDictionary<Guid, GameItem> live, IReadOnlyDictionary<Guid, GameInventory> observedSources,IReadOnlyDictionary<Guid,LiquidSnapshot>? liquidEntries=null)
    {
        ClearTiles(); liquids=liquidEntries; Layout = layout; PageIndex = page; items = live; sources = observedSources;
        SetShape(layout.Width, 12, Math.Max(13, layout.Height));
        RefreshVisible();
    }
    private bool visibilityQueued;
    private int previousTop = -1;
    internal void QueueVisibility()
    {
        if (visibilityQueued) return;
        visibilityQueued = true;
        LetterboxManager.RunNextUpdate(() => {
            visibilityQueued = false;
            if (handler != null && handler.gameObject.activeInHierarchy) RefreshVisible();
        });
    }
    internal void RefreshVisible()
    {
        if (Layout == null || ItemMouseDragHandler.current?.IsDraggingItem == true
            || ItemMultiSelectHandler.current?.IsGroupDragging == true) return;
        int top = (int)Math.Round(inner.rectTransform.anchoredPosition.y / UISettings.current.gridSize);
        if (previousTop >= 0 && previousTop != top) TerminalSelection.Cancel(this);
        previousTop = top;
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
        TerminalSelection.Cancel(this);
        foreach (var tile in itemTiles.Values) tile.DisposeView();
        foreach (var tile in tiles) { tile.gameObject.SetActive(false); UnityEngine.Object.Destroy(tile.gameObject); }
        tiles.Clear();
        itemTiles.Clear();
    }
    private void AddTile(Guid id, int x, int y, GameItem item)
    {
        int cell = UISettings.current.gridSize;
        var shape = item.modifiedShape;
        var tile = new ItemTile(this, id, item, IsLiquid?6*cell:shape.globalWidth * cell, IsLiquid?3*cell:shape.globalHeight * cell);
        tile.handler.SetParent(inner);
        tile.rectTransform.anchorMin = tile.rectTransform.anchorMax = tile.rectTransform.pivot = new Vector2(0, 1);
        MachineryStyle.Place(tile.handler, x * cell, y * cell, shape.globalWidth * cell, shape.globalHeight * cell);
        // Independent native UI nodes resolve to the one real item. The source renderer is untouched.
        tiles.Add(tile.handler);
        itemTiles.Add(id, tile);
        tile.CreateView();
    }
    internal (int X, int Y) Cell(Vector2 position)
    {
        var local = inner.rectTransform.InverseTransformPoint(position);
        int cell = UISettings.current.gridSize;
        return ((int)Math.Floor(local.x / cell), (int)Math.Floor(-local.y / cell));
    }
    internal GameItem? At(Vector2 position)
    {
        if (Layout == null) return null;
        var (x, y) = Cell(position); var id = Layout.At(PageIndex, x, y);
        return id.HasValue && items.TryGetValue(id.Value, out var item) ? item : null;
    }
    internal GameInventory? SourceAt(Vector2 position)
    {
        if (Layout == null) return null;
        var (x, y) = Cell(position); var id = Layout.At(PageIndex, x, y);
        return id.HasValue && sources.TryGetValue(id.Value, out var source) ? source : null;
    }
    private readonly List<TreeNodeRender> selectionCells = new();
    private static Rect Bounds(RectTransform transform)
    {
        var corners = new Vector3[4]; transform.GetWorldCorners(corners);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var corner in corners) { x0 = Math.Min(x0, corner.x); y0 = Math.Min(y0, corner.y); x1 = Math.Max(x1, corner.x); y1 = Math.Max(y1, corner.y); }
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }
    private static Rect Intersect(Rect a, Rect b) => Rect.MinMaxRect(Math.Max(a.xMin, b.xMin), Math.Max(a.yMin, b.yMin), Math.Min(a.xMax, b.xMax), Math.Min(a.yMax, b.yMax));
    private IEnumerable<(int X,int Y,Guid Id)> ViewCells()
    {
        if(Layout==null) yield break;
        int top=Math.Max(0,(int)Math.Round(inner.rectTransform.anchoredPosition.y/UISettings.current.gridSize));
        for(int y=top;y<Math.Min(Layout.Height,top+GetVisibleHeight()+1);y++) for(int x=0;x<Layout.Width;x++)
        {
            var id=Layout.At(0,x,y);
            if(id.HasValue && itemTiles.ContainsKey(id.Value)) yield return (x,y,id.Value);
        }
    }
    private Rect CellBounds(int x,int y)
    {
        int cell=UISettings.current.gridSize;
        var a=inner.rectTransform.TransformPoint(new Vector3(x*cell,-y*cell));
        var b=inner.rectTransform.TransformPoint(new Vector3((x+1)*cell,-(y+1)*cell));
        return Rect.MinMaxRect(Math.Min(a.x,b.x),Math.Min(a.y,b.y),Math.Max(a.x,b.x),Math.Max(a.y,b.y));
    }
    internal List<GameItemElement> BandItems(Rect band)
    {
        var result=new List<GameItemElement>(); var found=new HashSet<Guid>(); var eligibility=new Dictionary<Guid,bool>();
        if(IsLiquid) return result;
        if(handler==null || !handler.gameObject.activeInHierarchy) return result;
        var clip=Bounds(inner.parent.rectTransform); var window=handler.FindParentWindow();
        foreach(var cell in ViewCells())
        {
            if(found.Contains(cell.Id) || items[cell.Id] is not GameItemElement item) continue;
            var rectangle=Intersect(Intersect(CellBounds(cell.X,cell.Y),clip),band);
            if(rectangle.width<=0 || rectangle.height<=0) continue;
            if(!eligibility.TryGetValue(cell.Id,out var eligible))
            {
                eligible=!item.IsDestroyed() && !item.isDebugMenu && item.unitCount>0 && item.MayRemove()
                    && item.MaxNumRemove()>=item.unitCount && !ItemPinHelper.IsPinned(item)
                    && sources.TryGetValue(cell.Id,out var source) && ReferenceEquals(source,item.parentInventory);
                eligibility.Add(cell.Id,eligible);
            }
            if(eligible && ReferenceEquals(RenderHandler.RaycastFindParentElement<PixelWindow>(rectangle.center),window))
            { found.Add(cell.Id); result.Add(item); }
        }
        return result;
    }
    internal Vector3 ItemCenter(GameItemElement item)
    {
        var tile = itemTiles[ModEntry.ActiveSession!.GuidFor(item)];
        return tile.rectTransform.TransformPoint(new Vector3(tile.widthPixels / 2f, -tile.heightPixels / 2f));
    }
    internal List<TreeNodeRender> HighlightSelection(IReadOnlyCollection<GameItemElement> selected)
    {
        int count=0,cellSize=UISettings.current.gridSize; var selectedSet=new HashSet<GameItemElement>(selected);
        foreach(var cell in ViewCells())
        {
            if(items[cell.Id] is not GameItemElement item || !selectedSet.Contains(item)) continue;
            if(count==selectionCells.Count)
            {
                var node=TreeNodeRender.Instantiate().SetParent(inner); node.transform.SetSiblingIndex(1);
                var image=imageProperty.GetValue(node)!; image.GetType().GetProperty("raycastTarget")!.SetValue(image,false);
                imageEnabled ??= image.GetType().GetProperty("enabled")!; selectionCells.Add(node);
            }
            var target=selectionCells[count++];
            target.rectTransform.anchorMin=target.rectTransform.anchorMax=target.rectTransform.pivot=new Vector2(0,1);
            MachineryStyle.Place(target,cell.X*cellSize,cell.Y*cellSize,cellSize,cellSize);
            setHighlight.Invoke(RenderHandler.current,new object[]{imageProperty.GetValue(target)!,RenderHandler.BackgroundColor.Blue,RenderHandler.BackgroundType.Grid});
        }
        for(int i=count;i<selectionCells.Count;i++) imageEnabled!.SetValue(imageProperty.GetValue(selectionCells[i]),false);
        return selectionCells.GetRange(0,count);
    }
    internal void ClearSelectionVisuals()
    { foreach(var node in selectionCells) if(node!=null) imageEnabled!.SetValue(imageProperty.GetValue(node),false); }
    // The terminal is a network destination, not a physically placed item slot.
    // Use viewport-local cells so scrolling deep into a large catalog never allocates
    // highlight nodes for every preceding row.
    private readonly List<TreeNodeRender> dropHighlights = new();
    private static readonly PropertyInfo imageProperty = typeof(TreeNodeRender).GetProperty("image")!;
    private static readonly MethodInfo setHighlight = typeof(RenderHandler).GetMethod("SetHighlightColor")!;
    private static PropertyInfo? imageEnabled;
    public override List<TreeNodeRender> HighlightSlot(SlotMarker slot, RenderHandler.BackgroundColor color, bool doReplace)
        => HighlightDropArea(color);
    public override List<TreeNodeRender> HighlightWholeInventory(RenderHandler.BackgroundColor color)
        => HighlightDropArea(color);
    private List<TreeNodeRender> HighlightDropArea(RenderHandler.BackgroundColor color)
    {
        int cell = UISettings.current.gridSize;
        int rows = GetVisibleHeight(); int count = width * rows;
        while (dropHighlights.Count < count)
        {
            var node = TreeNodeRender.Instantiate().SetParent(handler);
            node.transform.SetSiblingIndex(1); // above the grid background, below native item views
            var image = imageProperty.GetValue(node)!;
            image.GetType().GetProperty("raycastTarget")!.SetValue(image, false);
            imageEnabled ??= image.GetType().GetProperty("enabled")!;
            dropHighlights.Add(node);
        }
        for (int i = 0; i < dropHighlights.Count; i++)
        {
            var node = dropHighlights[i]; var image = imageProperty.GetValue(node)!;
            if (i >= count) { imageEnabled!.SetValue(image, false); continue; }
            node.rectTransform.anchorMin = node.rectTransform.anchorMax = node.rectTransform.pivot = new Vector2(0, 1);
            MachineryStyle.Place(node, i % width * cell, i / width * cell, cell, cell);
            setHighlight.Invoke(RenderHandler.current, new object[] { image, color, RenderHandler.BackgroundType.Grid });
        }
        return dropHighlights.GetRange(0, count);
    }
    public override void ClearHighlight()
    {
        base.ClearHighlight();
        foreach (var node in dropHighlights) imageEnabled!.SetValue(imageProperty.GetValue(node), false);
    }
    // False selects native sequential bulk insertion, not spatial formation placement.
    public override bool SupportsGroupPlacement => false;
    public override bool UncheckedAccept(GameItem item) => false;
    public override bool Expel(GameItem item) => false;
    public override bool MayHaveValidInventorySlot(GameItem item) => !IsLiquid && item != null;
    public override SlotMarker TryFindOneValidInventorySlot(GameItem item) => IsLiquid?null!:new SlotMarker { item = item, inventory = this, numTransfer = item.unitCount };
    public override SlotMarker TryInventorySlot(GameItem item, int requestedNum, Vector2 position, GridShape shape, TagSystem state)
    {
        var (x,y) = Cell(position);
        return new SlotMarker { item = item, inventory = this, itemGridShape = shape, index = y * 9 + x,
            numTransfer = !IsLiquid && x >= 0 && y >= 0 && x < width && y < height && requestedNum >= item.unitCount ? item.unitCount : 0 };
    }
    internal sealed class ItemTile : EmptyPixelElement, TooltipListener
    {
        private readonly GameItem item;
        internal GameItem Item => item;
        private static readonly Dictionary<GameItemElement, ItemTile> views = new();
        private TreeNodeRender? mirror;
        private TreeNodeRender? liquidLabel;
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
            if(Surface.IsLiquid && Surface.liquids!.TryGetValue(Instance,out var snapshot))
            {
                int cell=UISettings.current.gridSize;
                float scale=Math.Min(2f/Math.Max(1,item.modifiedShape.globalWidth),2f/Math.Max(1,item.modifiedShape.globalHeight));
                mirror.rectTransform.localScale=Vector3.one*scale;
                if(liquidLabel==null)
                {
                    var caption=snapshot.Readable?snapshot.Name:UiText.Get("ui_liquid_insert_only");
                    if(caption.Length>18) caption=caption.Substring(0,17)+"…";
                    var label=NativeWidgets.Text(caption+"\n"+(snapshot.Readable?LiquidSnapshot.Volume(snapshot.Composition.Volume):""));
                    // Copy only the native text render tree and bind its root to the
                    // real container. A MachineryText element here would intercept
                    // RaycastElement<GameItem> and make the label impossible to target.
                    liquidLabel=NativeItemMirror.Copy(label.handler,handler,element);
                    UnityEngine.Object.Destroy(label.handler.gameObject);
                    MachineryStyle.Place(liquidLabel,2*cell,0,4*cell,3*cell);
                    foreach(var node in liquidLabel.GetComponentsInChildren<TreeNodeRender>(true))
                    {
                        var text=typeof(TreeNodeRender).GetProperty("text")!.GetValue(node);
                        if(text==null) continue;
                        text.GetType().GetProperty("raycastTarget")!.SetValue(text,true);
                        MachineryStyle.Place(node,0,0,4*cell,3*cell);
                    }
                }
            }
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
        public RichText GetTooltip() => Surface.IsLiquid && Surface.liquids!.TryGetValue(Instance,out var snapshot)
            ?new RichTextBuilder().Add(snapshot.Readable?snapshot.Description:UiText.Get("ui_liquid_insert_only")).Build()
            :item is GameItemElement element ? element.GetTooltip() : new RichTextBuilder().Add(item.name).Build();
        public RichText GetTooltipAdvanced() => Surface.IsLiquid?GetTooltip():item is GameItemElement element ? element.GetTooltipAdvanced() : GetTooltip();
    }
}
