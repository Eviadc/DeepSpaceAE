using System;
using System.Linq;
namespace DeepSpaceAE.GameAdapter;

// One native item identity, inventory graph and set of upgrades in both modes.
// Native right-click -> Toggle calls this same guarded path as the settings button.
internal static class PartModes
{
    internal static bool Supports(GameItem item)=>InterfaceStorage.IsInterface(item)
        || IoBus.IsBus(item) && !IoBus.IsPlane(item);
    internal static bool IsFluid(GameItem item)=>Supports(item)
        && PartStorage.Int(item,"fluid_form",item.identifier==InterfaceStorage.Fluid?1:0)!=0;
    internal static void Create(GameItem item)
    {
        if(!Supports(item)) return;
        item.mayToggleSlotItemFunc=(_,_,_)=>true;
        item.canToggleSlotItemFunc=(self,_,_)=>CanToggle(self);
        item.onToggleSlotItemFunc=(self,_,_)=>ModEntry.ActiveSession?.ToggleForm(self);
        Refresh(item);
        item.ValidateShapeState();
    }
    internal static bool CanToggle(GameItem item)=>Supports(item) && GeneralHelper.IsItemEffectivelyOwned(item)
        && !(InterfaceStorage.IsFluid(item) && WaterHelper.GetTotalVolume(item)>0);
    internal static string Sprite(GameItem item)
    {
        if(InterfaceStorage.IsInterface(item)) return IsFluid(item)?"fluid-interface":"item-interface";
        var sprite=ModEntry.Parts.First(p=>p.Id==item.identifier).Sprite;
        return IsFluid(item)?"fluid-"+sprite:sprite;
    }
    internal static void Refresh(GameItem item)
    {
        if(!Supports(item) || InterfaceStorage.IsInterface(item) && !InterfaceStorage.Ready(item)) return;
        bool fluid=IsFluid(item);
        string key=InterfaceStorage.IsInterface(item)?(fluid?"item_fluid_interface_name":"item_interface_name")
            :fluid?(item.identifier==IoBus.Import?"item_fluid_import_name":item.identifier==IoBus.Export?"item_fluid_export_name":"item_fluid_bus_name")
            :ModEntry.Parts.First(p=>p.Id==item.identifier).NameKey;
        item.name=UiText.Get(key);
        item.shortDescription=UiText.Get(InterfaceStorage.IsInterface(item)?"item_interface_modes_desc":"item_bus_modes_desc");
        if(InterfaceStorage.IsInterface(item))
            item.ModifyTag(WaterHelper.LIQUID_CONTAINER_TAG,t=>{ if(fluid) t.Enable(); else t.Disable(); },true);
    }
}

internal sealed partial class GameSession
{
    internal void ToggleForm(GameItem item)
    {
        if(Loading || writing || !PartModes.Supports(item) || !GeneralHelper.IsItemEffectivelyOwned(item)) return;
        if(!PartModes.CanToggle(item)) { ShowNotice(UiText.Get("ui_form_empty_first")); return; }
        NativeLiquidSelection.Clear(); ItemSelectHandler.current?.EndSelect();
        PartStorage.Number(item,"fluid_form",PartModes.IsFluid(item)?0:1);
        PartModes.Refresh(item);
        item.ValidateShapeState();
        AutomationStructureChanged(); Visuals.Inactive(item); Visuals.Invalidate(); NativeStorageChanged(item,true);
        if(ReferenceEquals(displayedPart,item)) OpenPart(item);
    }
}
