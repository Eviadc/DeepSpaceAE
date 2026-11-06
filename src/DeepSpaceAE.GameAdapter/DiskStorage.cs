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
    private sealed class BaySet { internal GameSlotInventory[] Slots=null!; internal GameGridInventory Cards=null!; }
    private static readonly ConditionalWeakTable<GameItem,BaySet> drives=new();
    internal static GameSlotInventory[] Bays(GameItem drive)=>drives.GetValue(drive,_=>throw new InvalidOperationException("Missing disk drive graph.")).Slots;
    internal static GameGridInventory Upgrades(GameItem drive)=>drives.GetValue(drive,_=>throw new InvalidOperationException("Missing disk drive graph.")).Cards;
    internal static int Unlocked(GameItem drive)=>Math.Min(10,2+2*Upgrades(drive).childItems.Count);
    private static bool CanRemoveUpgrade(GameItem drive)=>Bays(drive).Skip(Math.Max(2,Unlocked(drive)-2)).All(slot=>slot.childItem==null);
    private static void RefreshBays(GameItem drive)
    {
        var slots=Bays(drive); int unlocked=Unlocked(drive);
        for(int i=0;i<slots.Length;i++) { slots[i].overrideLockInsert=i>=unlocked; slots[i].Validate(); }
    }
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
        var slots=new GameSlotInventory[10]; var grid=new GridPixelElement(2,5,true).SetSpacing(4,4);
        var cards=new GameGridInventory(4,1); cards.SetMachineryStyle();
        cards.mayInventoryAddItemFunc=(card,_)=>card.identifier==PartStorage.Capacity && card.unitCount==1
            && GeneralHelper.IsItemEffectivelyOwned(card) && GeneralHelper.IsItemEffectivelyOwned(drive);
        cards.maxSlotRemoveItemFunc=(_,_,_)=>CanRemoveUpgrade(drive)?int.MaxValue:0;
        void Changed(GameItem _,GameInventory __,SlotMarker ___)
        { RefreshBays(drive); ModEntry.ActiveSession?.NativeStorageChanged(drive,true); }
        cards.onSlotAddItemEndFunc+=Changed; cards.onSlotRemoveItemEndFunc+=Changed;
        for(int i=0;i<slots.Length;i++)
        {
            int index=i;
            var slot=new GameSlotInventory(2,1); slot.SetMachineryStyle();
            NativeWidgets.SlotGhost(slot,"disk-ghost");
            slot.overrideLockInsert=i>=2;
            slot.mayInventoryAddItemFunc=(disk,_)=>index<Unlocked(drive) && (slot.childItem==null || ReferenceEquals(slot.childItem,disk)) && IsDisk(disk) && disk.unitCount==1 && GeneralHelper.IsItemEffectivelyOwned(disk)
                && GeneralHelper.IsItemEffectivelyOwned(drive);
            slot.onSlotAddItemEndFunc+=Changed; slot.onSlotRemoveItemEndFunc+=Changed;
            slots[i]=slot; grid.AttachPos(slot,i%2,i/2);
        }
        drives.Add(drive,new BaySet{Slots=slots,Cards=cards});
        var graph=new GridPixelElement(1,2,true); graph.AttachPos(grid,0,0); graph.AttachPos(cards,0,1);
        var window=new PixelWindow(true,UiText.Get("ui_drive_data")); window.Attach(graph); drive.SetContentWindow(window);
    }
}

// A real native item list/save graph with area accounting instead of spatial packing.
internal sealed class DiskAreaInventory : GameGridInventory
{
    internal GameItem Disk { get; }
    internal int Used=>childItems.Sum(i=>DiskStorage.Cells(i.modifiedShape));
    internal DiskAreaInventory(GameItem disk):base(1,1) { Disk=disk; }
    internal bool CanReplace(GameItem existing,GameItem incoming) => !DiskStorage.IsDisk(incoming) && !DiskStorage.IsDrive(incoming)
        && !incoming.FindAllChildItems().Any(i=>DiskStorage.IsDisk(i) || DiskStorage.IsDrive(i))
        && !GraphUtils.FindParentEquals(this,incoming)
        && DeepSpaceAE.Core.Network.CellCapacity.Fits(DiskStorage.Capacity(Disk),Used,DiskStorage.Cells(existing.modifiedShape),DiskStorage.Cells(incoming.modifiedShape));
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
        if(MayHaveValidInventorySlot(item) && Budget(item,shape)
            && item.CheckIsStateValid(this,slot,shape,state) && CheckAcceptParentShapeStateModifier(item,slot,shape,state))
            slot.numTransfer=Math.Max(0,Math.Min(amount<0?item.unitCount:Math.Min(amount,item.unitCount),Math.Min(item.MaxNumRemove(),item.MaxNumAdd(slot))));
        return slot;
    }
    public override bool CheckInventorySlotStillValid(GameItem item,GridShape shape,TagSystem state)
    {
        var slot=new SlotMarker{item=item,inventory=this,itemGridShape=shape};
        return (ReferenceEquals(item.parentInventory,this) || MayHaveValidInventorySlot(item) && Budget(item,shape))
            && item.CheckIsStateValid(this,slot,shape,state)
            && CheckChangeParentShapeStateModifier(item,slot,shape,state);
    }
    public override bool CheckShapePlacement(GameItem item,GridShape shape,List<GameItem> ignored)=>ReferenceEquals(item.parentInventory,this) || Budget(item,shape);
    public override bool UncheckedAccept(GameItem item)
    {
        // Loading restores every real instance even after a capacity change; excess is
        // retained and further insertion rejected rather than destroying saved items.
        if(ModEntry.ActiveSession?.Loading==false && !Budget(item,item.modifiedShape)) return false;
        return base.UncheckedAccept(item);
    }
}
