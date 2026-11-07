using System;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

internal static class InterfaceStorage
{
    internal const string Item="deepspaceae.item_interface",Fluid="deepspaceae.fluid_interface";
    internal sealed class State
    {
        internal InterfaceDepositInventory? Inventory;
        internal GameItem? PreviewItem;
        internal int PreviewRevision=-1,PreviewUnits,Preview;
    }
    private static readonly ConditionalWeakTable<GameItem,State> data=new();
    internal static bool IsItem(GameItem item)=>item.identifier==Item;
    internal static bool IsFluid(GameItem item)=>item.identifier==Fluid;
    internal static bool IsInterface(GameItem item)=>IsItem(item) || IsFluid(item);
    internal static State Get(GameItem item)=>data.GetOrCreateValue(item);
    internal static int Mode(GameItem item)=>PartStorage.Int(item,"interface_mode");
    internal static bool AutoPurchase(GameItem item)=>PartStorage.Int(item,"interface_auto_purchase")!=0;
    internal static bool Strict(GameItem item)=>PartStorage.Int(item,"interface_strict",1)!=0;
    internal static string Output(GameItem item)=>PartStorage.Text(item,"interface_output");
    internal static void Create(GameItem item)
    {
        var state=Get(item);
        if(IsFluid(item))
        {
            // Native chemical components, temperature, selection/target actions,
            // inspection and persistence. No parallel liquid representation.
            WaterHelper.InitLiquidContainerItem(item,20000,AcquisitionCatalogPrice(item),isGraduated:true);
            AudioHelper.InitSolidMetal(item);
        }
        else
        {
            state.Inventory=new InterfaceDepositInventory(item);
            var graph=new PixelWindow(true,UiText.Get("item_interface_name"));
            graph.Attach(state.Inventory); item.SetContentWindow(graph);
        }
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
