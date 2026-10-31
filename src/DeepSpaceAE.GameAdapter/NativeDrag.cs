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
    internal bool Checked, Allowed, Cancelled;
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
        NativeDrag.Gesture = new ProjectionGesture { Surface = surface, Item = item, Source = observedSource, Count = item.unitCount };
    }
}
[HarmonyPatch(typeof(ItemMouseDragHandler), "StartDrag")]
internal static class ProjectionStartPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        var g = NativeDrag.Gesture; if (g == null) return true;
        if (!g.Checked) { g.Checked = true; g.Allowed = ModEntry.ActiveSession?.ValidateProjection(g) == true; }
        return g.Allowed && !g.Cancelled;
    }
    [HarmonyPostfix]
    private static void Postfix(ItemMouseDragHandler __instance, ref int ___currentRemove, GameItemElement ___currentItem, Vector3 ___lastMouseClickedPosition)
    {
        if (__instance.IsDraggingItem)
        { NativeDrag.OriginItem = ___currentItem; NativeDrag.OriginSource = ___currentItem.parentInventory; }
        var g = NativeDrag.Gesture;
        if (g == null || !g.Allowed || !__instance.IsDraggingItem) return;
        g.Surface.handler.FindParentWindow()?.ToFront();
        ___currentRemove = g.Count; ___currentItem.UpdateFakeReal(g.Count);
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
        try { ModEntry.ActiveSession?.NativeDrop(source, target, ___currentItem, ___currentRemove, ___lastInventory, ___lastItem); }
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
        if (__instance.inventory is GhostFilterInventory) { __result = 0; return false; }
        if (__instance.inventory is not VirtualInventory) return true;
        __result = 0;
        if (__instance.inventory is VirtualInventory surface && __instance.item is GameItemElement item
            && __instance.numTransfer >= item.unitCount && (amount < 0 || amount >= item.unitCount))
            __result = ModEntry.ActiveSession?.DepositFromNative(surface, item) ?? 0;
        return false; // Route native bulk/quick moves through fresh network checks, never into this grid.
    }
}
[HarmonyPatch(typeof(SlotMarker), nameof(SlotMarker.AcceptUnchecked))]
internal static class ProjectionNeverUncheckedPatch
{
    [HarmonyPrefix]
    private static bool Prefix(SlotMarker __instance) => __instance.inventory is not VirtualInventory && __instance.inventory is not GhostFilterInventory;
}
// GameItem.Destroy is abstract and cannot be patched. Guard its concrete renderer override only.
[HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.Destroy))]
internal static class PreserveUnknownProjectionElementPatch
{
    [HarmonyPrefix]
    private static bool Prefix(GameItemElement __instance) => ModEntry.ActiveSession?.PreserveUncertain(__instance) != true;
}

