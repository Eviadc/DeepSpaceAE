using HarmonyLib;
using UnityEngine;

namespace DeepSpaceAE.GameAdapter;

internal sealed class TerminalQuickTake
{
    internal ProjectionGesture Gesture=null!;
    internal int Remaining=1;
    internal bool Routing;
}

/// <summary>Scope only the three native quick-transfer actions, never automation or source views.</summary>
internal static class TerminalQuickTransfer
{
    private static TerminalQuickTake? current;
    internal static TerminalQuickTake? Begin()
    {
        var previous=current; current=null;
        var tile=RenderHandler.RaycastFindParentElement<VirtualInventory.ItemTile>(Input.mousePosition);
        if(tile!=null && !tile.Surface.IsLiquid && tile.Item.parentInventory is GameInventory source)
            current=new TerminalQuickTake { Gesture=new ProjectionGesture {
                Surface=tile.Surface,Item=tile.Item,Source=source,Count=1 } };
        return previous;
    }
    internal static void End(TerminalQuickTake? previous)=>current=previous;
    internal static bool TryTake(SlotMarker slot,int amount,out int accepted)
    {
        accepted=0; var scope=current;
        if(scope==null || scope.Routing || !ReferenceEquals(slot.item,scope.Gesture.Item)) return false;
        if(scope.Remaining<=0 || slot.item is not GameItemElement item) return true;
        scope.Routing=true;
        try
        {
            SlotMarker? destination=slot;
            // The native helper sees the item's original window. Its active-
            // container shortcut can therefore choose this same terminal mirror.
            // A quick withdrawal in that case goes to the actual main inventory.
            if(ReferenceEquals(destination.inventory,scope.Gesture.Surface))
            {
                destination=GameSession.MainInventory()?.TryFindOneValidInventorySlot(item);
                if(destination==null) return true;
            }
            accepted=ModEntry.ActiveSession?.NativeDrop(scope.Gesture,destination.inventory as VirtualInventory,item,
                System.Math.Min(scope.Remaining,amount<0?destination.numTransfer:amount),destination.inventory,destination.targetItem,destination) ?? 0;
            scope.Remaining-=accepted;
            return true;
        }
        finally { scope.Routing=false; }
    }
}

[HarmonyPatch(typeof(ItemQuickTransferHandler),"OnEventRelease")]
internal static class TerminalQuickTransferPatch
{
    [HarmonyPrefix] private static void Prefix(out TerminalQuickTake? __state)=>__state=TerminalQuickTransfer.Begin();
    [HarmonyFinalizer] private static void Finalizer(TerminalQuickTake? __state)=>TerminalQuickTransfer.End(__state);
}
[HarmonyPatch(typeof(ItemQuickTransferAltHandler),"OnEventRelease")]
internal static class TerminalQuickShowcasePatch
{
    [HarmonyPrefix] private static void Prefix(out TerminalQuickTake? __state)=>__state=TerminalQuickTransfer.Begin();
    [HarmonyFinalizer] private static void Finalizer(TerminalQuickTake? __state)=>TerminalQuickTransfer.End(__state);
}
[HarmonyPatch(typeof(ItemQuickTransferAltHandler1),"OnEventRelease")]
internal static class TerminalQuickStoragePatch
{
    [HarmonyPrefix] private static void Prefix(out TerminalQuickTake? __state)=>__state=TerminalQuickTransfer.Begin();
    [HarmonyFinalizer] private static void Finalizer(TerminalQuickTake? __state)=>TerminalQuickTransfer.End(__state);
}
