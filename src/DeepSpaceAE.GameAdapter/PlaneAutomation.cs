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
    private void RunPlane(GameItem bus,Board board,View view,HashSet<GameItem> moved,ref int operations)
    {
        bool absorb=IoBus.IsImport(bus),whole=IoBus.WholeSpace(bus);
        int units=0;
        if(!absorb && !PartStorage.Policy(bus).Exclude && PartStorage.Policy(bus).Keys.Count==0)
        { Result(bus,0,"ui_plane_choose_filter"); return; }
        for(int attempt=0;attempt<8 && operations<64;attempt++)
        {
            // Moving a container can change the network and placement permissions.
            // Re-read on every pass rather than authorizing from a previous view.
            if(attempt>0)
            {
                board=ReadBoard();
                if(!board.Evaluation.Devices.TryGetValue(Id(bus),out var state) || state.Status!=DeviceStatus.Online
                    || state.CoreId==null || !board.Powered.Contains(state.CoreId)) break;
                view=ReadStorage(board,state.CoreId);
            }
            var inventory=board.Layout.InventoryOf(bus);
            var front=board.Layout.FrontCell(bus);
            if(inventory==null || inventory.IsTag(HopperHelper.NO_HOPPER_INVENTORY_TAG) || !front.HasValue) break;
            var occupant=board.Layout.FrontOwner(bus);
            if(!absorb && !whole && occupant!=null) break; // Never merge into an occupied front cell.
            var candidates=absorb?(whole?inventory.childItems.ToArray():occupant==null?Array.Empty<GameItem>():new[]{occupant})
                :view.Search.Find("").Select(r=>view.Live[r.Instance]).ToArray();
            bool accepted=false;
            foreach(var item in candidates)
            {
                if(moved.Contains(item) || !CanAutomateItem(item) || !IoBus.Matches(bus,item)
                    || GraphUtils.FindParentEquals(bus,item)) continue;
                var source=item.parentInventory!;
                if(absorb && !ReferenceEquals(source,inventory) || !absorb && !NetworkExtracts(view,item,source,bus)) continue;
                var destinations=absorb?NetworkDestinations(view,item,bus):new List<GameInventory>{inventory};
                foreach(var destination in destinations)
                {
                    if(operations>=64) break;
                    if(ReferenceEquals(source,destination) || destination.IsInsertLocked()
                        || destination.IsTag(HopperHelper.NO_HOPPER_INVENTORY_TAG) || GraphUtils.FindParentEquals(destination,item)) continue;
                    SlotMarker? slot;
                    if(!absorb && !whole)
                    {
                        var shape=new GridShapeBuilder(item.modifiedShape).SetTransform(front.Value.X,front.Value.Y,
                            item.modifiedShape.flipped,item.modifiedShape.orientation).Build();
                        slot=destination.TryInventorySlot(item,item.unitCount,shape);
                    }
                    else slot=destination.TryFindOneValidInventorySlot(item);
                    if(slot==null || slot.numTransfer<=0) continue;
                    operations++;
                    int count=slot.TryAcceptOnce(Math.Min(item.unitCount,Math.Min(slot.numTransfer,item.MaxNumRemove())));
                    if(count<=0) continue;
                    units+=count; moved.Add(item);
                    if(slot.item!=null) moved.Add(slot.item);
                    if(slot.targetItem!=null) moved.Add(slot.targetItem);
                    accepted=true; break;
                }
                if(accepted || operations>=64) break;
            }
            if(!accepted || !whole) break;
        }
        Result(bus,units,units>0?"ui_io_moved":"ui_io_idle");
    }
    private void RunFluidBus(GameItem bus,GameItem machine,View view,HashSet<GameItem> moved,ref int operations)
    {
        bool import=IoBus.IsImport(bus); int volume=0,attempts=0;
        foreach(var local in IoBus.LiquidTargets(machine,import).ToArray())
        {
            if(!GeneralHelper.IsItemEffectivelyOwned(local) || !WaterHelper.CanUseWaterContainer(local)) continue;
            foreach(var remote in LiquidRoutes(view,!import))
            {
                if(attempts>=8 || operations>=64) break;
                if(ReferenceEquals(local,remote) || moved.Contains(remote) || !WaterHelper.CanUseWaterContainer(remote)) continue;
                var source=import?local:remote; var target=import?remote:local;
                if(!IoBus.Matches(bus,source)) continue;
                if(!GeneralHelper.IsItemEffectivelyOwned(source) || !GeneralHelper.IsItemEffectivelyOwned(target)) continue;
                var from=LiquidStorage.Read(source); var to=LiquidStorage.Read(target);
                if(from.Composition.Volume<=0 || to.Composition.Volume>=to.Capacity
                    || PartStorage.Int(bus,"fluid_strict",1)!=0 && to.Composition.Volume>0 && from.Composition.Signature!=to.Composition.Signature) continue;
                attempts++; operations++;
                WaterHelper.TransferLiquid(source,target);
                int changed=(int)Math.Max(0,from.Composition.Volume-LiquidStorage.Read(source).Composition.Volume);
                volume+=changed;
                if(changed>0) { moved.Add(remote); moved.Add(local); }
            }
        }
        Result(bus,volume/1000,volume>0?"ui_fluid_io_moved":"ui_io_idle");
    }
}
