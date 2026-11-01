using System;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

/// <summary>Final native preflight. SlotMarker.TryAcceptOnce itself does not revalidate a slot.</summary>
internal static class NativeTransfer
{
    internal static SlotMarker? Revalidate(GameItem item,GameInventory source,SlotMarker candidate,int amount)
    {
        var destination=candidate.inventory;
        if(amount<=0 || item.unitCount<amount || !ReferenceEquals(candidate.item,item) || !ReferenceEquals(item.parentInventory,source)
            || !source.childItems.Contains(item) || !GeneralHelper.IsItemEffectivelyOwned(item)
            || !item.MayRemove() || item.MaxNumRemove()<amount || destination==null
            || destination is VirtualInventory || destination is GhostFilterInventory
            || ReferenceEquals(source,destination) || destination.IsInsertLocked()
            || GraphUtils.FindParentEquals(destination,item)) return null;
        var owner=destination.GetParentItem();
        if(owner!=null && !GeneralHelper.IsItemEffectivelyOwned(owner)) return null;
        SlotMarker? fresh;
        if(candidate.targetItem is GameItem target)
        {
            if(!ReferenceEquals(target.parentInventory,destination) || !destination.childItems.Contains(target)
                || !destination.MayHaveValidInventorySlot(item)) return null;
            fresh=target.GetStackSlot(item);
        }
        else
        {
            if(candidate.itemGridShape==null) return null;
            var shape=new GridShapeBuilder(item.modifiedShape).SetTransform(candidate.itemGridShape).Build();
            fresh=destination.TryInventorySlot(item,amount,Vector2.negativeInfinity,shape,item.modifiedState);
        }
        return fresh!=null && ReferenceEquals(fresh.item,item) && ReferenceEquals(fresh.inventory,destination)
            && ReferenceEquals(fresh.targetItem,candidate.targetItem) && fresh.numTransfer>=amount
            && (fresh.targetItem!=null || fresh.itemGridShape!=null)?fresh:null;
    }
}
