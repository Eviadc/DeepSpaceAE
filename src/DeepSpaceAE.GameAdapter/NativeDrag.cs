using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;
using HarmonyLib;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

internal sealed class ProjectionGesture
{
    internal VirtualInventory Surface = null!;
    internal GameItem Item = null!;
    internal GameInventory Source = null!;
    internal int Count;
    internal bool Cancelled;
}
internal static class NativeDrag
{
    internal static ProjectionGesture? Gesture;
    internal static GameItem? OriginItem;
    internal static GameInventory? OriginSource;
}

[HarmonyPatch(typeof(ItemMouseDragHandler), "OnEventPress")]
internal static class ProjectionPressPatch
{
    [HarmonyPostfix]
    private static void Postfix(ItemMouseDragHandler __instance, ISet<KeyCode> keysPressed,
        ref GameItemElement ___currentItem, Vector3 ___lastMouseClickedPosition, ref KeyCode ___lastMouseClickedKey)
    {
        if (__instance.IsDraggingItem || ItemPinHelper.BlocksClicks || !keysPressed.Contains(__instance.key)) return;
        NativeDrag.Gesture = null;
        var position = ___lastMouseClickedPosition;
        // Use the actual tile and the pointer captured by the native input handler.
        // Mirror hit nodes resolve to the real item without moving its renderer.
        var tile = RenderHandler.RaycastFindParentElement<VirtualInventory.ItemTile>(position);
        var surface = tile?.Surface;
        if (surface == null) return;
        var (x, y) = surface.Cell(position);
        if (surface.Layout?.At(surface.PageIndex, x, y) != tile!.Instance) return;
        if (surface.At(position) is not GameItemElement item || item.parentInventory == null) return;
        var observedSource = surface.SourceAt(position);
        if (observedSource == null || !ReferenceEquals(observedSource, item.parentInventory)) return;
        surface.handler.FindParentWindow()?.ToFront();
        ___currentItem = item;
        ___lastMouseClickedKey = __instance.key;
        NativeDrag.Gesture = new ProjectionGesture { Surface = surface, Item = item, Source = observedSource, Count = surface.IsLiquid?item.unitCount:1 };
    }
}
[HarmonyPatch(typeof(ItemMouseDragHandler), "StartDrag")]
internal static class ProjectionStartPatch
{
    [HarmonyPostfix]
    private static void Postfix(ItemMouseDragHandler __instance, GameItemElement ___currentItem, Vector3 ___lastMouseClickedPosition,
        ref int ___currentRemove,ref int ___maxRemove)
    {
        if (__instance.IsDraggingItem)
        { NativeDrag.OriginItem = ___currentItem; NativeDrag.OriginSource = ___currentItem.parentInventory; }
        var g = NativeDrag.Gesture;
        if (g == null || g.Cancelled || !__instance.IsDraggingItem) return;
        if(!g.Surface.IsLiquid) { ___currentRemove=___maxRemove=1; ___currentItem.UpdateFakeReal(1); }
        g.Surface.handler.FindParentWindow()?.ToFront();
        var p = ___currentItem.fakeMiddle.parent.rectTransform.InverseTransformPoint(___lastMouseClickedPosition);
        ___currentItem.SetCenteredPosition(p.x, p.y);
    }
}
[HarmonyPatch(typeof(ItemMouseDragHandler), "EndDrag")]
internal static class ProjectionDropPatch
{
    [HarmonyPrefix]
    private static void Prefix(bool ___isDragging, GameItemElement ___currentItem, int ___currentRemove,
        ref GameInventory ___lastInventory, ref GameItem ___lastItem, ref SlotMarker ___lastSlot)
    {
        if (!___isDragging || ___currentItem == null) return;
        ModEntry.ActiveSession?.TraceWrenchGesture(___currentItem, ___lastItem, "drop");
        if (___lastInventory is GhostFilterInventory filter)
        {
            try
            {
                var marker = filter.TryInventorySlot(___currentItem, ___currentRemove, ___currentItem.GetFakeRotatorCenteredPosition(), ___currentItem.tempShape, ___currentItem.modifiedState);
                if (marker.numTransfer > 0) filter.ConfigureFilter?.Invoke(marker.index, ___currentItem);
            }
            finally { ___lastInventory = null!; ___lastItem = null!; ___lastSlot = null!; NativeDrag.Gesture = null; }
            return;
        }
        var source = NativeDrag.Gesture;
        var target = ___lastInventory as VirtualInventory;
        if (source == null && target == null) return;
        try { ModEntry.ActiveSession?.NativeDrop(source, target, ___currentItem, ___currentRemove, ___lastInventory, ___lastItem, ___lastSlot); }
        finally
        {
            // Let the original handler do its preview/highlight/audio cleanup, with no
            // second native write after our single checked operation.
            ___lastInventory = null!; ___lastItem = null!; ___lastSlot = null!; NativeDrag.Gesture = null;
        }
    }
}
[HarmonyPatch(typeof(SlotMarker), nameof(SlotMarker.TryAcceptOnce))]
internal static class ProjectionNeverOwnsItemsPatch
{
    [HarmonyPrefix]
    private static bool Prefix(SlotMarker __instance, int amount, ref int __result)
    {
        if(TerminalQuickTransfer.TryTake(__instance,amount,out var taken)) { __result=taken; return false; }
        if(__instance.inventory is InterfaceDepositInventory input)
        { __result=__instance.item==null?0:ModEntry.ActiveSession?.DepositInterface(input.Owner,__instance.item,amount) ?? 0; return false; }
        if (__instance.inventory is GhostFilterInventory) { __result = 0; return false; }
        if (__instance.inventory is not VirtualInventory) return true;
        __result = 0;
        if (__instance.inventory is VirtualInventory surface && __instance.item is GameItemElement item
            && __instance.numTransfer > 0)
            __result = ModEntry.ActiveSession?.DepositFromNative(surface, item, amount < 0 ? __instance.numTransfer : Math.Min(amount,__instance.numTransfer)) ?? 0;
        return false; // Route native bulk/quick moves through fresh network checks, never into this grid.
    }
}
[HarmonyPatch(typeof(SlotMarker), nameof(SlotMarker.AcceptUnchecked))]
internal static class ProjectionNeverUncheckedPatch
{
    [HarmonyPrefix]
    private static bool Prefix(SlotMarker __instance) => __instance.inventory is not VirtualInventory && __instance.inventory is not GhostFilterInventory && __instance.inventory is not InterfaceDepositInventory;
}
internal sealed partial class GameSession
{
    internal int DepositFromNative(VirtualInventory target, GameItemElement item) => DepositFromNative(target,item,item.unitCount);
    internal int DepositFromNative(VirtualInventory target, GameItemElement item,int amount)
    {
        var source = item.parentInventory;
        return target.IsLiquid || source == null || writing ? 0 : NativeDrop(null,target,item,amount,target,null);
    }
    internal int NativeDrop(ProjectionGesture? gesture, VirtualInventory? target, GameItemElement item, int requested,
        GameInventory? hoveredInventory, GameItem? hoveredItem,SlotMarker? hoveredSlot=null)
    {
        if (writing) return 0;
        writing = true;
        try
        {
            if(target?.IsLiquid==true) throw new Rejected(UiText.Get("error_liquid_use_native"));
            if (gesture?.Cancelled == true) return 0;
            // Native ownership admission also permits unpaid trade goods. AE
            // transfers require actual ownership before any native move/split.
            if(!GeneralHelper.IsItemEffectivelyOwned(item)) throw new Rejected(UiText.Get("error_not_owned"));
            var source = item.parentInventory ?? throw new Rejected(UiText.Get("error_item_gone"));
            int count = Math.Min(requested,item.unitCount);
            if(gesture!=null) count=Math.Min(count,gesture.Count);
            if (count<=0 || !item.MayRemove())
                throw new Rejected(UiText.Get("error_locked_item"));
            if (gesture != null)
            {
                var visible = Read(gesture.Surface.Owner);
                if (!visible.Live.TryGetValue(GuidFor(item), out var live) || !ReferenceEquals(live, item)) throw new Rejected(UiText.Get("error_item_inaccessible"));
            }
            if (target != null && ReferenceEquals(target, gesture?.Surface))
            {
                var cell = target.Cell(item.GetFakeRotatorCenteredPosition());
                if (target.Layout?.Reposition(GuidFor(item), target.PageIndex, cell.X, cell.Y) == true) target.Redraw?.Invoke();
                return 0; // Display arrangement only; the real item stays in its real container.
            }
            SlotMarker placement;
            if (target != null)
            {
                var deposit = Read(target.Owner);
                var policies = deposit.Policies.ToDictionary(p => Id(p.Key), p => p.Value, StringComparer.Ordinal);
                var devices = deposit.Policies.Keys.ToDictionary(Id, p => p, StringComparer.Ordinal);
                SlotMarker? found = null;
                foreach (var busId in StorageRouting.InsertionOrder(policies, item.identifier, PartStorage.Tags(item)))
                {
                    var candidate = devices[busId];
                    try { found = Placement(item, deposit.Inventories[deposit.Owners[candidate]]); }
                    catch (Rejected) { continue; }
                    break;
                }
                placement = found ?? throw new Rejected(UiText.Get("error_network_no_slot"));
            }
            else
            {
                if (gesture == null) return 0;
                if(hoveredItem!=null && InterfaceStorage.IsItem(hoveredItem) && GeneralHelper.MayPlayerInsertInto(hoveredItem))
                    placement=InterfacePlacement(ReadInterface(hoveredItem),item);
                else if(hoveredInventory is InterfaceDepositInventory input)
                    placement=InterfacePlacement(ReadInterface(input.Owner),item);
                else if(hoveredSlot?.inventory!=null && hoveredSlot.inventory is not VirtualInventory && hoveredSlot.numTransfer>0)
                    placement=hoveredSlot;
                else if(hoveredItem?.parentInventory is GameInventory stackInventory && GeneralHelper.MayPlayerInsertInto(stackInventory)
                    && hoveredItem.GetStackSlot(item) is SlotMarker stack && stack.numTransfer>0)
                    placement=stack;
                else if (hoveredItem != null && GeneralHelper.MayPlayerInsertInto(hoveredItem))
                    placement = Placement(item, Contents(hoveredItem));
                else if (hoveredInventory != null && hoveredInventory is not VirtualInventory && GeneralHelper.MayPlayerInsertInto(hoveredInventory))
                    placement = hoveredInventory.TryInventorySlot(item, count, item.GetFakeRotatorCenteredPosition(), item.tempShape, item.modifiedState);
                else return 0;
                if(placement==null || placement.numTransfer<=0) throw new Rejected(UiText.Get("error_drop_here"));
            }
            if(placement.inventory is InterfaceDepositInventory redirected)
                placement=InterfacePlacement(ReadInterface(redirected.Owner),item);
            int accepted=placement.TryAcceptOnce(count);
            message=accepted>0?"":UiText.Get("error_drop_here");
            return accepted;
        }
        catch (Exception e)
        {
            message = e is Rejected ? e.Message : UiText.Get("error_operation");
            log.Warning(Diagnostics.ConsoleText(e.ToString()));
            return 0;
        }
        finally
        {
            writing = false; Visuals.Invalidate();
            terminalDirty = openTerminal != null;
            ScheduleTerminalRefresh(); RefreshPartLater();
        }
    }
}

[HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.Hide))]
internal static class NativeWindowHiddenPatch
{
    [HarmonyPostfix]
    private static void Postfix(PixelWindow __instance) => ModEntry.ActiveSession?.NativeWindowHidden(__instance);
}

[HarmonyPatch(typeof(ItemMouseDragHandler), "OnEventReset")]
internal static class CancelProjectionGesturePatch
{
    [HarmonyPrefix]
    private static void Prefix(bool ___isDragging, ref GameInventory ___lastInventory, ref GameItem ___lastItem, ref SlotMarker ___lastSlot)
    {
        if (!___isDragging || (NativeDrag.Gesture == null && ___lastInventory is not VirtualInventory && ___lastInventory is not GhostFilterInventory)) return;
        if (NativeDrag.Gesture != null) NativeDrag.Gesture.Cancelled = true;
        ___lastInventory = null!; ___lastItem = null!; ___lastSlot = null!;
    }
}

[HarmonyPatch(typeof(ItemMouseDragHandler), "EndDrag")]
internal static class ClearNativeDragOriginPatch
{
    [HarmonyFinalizer]
    private static void Finalizer()
    {
        NativeDrag.OriginItem = null; NativeDrag.OriginSource = null;
        ModEntry.ActiveSession?.DragEnded();
    }
}

