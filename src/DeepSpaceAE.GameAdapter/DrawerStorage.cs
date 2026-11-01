using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    internal static int Capacity(GameItem item) => DrawerCapacity.Limit(Math.Min(5, Upgrades(item).childItems.Count(c => c.identifier == PartStorage.Capacity)));
    internal static void Create(GameItem item)
    {
        var data = new Storage { Data = new DrawerInventory(item), Cards = new GameGridInventory(5, 1) };
        drawers.Add(item, data);
        data.Cards.SetMachineryStyle();
        data.Cards.mayInventoryAddItemFunc = (card, _) => card.identifier == PartStorage.Capacity && card.unitCount == 1
            && GeneralHelper.IsItemEffectivelyOwned(card) && GeneralHelper.IsItemEffectivelyOwned(item)
            && ReferenceEquals(item.parentInventory, GameSession.MainInventory());
        data.Cards.maxSlotRemoveItemFunc = (_, _, _) => data.Data.Used <= DrawerCapacity.Limit(Math.Max(0, Math.Min(5, data.Cards.childItems.Count - 1))) ? int.MaxValue : 0;
        void Changed(GameItem _, GameInventory __, SlotMarker ___) => ModEntry.ActiveSession?.NativeStorageChanged(item);
        foreach (var inv in new GameInventory[] { data.Data, data.Cards })
        {
            inv.onSlotAddItemEndFunc += Changed; inv.onSlotRemoveItemEndFunc += Changed; inv.onSlotChangeAmountItemFunc += Changed;
        }
        // Only this persistent native graph owns the stock. The open window uses
        // the same non-owning mirrors as the terminal, never a staging inventory.
        var graph = new PixelWindow(true, UiText.Get("item_drawer_name"));
        var layout = new GridPixelElement(1, 2, true); layout.AttachPos(data.Data, 0, 0); layout.AttachPos(data.Cards, 0, 1);
        graph.Attach(layout); item.SetContentWindow(graph);
    }
    private static bool EqualTags(TagSystem a, TagSystem b) => a.dict.Count == b.dict.Count && a.dict.All(p => b.dict.TryGetValue(p.Key, out var t)
        && p.Value.identifier == t.identifier && p.Value.identifierName == t.identifierName
        && p.Value.valueEnabled == t.valueEnabled && p.Value.valueInt == t.valueInt && p.Value.valueLong == t.valueLong
        && p.Value.valueFloat.Equals(t.valueFloat) && p.Value.valueDouble.Equals(t.valueDouble)
        && p.Value.valueBool == t.valueBool && p.Value.valueString == t.valueString);
    private static readonly FieldInfo[] featureFields = typeof(ItemFeature).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(f => f.Name != "parentItemUniqueId").ToArray();
    private static bool EqualFeature(ItemFeature a, ItemFeature b)
        // Unknown derived feature state is not guessed or coerced.
        => a.GetType() == typeof(ItemFeature) && b.GetType() == typeof(ItemFeature)
            && featureFields.All(f => Equals(f.GetValue(a), f.GetValue(b)));
    internal static bool Same(GameItem a, GameItem b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.GetType() != b.GetType() || a.identifier != b.identifier || a.child != null || b.child != null
            || a.name != b.name || a.spritePath != b.spritePath || a.customText != b.customText
            || a.shortDescription != b.shortDescription || a.longDescription != b.longDescription || a.flavorText != b.flavorText
            || a.unitValue != b.unitValue || a.unitBaseValue != b.unitBaseValue || a.backupUnitValue != b.backupUnitValue || a.lateUnitValue != b.lateUnitValue
            || !a.GetGameItemType().SequenceEqual(b.GetGameItemType()) || !EqualTags(a.state, b.state) || !EqualTags(a.modifiedState, b.modifiedState)
            || a.itemFeatures.Count != b.itemFeatures.Count || a.shape.width != b.shape.width || a.shape.height != b.shape.height) return false;
        for (int i = 0; i < a.itemFeatures.Count; i++) if (!EqualFeature(a.itemFeatures[i], b.itemFeatures[i])) return false;
        for (int y = 0; y < a.shape.height; y++) for (int x = 0; x < a.shape.width; x++) if (a.shape.GetLocal(x, y) != b.shape.GetLocal(x, y)) return false;
        return true;
    }
}

internal sealed class DrawerInventory : GameGridInventory
{
    internal GameItem Drawer { get; }
    internal long Used => childItems.Sum(i => (long)i.unitCount);
    internal DrawerInventory(GameItem drawer) : base(1, 1)
    {
        Drawer = drawer;
        // Keep native instances separate. Native GetStackSlot uses MaxNumAdd on
        // the existing target, so it must not bypass the unit budget by merging.
        maxSlotAddItemFunc = (item, _, slot) => slot.targetItem != null || ReferenceEquals(item.parentInventory, this)
            ? 0 : (int)Math.Max(0, DrawerStorage.Capacity(Drawer) - Used);
    }
    public override bool SupportsGroupPlacement => false;
    private bool Budget(GameItem item) => DrawerCapacity.Fits(DrawerStorage.Capacity(Drawer), Used,
        ReferenceEquals(item.parentInventory, this) ? item.unitCount : 0, item.unitCount);
    private bool Identical(GameItem item) => childItems.All(i => ReferenceEquals(i, item) || DrawerStorage.Same(i, item));
    public override bool MayHaveValidInventorySlot(GameItem item)
    {
        if (item == null || item.unitCount <= 0 || item.child != null || IsInsertLocked() || GraphUtils.FindParentEquals(this, item)
            || !GeneralHelper.MayOwnershipAcceptItem(item, this) || !Budget(item) || !Identical(item)) return false;
        if (item.mayInventoryAddItemFunc != null && !item.mayInventoryAddItemFunc.InvokeAllReduce(item, this, InvokeFuncExtensions.BoolReduceOR)) return false;
        return mayInventoryAddItemFunc == null || mayInventoryAddItemFunc.InvokeAllReduce(item, this, InvokeFuncExtensions.BoolReduceOR);
    }
    public override SlotMarker TryFindOneValidInventorySlot(GameItem item) => TryInventorySlot(item, item.unitCount, Vector2.zero, item.modifiedShape, item.modifiedState);
    public override SlotMarker TryInventorySlot(GameItem item, int amount, Vector2 position, GridShape shape, TagSystem state)
    {
        var slot = new SlotMarker { item = item, inventory = this, itemGridShape = shape };
        if (amount >= item.unitCount && MayHaveValidInventorySlot(item) && item.CheckIsStateValid(this, slot, shape, state)
            && CheckAcceptParentShapeStateModifier(item, slot, shape, state) && item.MaxNumRemove() >= item.unitCount && item.MaxNumAdd(slot) >= item.unitCount)
            slot.numTransfer = item.unitCount;
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
    public override bool CheckShapePlacement(GameItem item, GridShape shape, List<GameItem> ignored) => ReferenceEquals(item.parentInventory, this) || Budget(item);
    public override bool UncheckedAccept(GameItem item)
    {
        if (ModEntry.ActiveSession?.Loading == false && (!Budget(item) || !Identical(item))) return false;
        return base.UncheckedAccept(item); // Loading retains all saved instances, including over-capacity stock.
    }
}
