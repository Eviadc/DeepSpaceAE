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
        ref GameItemElement ___currentItem, Vector3 ___lastMouseClickedPosition)
    {
        if (__instance.IsDraggingItem || ItemPinHelper.BlocksClicks || !keysPressed.Contains(__instance.key)) return;
        NativeDrag.Gesture = null;
        var surface = RenderHandler.RaycastFindParentElement<VirtualInventory>(___lastMouseClickedPosition);
        if (surface == null || surface.Ghost || surface.At(___lastMouseClickedPosition) is not GameItemElement item || item.parentInventory == null) return;
        var observedSource = surface.SourceAt(___lastMouseClickedPosition);
        if (observedSource == null || !ReferenceEquals(observedSource, item.parentInventory)) return;
        ___currentItem = item;
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
    private static bool Prefix(SlotMarker __instance, ref int __result)
    {
        if (__instance.inventory is not VirtualInventory) return true;
        __result = 0; return false; // Never let another native path expel into a display-only grid.
    }
}
[HarmonyPatch(typeof(SlotMarker), nameof(SlotMarker.AcceptUnchecked))]
internal static class ProjectionNeverUncheckedPatch
{
    [HarmonyPrefix]
    private static bool Prefix(SlotMarker __instance) => __instance.inventory is not VirtualInventory;
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
            if (target?.Ghost == true)
            {
                var marker = target.TryInventorySlot(item, item.unitCount, item.GetFakeRotatorCenteredPosition(), item.tempShape, item.modifiedState);
                if (marker.numTransfer > 0) target.ConfigureFilter?.Invoke(marker.index, item);
                return; // A ghost filter copies metadata; never moves the dragged instance.
            }
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
                if ((IsPart(item) && ReferenceEquals(source, deposit.Root)) || deposit.Inventories.ContainsKey(item)) throw new Rejected(UiText.Get("error_active_part"));
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
            var terminal = openTerminal;
            LetterboxManager.RunNextUpdate(() => { if (terminal != null && ReferenceEquals(openTerminal, terminal)) Open(terminal); RefreshPartLater(); });
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
        if (!___isDragging || (NativeDrag.Gesture == null && ___lastInventory is not VirtualInventory)) return;
        if (NativeDrag.Gesture != null) NativeDrag.Gesture.Cancelled = true;
        ___lastInventory = null!; ___lastItem = null!; ___lastSlot = null!;
    }
}

[HarmonyPatch(typeof(ItemMouseDragHandler), "EndDrag")]
internal static class ClearNativeDragOriginPatch
{
    [HarmonyFinalizer]
    private static void Finalizer()
    { NativeDrag.OriginItem = null; NativeDrag.OriginSource = null; }
}