// Refresh view-only snapshots after native visuals have settled, coalesced per item.
[HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.ValidateSelect))]
internal static class NativeTerminalViewChangedPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameItemElement __instance)
    { VirtualInventory.ItemTile.Changed(__instance); ModEntry.ActiveSession?.TerminalItemValidated(__instance); }
}
[HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.ApplyAnimationFrame))]
internal static class NativeTerminalAnimationChangedPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameItemElement __instance) => VirtualInventory.ItemTile.Changed(__instance);
}
[HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.ValidatePartialSprite))]
internal static class NativeTerminalSpriteChangedPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameItemElement __instance) => VirtualInventory.ItemTile.Changed(__instance);
}

// The native scroller derives its extent from owned items. Our non-owning directory
// supplies its display extent instead; its childItems must stay empty.
[HarmonyPatch(typeof(GameGridScrollableInventory), nameof(GameGridScrollableInventory.GetMinVisibleHeight))]
internal static class TerminalScrollExtentPatch
{
    [HarmonyPrefix]
    private static bool Prefix(GameGridScrollableInventory __instance, ref int __result)
    {
        if (__instance is not VirtualInventory surface) return true;
        __result = Math.Max(surface.GetVisibleHeight() + 1, surface.Layout?.Height ?? surface.GetVisibleHeight() + 1);
        surface.QueueVisibility();
        return false;
    }
}
[HarmonyPatch(typeof(GameGridScrollableInventory), nameof(GameGridScrollableInventory.Scroll))]
internal static class TerminalScrollDragGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(GameGridScrollableInventory __instance) => __instance is not VirtualInventory
        || (ItemMouseDragHandler.current?.IsDraggingItem != true && ItemMultiSelectHandler.current?.IsGroupDragging != true);
}

[HarmonyPatch(typeof(GameGridScrollableInventory), nameof(GameGridScrollableInventory.SetScroll))]
internal static class TerminalScrollBarDragGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(GameGridScrollableInventory __instance) => __instance is not VirtualInventory
        || (ItemMouseDragHandler.current?.IsDraggingItem != true && ItemMultiSelectHandler.current?.IsGroupDragging != true);
}

[HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.HandleModSprite))]
internal static class NativeTerminalModSpriteChangedPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameItemElement __instance) => VirtualInventory.ItemTile.Changed(__instance);
}
[HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.HandleInnateModSprite))]
internal static class NativeTerminalInnateSpriteChangedPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameItemElement __instance) => VirtualInventory.ItemTile.Changed(__instance);
}
[HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.RefreshPlayingFrame))]
internal static class NativeTerminalPlayingFrameChangedPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameItemElement __instance) => VirtualInventory.ItemTile.Changed(__instance);
}
