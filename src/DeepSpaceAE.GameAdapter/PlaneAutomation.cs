using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;
namespace DeepSpaceAE.GameAdapter;

internal sealed partial class GameSession
{
    private static bool CanAutomateItem(GameItem item)=>!IsComponent(item) && !HopperHelper.IsHopper(item)
        && item.unitCount>0 && GeneralHelper.IsItemEffectivelyOwned(item) && item.MayRemove() && item.MaxNumRemove()>0
        && item.parentInventory!=null && !item.parentInventory.IsRemoveLocked()
        && !item.parentInventory.IsTag(HopperHelper.NO_HOPPER_INVENTORY_TAG);
    private void RunPlane(GameItem bus,Board board,View view,HashSet<GameItem> moved,ref int operations,int busLimit,int batchLimit)
    {
        bool absorb=IoBus.IsImport(bus),whole=IoBus.WholeSpace(bus);
        int units=0,attempts=0,generation=automationGeneration;
        if(!absorb && !PartStorage.Policy(bus).Exclude && PartStorage.Policy(bus).Keys.Count==0)
        { Result(bus,0,"ui_plane_choose_filter"); return; }
        var inventory=board.Layout.InventoryOf(bus); var front=board.Layout.FrontCell(bus);
        if(inventory==null || !front.HasValue || inventory.IsTag(HopperHelper.NO_HOPPER_INVENTORY_TAG))
        { Result(bus,0,"ui_io_no_slots"); return; }
        var occupant=board.Layout.FrontOwner(bus);
        if(!absorb && !whole && occupant!=null) { Result(bus,0,"ui_io_idle"); return; }
        // Finite gesture snapshot: never chase items created by callbacks. Each
        // candidate is visited once; native slot checks still run on every move.
        var candidates=absorb?(whole?inventory.childItems.ToArray():occupant==null?Array.Empty<GameItem>():new[]{occupant})
            :view.Search.Find("").Select(r=>view.Live[r.Instance]).ToArray();
        foreach(var item in candidates)
        {
            if(attempts>=busLimit || operations>=batchLimit) break;
            if(moved.Contains(item)) continue;
            var source=item.parentInventory; int remaining=item.unitCount;
            while(remaining>0 && source!=null && ReferenceEquals(item.parentInventory,source) && attempts<busLimit && operations<batchLimit)
            {
                if(generation!=automationGeneration)
                {
                    board=ReadBoard(); generation=automationGeneration;
                    if(!board.Evaluation.Devices.TryGetValue(Id(bus),out var state) || state.Status!=DeviceStatus.Online
                        || state.CoreId==null || !board.Powered.Contains(state.CoreId)
                        || !ReferenceEquals(board.Layout.InventoryOf(bus),inventory)) goto Complete;
                    view=ReadStorage(board,state.CoreId,catalog:!absorb);
                }
                if(!CanAutomateItem(item) || !IoBus.Matches(bus,item) || GraphUtils.FindParentEquals(bus,item)
                    || absorb && !ReferenceEquals(source,inventory) || !absorb && !NetworkExtracts(view,item,source,bus)) break;
                var destinations=absorb?NetworkDestinations(view,item,bus):new List<GameInventory>{inventory};
                bool accepted=false;
                foreach(var destination in destinations)
                {
                    if(attempts>=busLimit || operations>=batchLimit) break;
                    if(ReferenceEquals(source,destination) || destination.IsInsertLocked()
                        || destination.IsTag(HopperHelper.NO_HOPPER_INVENTORY_TAG) || GraphUtils.FindParentEquals(destination,item)) continue;
                    SlotMarker? slot;
                    if(!absorb && !whole)
                    {
                        if(board.Layout.FrontOwner(bus)!=null) goto Complete;
                        var shape=new GridShapeBuilder(item.modifiedShape).SetTransform(front.Value.X,front.Value.Y,
                            item.modifiedShape.flipped,item.modifiedShape.orientation).Build();
                        slot=destination.TryInventorySlot(item,item.unitCount,shape);
                    }
                    else slot=AutomationPlacement.Find(destination,item);
                    if(slot==null || slot.numTransfer<=0) continue;
                    attempts++; operations++;
                    int count=slot.TryAcceptOnce(Math.Min(remaining,Math.Min(item.unitCount,Math.Min(slot.numTransfer,item.MaxNumRemove()))));
                    if(count<=0) continue;
                    units+=count; remaining-=count;
                    if(slot.item!=null && !ReferenceEquals(slot.item,item)) moved.Add(slot.item);
                    if(slot.targetItem!=null) moved.Add(slot.targetItem);
                    if(!ReferenceEquals(item.parentInventory,source)) moved.Add(item);
                    accepted=true; break;
                }
                if(!accepted) break;
                if(!whole && !absorb) goto Complete;
                if(busLimit!=int.MaxValue) { moved.Add(item); break; }
            }
            if(!whole && absorb) break;
        }
        Complete:
        Result(bus,units,units>0?"ui_io_moved":"ui_io_idle");
    }
    private void RunFluidBus(GameItem bus,GameItem machine,View view,HashSet<GameItem> moved,ref int operations,int busLimit,int batchLimit)
    {
        bool import=IoBus.IsImport(bus); int volume=0,attempts=0;
        foreach(var local in IoBus.LiquidTargets(machine,import).ToArray())
        {
            if(!GeneralHelper.IsItemEffectivelyOwned(local) || !WaterHelper.CanUseWaterContainer(local)) continue;
            foreach(var remote in LiquidRoutes(view,!import))
            {
                if(attempts>=busLimit || operations>=batchLimit) break;
                if(ReferenceEquals(local,remote) || busLimit!=int.MaxValue && moved.Contains(remote) || !WaterHelper.CanUseWaterContainer(remote)) continue;
                var source=import?local:remote; var target=import?remote:local;
                if(!IoBus.Matches(bus,source)) continue;
                if(!GeneralHelper.IsItemEffectivelyOwned(source) || !GeneralHelper.IsItemEffectivelyOwned(target)) continue;
                var from=LiquidStorage.Read(source); var to=LiquidStorage.Read(target);
                if(FluidDrawer.IsDrawer(target) && LiquidStorage.ExactFill(from.Composition,to)==0) continue;
                if(from.Composition.Volume<=0 || to.Composition.Volume>=to.Capacity
                    || PartStorage.Int(bus,"fluid_strict",1)!=0 && to.Composition.Volume>0 && from.Composition.Signature!=to.Composition.Signature) continue;
                attempts++; operations++;
                LiquidStorage.NetworkTransfer(source,target);
                int changed=(int)Math.Max(0,from.Composition.Volume-LiquidStorage.Read(source).Composition.Volume);
                volume+=changed;
                if(changed>0) { moved.Add(remote); moved.Add(local); }
            }
        }
        Result(bus,volume/1000,volume>0?"ui_fluid_io_moved":"ui_io_idle");
    }
}
