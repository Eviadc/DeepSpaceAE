using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceAE.GameAdapter;

// A rejection-only cache for one synchronous automation batch. A full ordinary
// grid cannot place another occupied footprint, but may still stack or forward.
// Never cache an accepted slot or use this cache to authorize a write.
internal sealed class AutomationPlacement : IDisposable
{
    private static AutomationPlacement? current;
    private readonly Dictionary<GameInventory,GameItem[]> full=new();
    private readonly HashSet<GameInventory> watched=new();
    internal AutomationPlacement()=>current=this;
    private void Changed(GameItem item,GameInventory inventory,SlotMarker slot)=>full.Remove(inventory);
    internal static void Validated(GameItem item)
    { if(item.parentInventory!=null) current?.full.Remove(item.parentInventory); }
    private void Watch(GameInventory inventory)
    {
        if(!watched.Add(inventory)) return;
        inventory.onSlotAddItemEndFunc+=Changed; inventory.onSlotRemoveItemEndFunc+=Changed; inventory.onSlotChangeAmountItemFunc+=Changed;
    }
    internal static SlotMarker? Find(GameInventory inventory,GameItem item)
    {
        var cache=current;
        if(cache==null || inventory.GetType()!=typeof(GameGridInventory)) return inventory.TryFindOneValidInventorySlot(item);
        cache.Watch(inventory);
        if(cache.full.TryGetValue(inventory,out var stock)
            && inventory.slotSearchPassthroughForItemFunc==null && stock.All(i=>i.slotSearchPassthroughForItemFunc==null)
            && !HopperHelper.MayBypassGridRules(item,inventory))
        {
            bool occupied=false;
            for(int y=0;y<item.modifiedShape.height && !occupied;y++) for(int x=0;x<item.modifiedShape.width;x++)
                if(item.modifiedShape.GetLocal(x,y)==1) { occupied=true; break; }
            if(occupied && !stock.Any(target=>item.MayStack(target) && target.MaxNumAdd()>0)) return null;
        }
        var slot=inventory.TryFindOneValidInventorySlot(item);
        if((slot==null || slot.numTransfer<=0) && !((GameGridInventory)inventory).HasFreeCell()) cache.full[inventory]=inventory.childItems.ToArray();
        return slot;
    }
    public void Dispose()
    {
        foreach(var inventory in watched)
        { inventory.onSlotAddItemEndFunc-=Changed; inventory.onSlotRemoveItemEndFunc-=Changed; inventory.onSlotChangeAmountItemFunc-=Changed; }
        if(ReferenceEquals(current,this)) current=null;
    }
}
