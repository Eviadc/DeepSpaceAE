using System;
using System.Linq;
using DeepSpaceAE.Core.Network;
using HarmonyLib;

namespace DeepSpaceAE.GameAdapter;

// Production goes through these native entry points before spilling into the
// ship inventories. Route only new, unplaced output from an owned machine.
[HarmonyPatch(typeof(GraphUtils),nameof(GraphUtils.TryAcceptAll))]
internal static class MachineOutputPlacementPatch
{
    [HarmonyPostfix]
    private static void Postfix(GraphNodeStorage currentNode,GameItem item,ref int __result)
    {
        if(__result<=0 || currentNode is not GameInventory output || item==null || item.parentInventory!=null) return;
        var session=ModEntry.ActiveSession;
        if(session==null) return;
        __result-=session.RouteMachineOutput(output,item,Math.Min(__result,item.unitCount),out bool enabled);
        // These button-operated machines ignore the native remainder. Their
        // actual production calls request a whole new item.
        if(enabled && __result>0 && __result==item.unitCount
            && output.GetParentItem()?.identifier is "chem_finisher" or "bottle_printer" or "box_dispenser")
        {
            GraphUtils.EmporiumTryAdd(item);
            __result=0;
        }
    }
}

[HarmonyPatch(typeof(GraphUtils),nameof(GraphUtils.CanAccept))]
internal static class MachineOutputCapacityPatch
{
    [HarmonyPostfix]
    private static void Postfix(GraphNodeStorage currentNode,GameItem item,ref bool __result)
    {
        if(__result || currentNode is not GameInventory output || item==null || item.parentInventory!=null
            || output.IsInsertLocked() || output.GetParentItem()?.identifier is not ("furnace" or "bottle_printer" or "box_dispenser")
            || output.mayInventoryAddItemFunc?.Invoke(item,output)!=true) return;
        __result=ModEntry.ActiveSession?.RouteMachineOutput(output,item,item.unitCount,out _,preview:true)>0;
    }
}

[HarmonyPatch(typeof(HopperHelper),nameof(HopperHelper.TryRouteOverflow))]
internal static class MachineOutputOverflowPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(GameInventory grid,GameItem item,ref bool __result)
    {
        if(grid==null || item==null || item.parentInventory!=null || item.unitCount<=0) return true;
        int requested=item.unitCount;
        if((ModEntry.ActiveSession?.RouteMachineOutput(grid,item,requested,out _) ?? 0)<requested) return true;
        __result=true; return false;
    }
}

internal sealed partial class GameSession
{
    internal int RouteMachineOutput(GameInventory output,GameItem item,int requested,out bool enabled,bool preview=false)
    {
        enabled=false;
        if(Loading || writing || requested<=0 || item.parentInventory!=null || MainInventory()==null
            || !GeneralHelper.IsItemEffectivelyOwned(item) || IsPart(item) || HopperHelper.IsHopper(item)
            || output.IsRemoveLocked() || output.IsTag(HopperHelper.NO_HOPPER_INVENTORY_TAG)
            || MapUIManager.Instance!=null && MapUIManager.Instance.IsOutside) return 0;
        var machine=output.GetParentItem();
        if(machine==null || !GeneralHelper.IsItemEffectivelyOwned(machine) || !IoBus.MachineSlots(machine,true).Contains(output)) return 0;
        int moved=0;
        writing=true;
        try
        {
            var board=ReadBoard(); int generation=automationGeneration;
            var buses=board.Layout.Parts.Where(b=>b.identifier==IoBus.Import && !PartModes.IsFluid(b)
                && IoBus.CollectOverflow(b) && ReferenceEquals(board.Layout.FrontOwner(b),machine))
                .OrderBy(IoBus.Order).ThenByDescending(b=>PartStorage.Policy(b).Priority).ThenBy(b=>b.GetUniqueID());
            foreach(var bus in buses)
            {
                if(moved>=requested || generation!=automationGeneration) break;
                if(!board.Evaluation.Devices.TryGetValue(Id(bus),out var connection)
                    || connection.Status!=DeviceStatus.Online || connection.CoreId==null || !board.Powered.Contains(connection.CoreId)) continue;
                if(!enabled && !preview && machine.identifier=="chem_finisher" && item.IsTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG))
                    ChemicalProductHelper.GradeProduct(item); // Native button grades immediately after placement.
                enabled=true;
                var policy=PartStorage.Policy(bus);
                bool pendingQuality=preview && machine.identifier=="furnace" && IoBus.Exact(bus) && !policy.Tags && !policy.Exclude;
                // The furnace probes with a generic ingot even for its scrap recipe.
                if(pendingQuality ? policy.Keys.Count==0 : !IoBus.Matches(bus,item)) continue;
                var view=ReadStorage(board,connection.CoreId,false); int before=moved;
                foreach(var destination in NetworkDestinations(view,item,machine))
                {
                    if(moved>=requested || generation!=automationGeneration) break;
                    if(destination.IsInsertLocked() || destination.IsTag(HopperHelper.NO_HOPPER_INVENTORY_TAG)
                        || GraphUtils.FindParentEquals(destination,item)) continue;
                    for(int attempt=0;attempt<40 && moved<requested && generation==automationGeneration;attempt++)
                    {
                        if(pendingQuality && destination is DrawerInventory drawer && DrawerStorage.AcceptsType(drawer.Drawer,item)
                            && (drawer.First==null || drawer.First.identifier==item.identifier)
                            && (drawer.Used<DrawerStorage.Capacity(drawer.Drawer) || DrawerStorage.VoidOverflow(drawer.Drawer)))
                            return 1; // Only the ingot's final grade is unknown.
                        var slot=destination.TryFindOneValidInventorySlot(item);
                        if(slot==null || slot.numTransfer<=0) break;
                        if(preview) return slot.numTransfer;
                        int accepted=slot.TryAcceptOnce(Math.Min(requested-moved,slot.numTransfer));
                        if(accepted<=0) break;
                        moved+=accepted;
                    }
                }
                if(!preview) Result(bus,moved-before,moved>before?"ui_io_moved":"ui_io_idle");
            }
        }
        catch(Exception e) { log.Warning("AE machine output routing stopped: "+Diagnostics.ConsoleText(e.ToString())); }
        finally
        {
            writing=false;
            if(moved>0) { Visuals.Invalidate(); NativeStorageChanged(machine); }
        }
        return moved;
    }
}
