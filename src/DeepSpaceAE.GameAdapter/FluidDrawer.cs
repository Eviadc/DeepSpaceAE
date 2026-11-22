using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DeepSpaceAE.Core.Network;
using HarmonyLib;
using UnityEngine;

namespace DeepSpaceAE.GameAdapter;

internal static class FluidDrawer
{
    internal const string Id="deepspaceae.fluid_drawer";
    private const int BaseMl=1000;
    private static readonly ConditionalWeakTable<GameItem,GameGridInventory> upgrades=new();
    internal static bool IsDrawer(GameItem? item)=>item?.identifier==Id;
    internal static GameGridInventory Upgrades(GameItem item)=>upgrades.GetValue(item,_=>throw new InvalidOperationException("Missing fluid drawer upgrade bay."));
    private static int Capacity(int cards)=>BaseMl*1000*(1<<(2*Math.Max(0,Math.Min(5,cards))));

    internal static bool CanRemove(GameItem drawer,int count)
        =>WaterHelper.GetTotalVolume(drawer)<=Capacity(Upgrades(drawer).childItems.Count-count);

    internal static GameItem Create()
    {
        var item=ItemDirectory.CreateEmptyItem(Id);
        item.name=UiText.Get("item_fluid_drawer_name"); item.shortDescription=UiText.Get("item_fluid_drawer_desc");
        item.unitValue=AcquisitionCatalog.Price(Id);
        item.SetSpriteAndShapeFromMod(ModEntry.ModId,"fluid-drawer");
        item.SetShape(new GridShapeBuilder(2,2).SetDataFill(1).Build());
        WaterHelper.InitLiquidContainerItem(item,BaseMl,AcquisitionCatalog.Price(Id),isGraduated:true);
        item.mayStackItemFunc=(_,_)=>false;
        item.activateDefault=false;
        item.mayActivateSlotItemFunc=(_,_,_)=>false;
        item.canActivateSlotItemFunc=(_,_,_)=>false;
        var cards=new GameGridInventory(5,1);
        upgrades.Add(item,cards);
        cards.EnableTag(HopperHelper.NO_HOPPER_INVENTORY_TAG);
        cards.mayInventoryAddItemFunc=(card,_)=>card.identifier==PartStorage.Capacity && card.unitCount==1
            && GeneralHelper.IsItemEffectivelyOwned(item) && GeneralHelper.IsItemEffectivelyOwned(card);
        cards.maxSlotRemoveItemFunc=(_,_,_)=>CanRemove(item,1)?1:0;
        void Changed(GameItem card,GameInventory inventory,SlotMarker slot)
        {
            item.ModifyTag(WaterHelper.LIQUID_CONTAINER_CAPACITY,t=>t.Enable().SetInt(Capacity(cards.childItems.Count)));
            ModEntry.ActiveSession?.NativeStorageChanged(item);
        }
        cards.onSlotAddItemEndFunc+=Changed; cards.onSlotRemoveItemEndFunc+=Changed;
        var graph=new PixelWindow(true,item.name);
        graph.Attach(cards); item.SetContentWindow(graph);
        item.getItemSpriteModifierForItemFunc=drawer=>
        {
            int volume=WaterHelper.GetTotalVolume(drawer);
            if(volume<=0) return null;
            int capacity=WaterHelper.GetTotalCapacity(drawer);
            int level=(int)Math.Min(4,(4L*volume+capacity-1)/capacity);
            var overlay=NativeWidgets.Sprite("fluid-drawer-level-"+level);
            if(overlay==null) return null;
            var tint=MetaHelper.HexToColor(WaterHelper.GetLiquidContainerColor(drawer));
            return new List<ItemSpriteModifier>{ new(overlay,Vector2.zero,color:tint) };
        };
        return item;
    }
}

// Native precise group placement validates each card before removing the whole group.
[HarmonyPatch(typeof(ItemMultiSelectHandler),"IsFormationValid")]
internal static class FluidDrawerGroupRemovalPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameInventory inventory,List<GameItemElement> ___selectedItems,ref bool __result)
    {
        if(!__result) return;
        foreach(var group in ___selectedItems.Where(card=>card.identifier==PartStorage.Capacity && card.parentInventory!=inventory)
            .GroupBy(card=>card.parentInventory?.GetParentItem()))
        {
            if(FluidDrawer.IsDrawer(group.Key) && !FluidDrawer.CanRemove(group.Key!,group.Count()))
            { __result=false; return; }
        }
    }
}

// Vanilla refreshes its own named bottle sprites here after pours, taps and reagents.
[HarmonyPatch(typeof(SpriteHelper),nameof(SpriteHelper.UpdateWaterContainerSprite))]
internal static class FluidDrawerAppearancePatch
{
    [HarmonyPostfix]
    private static void Postfix(GameItem gameItem)
    {
        if(!FluidDrawer.IsDrawer(gameItem) || gameItem is not GameItemElement drawer) return;
        drawer.HandleInnateModSprite();
        ModEntry.ActiveSession?.NativeStorageChanged(drawer);
    }
}

internal sealed partial class GameSession
{
    private MachineryText? fluidDrawerStatus;
    private void ShowFluidDrawer(GameItem drawer)
    {
        ClosePart(); displayedPart=drawer;
        var cards=new BorrowedInventoryView(FluidDrawer.Upgrades(drawer)); driveBorrowed.Add(cards);
        fluidDrawerStatus=NativeWidgets.Text(LiquidStorage.Read(drawer).Description);
        var pour=new MachineryButton(UiText.Get("ui_fluid_drawer_pour"),()=>{ ClosePart(); ItemSelectHandler.current?.TrySelectItem(drawer); });
        var body=MachineryStack.Vertical(fluidDrawerStatus,pour,NativeWidgets.Text(UiText.Get("ui_upgrades")),new MachineryIntake(cards,true,false));
        partWindow=MachineHelper.CreateMachineryWindow(true,MachineHelper.Panel(drawer.name,body,"fluid_drawer",false));
        partWindow.closeCallback=ClosePart; partWindow.Show().Center();
    }
}
