using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

internal static class DiskStorage
{
    internal const string Drive="deepspaceae.disk_drive",Small="deepspaceae.disk_small",Large="deepspaceae.disk_large";
    internal static bool IsDrive(GameItem item)=>item.identifier==Drive;
    internal static bool IsDisk(GameItem item)=>item.identifier==Small || item.identifier==Large;
    internal static int Capacity(GameItem disk)=>disk.identifier==Small?64:256;
    private static readonly ConditionalWeakTable<GameItem,DiskAreaInventory> data=new();
    private sealed class BaySet { internal GameSlotInventory[] Slots=null!; }
    private static readonly ConditionalWeakTable<GameItem,BaySet> drives=new();
    internal static GameSlotInventory[] Bays(GameItem drive)=>drives.GetValue(drive,_=>throw new InvalidOperationException("Missing disk drive graph.")).Slots;
    internal static DiskAreaInventory Data(GameItem disk)=>data.GetValue(disk,_=>throw new InvalidOperationException("Missing disk data graph."));
    internal static GameItem? HostDrive(GameItem disk)
    { var owner=disk.parentInventory?.GetParentItem(); return owner!=null && IsDrive(owner)?owner:null; }
    internal static int Cells(GridShape shape)
    {
        int count=0; for(int y=0;y<shape.height;y++) for(int x=0;x<shape.width;x++) if(shape.GetLocal(x,y)==1) count++;
        return count;
    }
    internal static void CreateDisk(GameItem disk)
    {
        var inventory=new DiskAreaInventory(disk); data.Add(disk,inventory);
        var window=new PixelWindow(true,UiText.Get("ui_disk_data")); window.Attach(inventory); disk.SetContentWindow(window);
        void Changed(GameItem _,GameInventory __,SlotMarker ___)
        { var host=HostDrive(disk); if(host!=null) ModEntry.ActiveSession?.NativeStorageChanged(host); }
        inventory.onSlotAddItemEndFunc+=Changed; inventory.onSlotRemoveItemEndFunc+=Changed; inventory.onSlotChangeAmountItemFunc+=Changed;
    }
    internal static void CreateDrive(GameItem drive)
    {
        var slots=new GameSlotInventory[6]; var grid=new GridPixelElement(3,2,true).SetSpacing(4,4);
        for(int i=0;i<slots.Length;i++)
        {
            var slot=new GameSlotInventory(1,2); slot.SetMachineryStyle();
            slot.SetBackgroundFadeSprite("Items/items_hydroponic_plant","dna_disk_black_full");
            slot.mayInventoryAddItemFunc=(disk,_)=>IsDisk(disk) && disk.unitCount==1 && GeneralHelper.IsItemEffectivelyOwned(disk)
                && GeneralHelper.IsItemEffectivelyOwned(drive) && ReferenceEquals(drive.parentInventory,GameSession.MainInventory());
            void Changed(GameItem _,GameInventory __,SlotMarker ___) { ModEntry.ActiveSession?.NativeStorageChanged(drive); }
            slot.onSlotAddItemEndFunc+=Changed; slot.onSlotRemoveItemEndFunc+=Changed;
            slots[i]=slot; grid.AttachPos(slot,i%3,i/3);
        }
        drives.Add(drive,new BaySet{Slots=slots});
        var window=new PixelWindow(true,UiText.Get("ui_drive_data")); window.Attach(grid); drive.SetContentWindow(window);
    }
}

// A real native item list/save graph with area accounting instead of spatial packing.
internal sealed class DiskAreaInventory : GameGridInventory
{
    internal GameItem Disk { get; }
    internal int Used=>childItems.Sum(i=>DiskStorage.Cells(i.modifiedShape));
    internal DiskAreaInventory(GameItem disk):base(1,1) { Disk=disk; }
    private bool Budget(GameItem item,GridShape shape)
    {
        int occupied=DiskStorage.Cells(shape);
        int existing=ReferenceEquals(item.parentInventory,this)?DiskStorage.Cells(item.modifiedShape):0;
        return occupied>0 && DeepSpaceAE.Core.Network.CellCapacity.Fits(DiskStorage.Capacity(Disk),Used,existing,occupied);
    }
    public override bool SupportsGroupPlacement=>false;
    public override bool MayHaveValidInventorySlot(GameItem item)
    {
        if(item==null || item.unitCount<=0 || IsInsertLocked() || GraphUtils.FindParentEquals(this,item)
            || !GeneralHelper.MayOwnershipAcceptItem(item,this) || DiskStorage.IsDisk(item) || DiskStorage.IsDrive(item)
            || item.FindAllChildItems().Any(i=>DiskStorage.IsDisk(i) || DiskStorage.IsDrive(i))) return false;
        if(item.mayInventoryAddItemFunc!=null && !item.mayInventoryAddItemFunc.InvokeAllReduce(item,this,InvokeFuncExtensions.BoolReduceOR)) return false;
        if(mayInventoryAddItemFunc!=null && !mayInventoryAddItemFunc.InvokeAllReduce(item,this,InvokeFuncExtensions.BoolReduceOR)) return false;
        return Budget(item,item.modifiedShape);
    }
    public override SlotMarker TryFindOneValidInventorySlot(GameItem item)=>TryInventorySlot(item,item.unitCount,Vector2.zero,item.modifiedShape,item.modifiedState);
    public override SlotMarker TryInventorySlot(GameItem item,int amount,Vector2 position,GridShape shape,TagSystem state)
    {
        var slot=new SlotMarker{item=item,inventory=this,itemGridShape=shape};
        if(amount>=item.unitCount && CheckInventorySlotStillValid(item,shape,state) && item.MaxNumAdd(slot)>=item.unitCount) slot.numTransfer=item.unitCount;
        return slot;
    }
    public override bool CheckInventorySlotStillValid(GameItem item,GridShape shape,TagSystem state)
    {
        var slot=new SlotMarker{item=item,inventory=this,itemGridShape=shape};
        return MayHaveValidInventorySlot(item) && Budget(item,shape) && item.CheckIsStateValid(this,slot,shape,state)
            && CheckChangeParentShapeStateModifier(item,slot,shape,state);
    }
    public override bool CheckShapePlacement(GameItem item,GridShape shape,List<GameItem> ignored)=>Budget(item,shape);
    public override bool UncheckedAccept(GameItem item)
    {
        // Loading restores every real instance even after a capacity change; excess is
        // retained and further insertion rejected rather than destroying saved items.
        if(ModEntry.ActiveSession?.Loading==false && !Budget(item,item.modifiedShape)) return false;
        return base.UncheckedAccept(item);
    }
}
