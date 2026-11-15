using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DeepSpaceAE.Core.Network;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

internal static class DrawerStorage
{
    internal const string Id = "deepspaceae.drawer";
    private sealed class Storage { internal DrawerInventory Data = null!; internal GameGridInventory Cards = null!; }
    private static readonly ConditionalWeakTable<GameItem, Storage> drawers = new();
    internal static bool IsDrawer(GameItem item) => item.identifier == Id;
    private static Storage Get(GameItem item) => drawers.GetValue(item, _ => throw new InvalidOperationException("Missing drawer graph."));
    internal static DrawerInventory Data(GameItem item) => Get(item).Data;
    internal static GameGridInventory Upgrades(GameItem item) => Get(item).Cards;
    internal static void FaceVisibility(GameItem item,bool fake,bool enabled,Color tint)
    { if(drawers.TryGetValue(item,out var data)) data.Data.FaceVisibility(fake,enabled,tint); }
    internal static int Capacity(GameItem item) => DrawerCapacity.Limit(Math.Min(5, Upgrades(item).childItems.Count(c => c.identifier == PartStorage.Capacity)));
    internal static bool IsLocked(GameItem drawer) => PartStorage.Int(drawer,"drawer_locked") != 0;
    internal static bool VoidOverflow(GameItem drawer) => PartStorage.Int(drawer,"drawer_void") != 0;
    internal static void SetVoidOverflow(GameItem drawer,bool enabled)
    {
        if(!GeneralHelper.IsItemEffectivelyOwned(drawer)) return;
        PartStorage.Number(drawer,"drawer_void",enabled?1:0);
        ModEntry.ActiveSession?.Visuals.Invalidate(); ModEntry.ActiveSession?.NativeStorageChanged(drawer);
    }
    internal static string LockedId(GameItem drawer) => PartStorage.Text(drawer,"drawer_type");
    internal static string LockedName(GameItem drawer) => Data(drawer).childItems.FirstOrDefault()?.name
        ?? PartStorage.Text(drawer,"drawer_type_name");
    internal static bool AcceptsType(GameItem drawer, GameItem item) => !IsLocked(drawer)
        || LockedId(drawer).Length == 0 || LockedId(drawer) == item.identifier;
    internal static void RememberType(GameItem drawer, GameItem? item)
    {
        if (!IsLocked(drawer) || LockedId(drawer).Length != 0 || item == null) return;
        PartStorage.String(drawer,"drawer_type",item.identifier);
        PartStorage.String(drawer,"drawer_type_name",item.name ?? item.identifier);
        PartStorage.String(drawer,"drawer_type_icon",item.spriteAtlasPath+"\n"+item.spritePath+"\n"+(item.useSpriteFromMod?"1":"0"));
    }
    internal static Sprite? LockedSprite(GameItem drawer)
    {
        if (!IsLocked(drawer)) return null;
        var source = PartStorage.Text(drawer,"drawer_type_icon").Split('\n');
        if (source.Length != 3) return null;
        return source[2] == "1" ? ModLoader.Mods.FirstOrDefault(m=>m.Manifest.ID==source[0])?.Sprites.GetValueOrDefault(source[1])
            : RenderHandler.TryLoadFromAtlas(source[0],source[1]);
    }
    internal static void ToggleLock(GameItem drawer)
    {
        if (!GeneralHelper.IsItemEffectivelyOwned(drawer)) return;
        PartStorage.Number(drawer,"drawer_locked",IsLocked(drawer)?0:1);
        PartStorage.String(drawer,"drawer_type",""); PartStorage.String(drawer,"drawer_type_name",""); PartStorage.String(drawer,"drawer_type_icon","");
        RememberType(drawer,Data(drawer).childItems.FirstOrDefault());
        Data(drawer).QueueDisplay(); ModEntry.ActiveSession?.NativeStorageChanged(drawer);
    }
    internal static void Create(GameItem item)
    {
        var data = new Storage { Data = new DrawerInventory(item), Cards = new GameGridInventory(5, 1) };
        drawers.Add(item, data);
        data.Cards.SetMachineryStyle();
        data.Cards.mayInventoryAddItemFunc = (card, _) => card.identifier == PartStorage.Capacity && card.unitCount == 1
            && GeneralHelper.IsItemEffectivelyOwned(card) && GeneralHelper.IsItemEffectivelyOwned(item);
        data.Cards.maxSlotRemoveItemFunc = (_, _, _) => data.Data.Used <= DrawerCapacity.Limit(Math.Max(0, Math.Min(5, data.Cards.childItems.Count - 1))) ? int.MaxValue : 0;
        void Changed(GameItem stock, GameInventory inventory, SlotMarker ___)
        {
            if (ReferenceEquals(inventory, data.Data))
            {
                data.Data.StockChanged(stock);
                RememberType(item,data.Data.First);
                // A hidden non-representative instance must become visible when
                // it leaves the pile for an ordinary native inventory.
                if (!ReferenceEquals(stock.parentInventory, data.Data) && stock is GameItemElement removed) removed.handler.gameObject.SetActive(true);
                data.Data.QueueDisplay();
            }
            ModEntry.ActiveSession?.NativeStorageChanged(item);
        }
        foreach (var inv in new GameInventory[] { data.Data, data.Cards })
        {
            inv.onSlotAddItemEndFunc += Changed; inv.onSlotRemoveItemEndFunc += Changed; inv.onSlotChangeAmountItemFunc += Changed;
        }
        // Only this persistent graph owns stock. The local native panel borrows
        // this inventory's render; terminal mirrors remain independent.
        var graph = new PixelWindow(true, UiText.Get("item_drawer_name"));
        var layout = new GridPixelElement(1, 2, true); layout.AttachPos(data.Data, 0, 0); layout.AttachPos(data.Cards, 0, 1);
        graph.Attach(layout); item.SetContentWindow(graph);
    }
    internal static bool Same(GameItem a, GameItem b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.GetType() != b.GetType() || a.identifier != b.identifier || a.child != null || b.child != null
            || a.name != b.name || a.spritePath != b.spritePath || a.customText != b.customText
            || a.shortDescription != b.shortDescription || a.longDescription != b.longDescription || a.flavorText != b.flavorText
            || a.unitValue != b.unitValue || a.unitBaseValue != b.unitBaseValue || a.backupUnitValue != b.backupUnitValue || a.lateUnitValue != b.lateUnitValue
            || !a.GetGameItemType().SequenceEqual(b.GetGameItemType()) || !ItemStackState.SameTags(a.state, b.state) || !ItemStackState.SameTags(a.modifiedState, b.modifiedState)
            || a.itemFeatures.Count != b.itemFeatures.Count || a.shape.width != b.shape.width || a.shape.height != b.shape.height) return false;
        for (int i = 0; i < a.itemFeatures.Count; i++) if (!ItemStackState.SameFeature(a.itemFeatures[i], b.itemFeatures[i])) return false;
        for (int y = 0; y < a.shape.height; y++) for (int x = 0; x < a.shape.width; x++) if (a.shape.GetLocal(x, y) != b.shape.GetLocal(x, y)) return false;
        return true;
    }
}

