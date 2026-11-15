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
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(GraphNodeStorage currentNode,GameItem item,ref int amount,ref int __result)
    {
        if(currentNode is not GameInventory output || item==null || item.parentInventory!=null) return true;
        int requested=Math.Min(amount<0?item.unitCount:amount,item.unitCount);
        int moved=ModEntry.ActiveSession?.RouteMachineOutput(output,item,requested) ?? 0;
        if(moved<=0) return true;
        amount=requested-moved;
        if(amount>0) return true;
        __result=0; return false; // Native result is the number left unplaced.
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
        if((ModEntry.ActiveSession?.RouteMachineOutput(grid,item,requested) ?? 0)<requested) return true;
        __result=true; return false;
    }
}

internal sealed partial class GameSession
{
    internal int RouteMachineOutput(GameInventory output,GameItem item,int requested)
    {
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
                && PartStorage.Int(b,"io_enabled",1)!=0 && ReferenceEquals(board.Layout.FrontOwner(b),machine))
                .OrderBy(IoBus.Order).ThenByDescending(b=>PartStorage.Policy(b).Priority).ThenBy(b=>b.GetUniqueID());
            foreach(var bus in buses)
            {
                if(moved>=requested || generation!=automationGeneration) break;
                if(!IoBus.Matches(bus,item) || !board.Evaluation.Devices.TryGetValue(Id(bus),out var connection)
                    || connection.Status!=DeviceStatus.Online || connection.CoreId==null || !board.Powered.Contains(connection.CoreId)) continue;
                var view=ReadStorage(board,connection.CoreId,false); int before=moved;
                foreach(var destination in NetworkDestinations(view,item,machine))
                {
                    if(moved>=requested || generation!=automationGeneration) break;
                    if(destination.IsInsertLocked() || destination.IsTag(HopperHelper.NO_HOPPER_INVENTORY_TAG)
                        || GraphUtils.FindParentEquals(destination,item)) continue;
                    for(int attempt=0;attempt<40 && moved<requested && generation==automationGeneration;attempt++)
                    {
                        var slot=destination.TryFindOneValidInventorySlot(item);
                        if(slot==null || slot.numTransfer<=0) break;
                        int accepted=slot.TryAcceptOnce(Math.Min(requested-moved,slot.numTransfer));
                        if(accepted<=0) break;
                        moved+=accepted;
                    }
                }
                Result(bus,moved-before,moved>before?"ui_io_moved":"ui_io_idle");
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