internal sealed partial class GameSession
{
    private readonly HashSet<GameItem> uncertainItems = new();
    internal int DepositFromNative(VirtualInventory target, GameItemElement item)
    {
        var source = item.parentInventory;
        if (source == null || writing || writeFault) return 0;
        var previousItem = NativeDrag.OriginItem; var previousSource = NativeDrag.OriginSource;
        int count = item.unitCount;
        try
        {
            NativeDrag.OriginItem = item; NativeDrag.OriginSource = source;
            NativeDrop(null, target, item, count, target, null);
            return !writeFault && item.parentInventory != null && !ReferenceEquals(source, item.parentInventory) ? count : 0;
        }
        finally { NativeDrag.OriginItem = previousItem; NativeDrag.OriginSource = previousSource; }
    }
    internal bool PreserveUncertain(GameItem item) => uncertainItems.Contains(item);
    internal bool ValidateProjection(ProjectionGesture gesture)
    {
        try
        {
            if (writing || writeFault || gesture.Cancelled || !gesture.Surface.handler.gameObject.activeInHierarchy) return false;
            var view = Read(gesture.Surface.Owner);
            if (!view.Live.TryGetValue(GuidFor(gesture.Item), out var actual) || !ReferenceEquals(actual, gesture.Item)
                || !ReferenceEquals(gesture.Item.parentInventory, gesture.Source) || gesture.Item.unitCount != gesture.Count
                || !gesture.Item.MayRemove() || gesture.Item.MaxNumRemove() < gesture.Count) return false;
            return true;
        }
        catch (Exception e) { log.Warning(Diagnostics.ConsoleText(e.ToString())); return false; }
    }
    internal void NativeDrop(ProjectionGesture? gesture, VirtualInventory? target, GameItemElement item, int requested,
        GameInventory? hoveredInventory, GameItem? hoveredItem)
    {
        if (writing) return;
        writing = true;
        try
        {
            if (gesture?.Cancelled == true) return;
            if (writeFault) throw new Rejected(UiText.Get("error_transfer_locked"));
            if (gesture != null && (gesture.Cancelled || !gesture.Allowed)) throw new Rejected(UiText.Get("error_gesture_expired"));
            var source = item.parentInventory ?? throw new Rejected(UiText.Get("error_item_gone"));
            if (!ReferenceEquals(NativeDrag.OriginItem, item) || !ReferenceEquals(NativeDrag.OriginSource, source))
                throw new Rejected(UiText.Get("error_drag_source_changed"));
            int count = item.unitCount;
            if (requested != count || (gesture != null && (gesture.Count != count || !ReferenceEquals(gesture.Source, source))))
                throw new Rejected(UiText.Get("error_whole_instance"));
            if (!GeneralHelper.IsItemEffectivelyOwned(item) || !item.MayRemove() || item.MaxNumRemove() < count)
                throw new Rejected(UiText.Get("error_locked_item"));
            // Validate without calling ValidateProjection while the transaction guard is held.
            if (gesture != null)
            {
                var visible = Read(gesture.Surface.Owner);
                if (!visible.Live.TryGetValue(GuidFor(item), out var live) || !ReferenceEquals(live, item)) throw new Rejected(UiText.Get("error_item_inaccessible"));
            }
            if (target != null && ReferenceEquals(target, gesture?.Surface))
            {
                var cell = target.Cell(item.GetFakeRotatorCenteredPosition());
                if (target.Layout?.Reposition(GuidFor(item), target.PageIndex, cell.X, cell.Y) == true) target.Redraw?.Invoke();
                return; // Display arrangement only; the real item stays in its real container.
            }
            SlotMarker placement; View? deposit = null; GameItem? bus = null, owner = null;
            if (target != null)
            {
                deposit = Read(target.Owner);
                if ((IsPart(item) && item.identifier != "deepspaceae.item_storage_bus" && ReferenceEquals(source, deposit.Root)) || deposit.Inventories.ContainsKey(item)) throw new Rejected(UiText.Get("error_active_part"));
                var policies = deposit.Policies.ToDictionary(p => Id(p.Key), p => p.Value, StringComparer.Ordinal);
                SlotMarker? found = null;
                foreach (var busId in StorageRouting.InsertionOrder(policies, item.identifier, PartStorage.Tags(item)))
                {
                    var candidate = deposit.Board.ById[busId];
                    try { found = Placement(item, deposit.Inventories[deposit.Owners[candidate]]); }
                    catch (Rejected) { continue; }
                    bus = candidate; owner = deposit.Owners[candidate]; break;
                }
                placement = found ?? throw new Rejected(UiText.Get("error_network_no_slot"));
            }
            else
            {
                if (gesture == null) return;
                if (hoveredItem != null && GeneralHelper.MayPlayerInsertInto(hoveredItem) && GeneralHelper.IsItemEffectivelyOwned(hoveredItem))
                    placement = Placement(item, Contents(hoveredItem));
                else if (hoveredInventory != null && hoveredInventory is not VirtualInventory && GeneralHelper.MayPlayerInsertInto(hoveredInventory))
                    placement = hoveredInventory.TryInventorySlot(item, count, item.GetFakeRotatorCenteredPosition(), item.tempShape, item.modifiedState);
                else return; // Cancel over empty screen, preserving the real source.
                if (placement == null || !ReferenceEquals(placement.item, item) || placement.targetItem != null || placement.itemGridShape == null
                    || placement.numTransfer < count || ReferenceEquals(placement.inventory, source) || placement.inventory.IsInsertLocked()
                    || GraphUtils.FindParentEquals(placement.inventory, item)) throw new Rejected(UiText.Get("error_drop_here"));
                var destinationOwner = placement.inventory.GetParentItem();
                if (destinationOwner != null && !GeneralHelper.IsItemEffectivelyOwned(destinationOwner)) throw new Rejected(UiText.Get("error_target_not_owned"));
            }
            if (gesture != null)
            {
                var current = Read(gesture.Surface.Owner);
                if (!current.Live.ContainsKey(GuidFor(item)) || !ReferenceEquals(item.parentInventory, source)) throw new Rejected(UiText.Get("error_source_disconnected"));
                if (!PartStorage.Powered(current.Core, current.Board.Evaluation.NightCosts[Id(current.Core)], true)) throw new Rejected(UiText.Get("error_power_lost"));
            }
            if (deposit != null && target != null && bus != null)
            {
                var current = Read(target.Owner);
                if (!ReferenceEquals(current.Core, deposit.Core) || !current.Owners.TryGetValue(bus, out var currentOwner) || !ReferenceEquals(owner, currentOwner)
                    || !current.Inventories[currentOwner].Contains(placement.inventory) || !current.Policies[bus].CanInsert(item.identifier, PartStorage.Tags(item)))
                    throw new Rejected(UiText.Get("error_bus_changed"));
                if (!PartStorage.Powered(current.Core, current.Board.Evaluation.NightCosts[Id(current.Core)], true)) throw new Rejected(UiText.Get("error_power_lost"));
            }
            NativeMove(item, source, placement); message = "";
        }
        catch (Exception e)
        {
            message = e is Rejected ? e.Message : writeFault ? UiText.Get("error_move_unknown") : UiText.Get("error_read_failed");
            log.Warning(Diagnostics.ConsoleText(e.ToString()));
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
    private static void Postfix(GameItemElement __instance) => VirtualInventory.ItemTile.Changed(__instance);
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