internal sealed class DrawerInventory : GameGridInventory
{
    // The native getter copies the entire stock. Cache its authoritative list,
    // not a second inventory. All item movement still goes through native slots.
    private List<GameItem>? stock;
    private NativeLayout? nativeLayout;
    private sealed class NativeLayout
    {
        // This fixed game field is scanner-visible. ModReflect.Get performs a
        // caller stack walk on every read; keep that out of the layout hot path.
        private static readonly System.Reflection.FieldInfo hideBorder=typeof(GameGridInventory)
            .GetField("hideBorder",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        private readonly Action<int> setWidth,setHeight;
        private readonly Action background,cells;
        private readonly TreeNodeRender backgroundNode,borderNode;
        internal NativeLayout(DrawerInventory inventory)
        {
            setWidth=ModReflect.Method<Action<int>>(inventory,"set_widthPixels");
            setHeight=ModReflect.Method<Action<int>>(inventory,"set_heightPixels");
            background=ModReflect.Method<Action>(inventory,"ValidateBackground");
            cells=ModReflect.Method<Action>(inventory,"ValidateCellBackdrops");
            backgroundNode=ModReflect.Get<TreeNodeRender>(inventory,"background");
            borderNode=ModReflect.Get<TreeNodeRender>(inventory,"insideBorder");
        }
        internal void Validate(DrawerInventory inventory)
        {
            setWidth(inventory.inventoryShape.width*UISettings.current.gridSize);
            setHeight(inventory.inventoryShape.height*UISettings.current.gridSize);
            background(); cells(); inventory.First?.Validate();
            var color=new Color(1,1,1,1-inventory.transparency);
            borderNode.image.color=color; backgroundNode.image.color=color;
            if((bool)hideBorder.GetValue(inventory)!) borderNode.image.enabled=false;
        }
    }
    private readonly Dictionary<GameItem,int> quantities = new();
    private readonly HashSet<GameItem> variants = new();
    private GameItem? typeRepresentative;
    private bool typeDirty;
    private long used;
    internal List<GameItem> Stock => ModSettings.NativeInventoryReads ? childItems : stock ??= ModReflect.Get<List<GameItem>>(this,"items");
    internal GameItem? First { get { var items=Stock; return items.Count == 0 ? null : items[0]; } }
    internal void StockChanged(GameItem item)
    {
        quantities.TryGetValue(item,out int previous);
        int current=ReferenceEquals(item.parentInventory,this)?item.unitCount:0;
        used+=current-previous;
        if(current==0) quantities.Remove(item); else quantities[item]=current;
        if(current==0) variants.Remove(item);
        if(!ReferenceEquals(typeRepresentative,First))
        {
            if(variants.Count>0) typeDirty=true;
            typeRepresentative=First;
        }
        if(item is GameItemElement element) element.handler.gameObject.SetActive(ReferenceEquals(item,First));
        if(current!=previous) QueueDisplay();
    }
    internal GameItem Drawer { get; }
    private GameItem? representative;
    private readonly DrawerFace? face;
    private long displayCount;
    private bool displayQueued,displayDirty=true;
    internal long Used => used;
    public override void Validate()
    {
        // The native implementation validates every hidden real instance on
        // every insertion and window layout. Only one is rendered here.
        if(Drawer==null || ModSettings.NativeDrawerValidation) { base.Validate(); return; }
        // Lazy binding also lets the compatibility path avoid these private
        // members entirely, including when constructing a new drawer.
        (nativeLayout ??= new NativeLayout(this)).Validate(this);
    }
    internal void FaceVisibility(bool fake,bool enabled,Color tint)=>face?.SetVisibility(fake,enabled,tint);
    internal void QueueDisplay()
    {
        displayDirty=true;
        if(displayQueued) return;
        displayQueued=true;
        LetterboxManager.RunNextUpdate(()=>{
            displayQueued=false;
            if(Drawer is GameItemElement element && !element.IsDestroyed()) PrepareDisplay();
        });
    }
    internal void PrepareDisplay()
    {
        if(!displayDirty) return;
        displayDirty=false;
        var first = First;
        var previous=representative; representative = first;
        if(previous!=null && !ReferenceEquals(previous,first) && ReferenceEquals(previous.parentInventory,this)) RefreshVisibility(previous);
        if(first!=null) RefreshVisibility(first);
        DrawerStorage.RememberType(Drawer,first);
        int width = first?.modifiedShape.globalWidth ?? 2, height = first?.modifiedShape.globalHeight ?? 2;
        if (inventoryShape.width != width || inventoryShape.height != height) SetShape(width, height);
        displayCount=Used;
        face?.Update(first,displayCount,DrawerStorage.LockedSprite(Drawer));
    }
    internal void HideOtherStock()
    {
        if(First!=null) RefreshVisibility(First);
    }
    private void RefreshVisibility(GameItem item)
    { if(item is GameItemElement element) element.handler.gameObject.SetActive(ReferenceEquals(representative,item)); }
    internal void RefreshItemDisplay(GameItem item)
    {
        StockChanged(item);
        RefreshVisibility(item);
        if(ReferenceEquals(representative,item)) face?.Update(item,displayCount,DrawerStorage.LockedSprite(Drawer));
    }
    internal void StateChanged(GameItem item)
    {
        if(!ReferenceEquals(item.parentInventory,this)) return;
        if(ReferenceEquals(item,First)) typeDirty=true;
        else if(First!=null && !DrawerStorage.Same(First,item)) variants.Add(item);
        else variants.Remove(item);
    }
    internal DrawerInventory(GameItem drawer) : base(1, 1)
    {
        Drawer = drawer;
        if(drawer is GameItemElement element) face=new DrawerFace(element);
        // Keep native instances separate. Native GetStackSlot uses MaxNumAdd on
        // the existing target, so it must not bypass the unit budget by merging.
        maxSlotAddItemFunc = (item, _, slot) => slot.targetItem != null || ReferenceEquals(item.parentInventory, this)
            ? 0 : CanVoid(item)?int.MaxValue:(int)Math.Max(0, DrawerStorage.Capacity(Drawer) - Used);
    }
    internal bool CanReplace(GameItem existing,GameItem incoming) => incoming.child==null
        && DrawerStorage.AcceptsType(Drawer,incoming)
        && !GraphUtils.FindParentEquals(this,incoming)
        && DrawerCapacity.Fits(DrawerStorage.Capacity(Drawer),Used,existing.unitCount,incoming.unitCount)
        && Identical(incoming);
    public override bool SupportsGroupPlacement => false;
    private bool Budget(GameItem item,int amount) => DrawerCapacity.Fits(DrawerStorage.Capacity(Drawer), Used,
        ReferenceEquals(item.parentInventory, this) ? item.unitCount : 0, amount);
    private bool CanVoid(GameItem item)=>ModEntry.ActiveSession?.Loading==false && DrawerStorage.VoidOverflow(Drawer)
        && !IsInsertLocked() && item.child==null
        && (item.parentInventory==null || item.MayRemove())
        && GeneralHelper.IsItemEffectivelyOwned(Drawer) && GeneralHelper.IsItemEffectivelyOwned(item);
    private bool Identical(GameItem item)
    {
        // Ordinary additions compare only the representative. Recheck the pile
        // once if the representative itself changes state; preserve differing
        // existing instances for withdrawal rather than erasing their state.
        if(typeDirty)
        {
            typeDirty=false; variants.Clear();
            if(First!=null) foreach(var stock in Stock) if(!DrawerStorage.Same(First,stock)) variants.Add(stock);
        }
        return DrawerStorage.AcceptsType(Drawer,item) && variants.Count==0 && (First==null || DrawerStorage.Same(First,item));
    }
    public override bool MayHaveValidInventorySlot(GameItem item)
    {
        if (item == null || item.unitCount <= 0 || item.child != null || IsInsertLocked() || GraphUtils.FindParentEquals(this, item)
            || !GeneralHelper.MayOwnershipAcceptItem(item, this) || !Budget(item,1) && !CanVoid(item) || !Identical(item)) return false;
        if (item.mayInventoryAddItemFunc != null && !item.mayInventoryAddItemFunc.InvokeAllReduce(item, this, InvokeFuncExtensions.BoolReduceOR)) return false;
        return mayInventoryAddItemFunc == null || mayInventoryAddItemFunc.InvokeAllReduce(item, this, InvokeFuncExtensions.BoolReduceOR);
    }
    public override SlotMarker TryFindOneValidInventorySlot(GameItem item) => TryInventorySlot(item, item.unitCount, Vector2.zero, item.modifiedShape, item.modifiedState);
    public override SlotMarker GetIncompleteItemInventorySlot(GameItem item)=>new()
        { item=item,inventory=this,itemGridShape=item.modifiedShape };
    public override SlotMarker GetItemInventorySlot(GameItem item)=>TryFindOneValidInventorySlot(item);
    public override SlotMarker TryInventorySlot(GameItem item, int amount, Vector2 position, GridShape shape, TagSystem state)
    {
        shape = new GridShapeBuilder(shape).SetTransform(0, 0, false, 0).Build();
        var slot = new SlotMarker { item = item, inventory = this, itemGridShape = shape };
        if (MayHaveValidInventorySlot(item) && item.CheckIsStateValid(this, slot, shape, state)
            && CheckAcceptParentShapeStateModifier(item, slot, shape, state))
            slot.numTransfer = Math.Max(0,Math.Min(amount<0?item.unitCount:Math.Min(amount,item.unitCount),Math.Min(item.MaxNumRemove(),item.MaxNumAdd(slot))));
        return slot;
    }
    public override bool CheckInventorySlotStillValid(GameItem item, GridShape shape, TagSystem state)
    {
        var slot = new SlotMarker { item = item, inventory = this, itemGridShape = shape };
        // Revalidation of saved/current stock is not a new insertion. State
        // changes and overfull loads retain existing instances for withdrawal.
        return (ReferenceEquals(item.parentInventory, this) || MayHaveValidInventorySlot(item))
            && item.CheckIsStateValid(this, slot, shape, state) && CheckChangeParentShapeStateModifier(item, slot, shape, state);
    }
    public override bool CheckShapePlacement(GameItem item, GridShape shape, List<GameItem> ignored) => ReferenceEquals(item.parentInventory, this)
        || Identical(item) && (Budget(item,1) || CanVoid(item));
    public override bool UncheckedAccept(GameItem item)
    {
        if (ModEntry.ActiveSession?.Loading == false)
        {
            if(!Identical(item) || !Budget(item,item.unitCount) && !CanVoid(item)) return false;
            if(CanVoid(item))
            {
                // Only the incoming units may be discarded. Saved stock, a
                // capacity downgrade and merely enabling this option never void.
                if(item.parentInventory!=null || childItems.Contains(item)) return false;
                int incoming=item.unitCount;
                int retained=(int)Math.Min(incoming,Math.Max(0,DrawerStorage.Capacity(Drawer)-Used));
                if(retained==0)
                {
                    item.Destroy(); QueueDisplay(); ModEntry.ActiveSession?.NativeStorageChanged(Drawer); return true;
                }
                if(retained<incoming)
                {
                    item.SetAmount(retained);
                    if(base.UncheckedAccept(item)) return true;
                    item.SetAmount(incoming); return false;
                }
            }
        }
        bool accepted=base.UncheckedAccept(item); // Loading retains all saved instances, including over-capacity stock.
        if(accepted && ModEntry.ActiveSession?.Loading!=false) QueueDisplay();
        return accepted;
    }
}
