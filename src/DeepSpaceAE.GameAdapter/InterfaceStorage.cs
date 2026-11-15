using System;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

internal static class InterfaceStorage
{
    internal const string Item="deepspaceae.item_interface",Fluid="deepspaceae.fluid_interface";
    internal sealed class State
    {
        internal bool Ready;
        internal InterfaceDepositInventory? Inventory;
        internal GameItem? PreviewItem;
        internal int PreviewRevision=-1,PreviewUnits,Preview;
    }
    private static readonly ConditionalWeakTable<GameItem,State> data=new();
    internal static bool IsItem(GameItem item)=>IsInterface(item) && !PartModes.IsFluid(item);
    internal static bool IsFluid(GameItem item)=>IsInterface(item) && PartModes.IsFluid(item);
    internal static bool IsInterface(GameItem item)=>item.identifier==Item || item.identifier==Fluid;
    internal static State Get(GameItem item)=>data.GetOrCreateValue(item);
    internal static bool Ready(GameItem item)=>data.TryGetValue(item,out var state) && state.Ready;
    internal static int Mode(GameItem item)=>PartStorage.Int(item,"interface_mode");
    internal static bool AutoPurchase(GameItem item)=>PartStorage.Int(item,"interface_auto_purchase")!=0;
    internal static bool Strict(GameItem item)=>PartStorage.Int(item,"interface_strict",1)!=0;
    internal static string Output(GameItem item)=>PartStorage.Text(item,"interface_output");
    internal static void Create(GameItem item)
    {
        var state=Get(item);
        // Initialize the native liquid graph exactly once. Never recreate it on a
        // toggle: doing so would erase composition, temperature or saved settings.
        WaterHelper.InitLiquidContainerItem(item,20000,AcquisitionCatalogPrice(item),isGraduated:true);
        var may=item.mayThisTargetItemFunc; var can=item.canThisTargetItemFunc; var action=item.onThisTargetItemFunc;
        item.maySelectSlotItemFunc=(self,_,_)=>IsFluid(self) && WaterHelper.CanUseWaterContainer(self);
        item.canSelectSlotItemFunc=(self,_,_)=>IsFluid(self) && WaterHelper.CanUseWaterContainer(self);
        item.mayThisTargetItemFunc=(self,target)=>IsFluid(self) && may?.Invoke(self,target)==true;
        item.canThisTargetItemFunc=(self,target)=>IsFluid(self) && can?.Invoke(self,target)==true;
        item.onThisTargetItemFunc=(self,target)=>{ if(IsFluid(self)) action?.Invoke(self,target); };
        AudioHelper.InitSolidMetal(item);
        state.Inventory=new InterfaceDepositInventory(item);
        var graph=new PixelWindow(true,UiText.Get("item_interface_name"));
        graph.Attach(state.Inventory); item.SetContentWindow(graph);
        state.Ready=true;
    }
    private static int AcquisitionCatalogPrice(GameItem item)=>DeepSpaceAE.Core.Network.AcquisitionCatalog.Price(item.identifier);
}

// A native container destination that routes writes to a real network inventory.
// Its saved inventory stays empty; TryAcceptOnce is redirected before native Expel.
internal sealed class InterfaceDepositInventory : GameGridInventory
{
    internal GameItem Owner { get; }
    internal InterfaceDepositInventory(GameItem owner):base(1,1) { Owner=owner; }
    public override bool SupportsGroupPlacement=>false;
    public override bool MayHaveValidInventorySlot(GameItem item)=>ModEntry.ActiveSession?.PreviewInterface(Owner,item)>0;
    public override SlotMarker TryFindOneValidInventorySlot(GameItem item)=>TryInventorySlot(item,item.unitCount,Vector2.zero,item.modifiedShape,item.modifiedState);
    public override SlotMarker TryInventorySlot(GameItem item,int amount,Vector2 position,GridShape shape,TagSystem state)
        =>new SlotMarker{inventory=this,item=item,itemGridShape=shape,
            numTransfer=Math.Max(0,Math.Min(amount<0?item.unitCount:amount,ModEntry.ActiveSession?.PreviewInterface(Owner,item) ?? 0))};
    public override bool UncheckedAccept(GameItem item)=>false;
    public override bool CheckInventorySlotStillValid(GameItem item,GridShape shape,TagSystem state)=>false;
}
