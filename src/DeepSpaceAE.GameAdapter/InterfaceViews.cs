using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

internal sealed partial class GameSession
{
    private static string RecipeName(LiquidSnapshot liquid)=>string.Join(" + ",liquid.Composition.Parts.Select(p=>
        (Liquid.LiquidDict.TryGetValue(p.Key,out var definition)?definition.displayName:p.Key)+" "+
        ((decimal)p.Value*100/Math.Max(1,liquid.Composition.Volume)).ToString("0.######")+"%"));
    private void InterfaceSetting(GameItem device,string key,int value)
    { PartStorage.Number(device,key,value); NativeStorageChanged(device); Visuals.Invalidate(); }
    private void ShowInterface(GameItem device)
    {
        ClosePart(); displayedPart=device;
        View? view=null; string status;
        try { view=ReadInterface(device); status=UiText.Get("ui_interface_online"); }
        catch(Rejected e) { status=e.Message; }
        var elements=new List<PixelElement>();
        elements.Add(MachineryStack.Horizontal(NativeWidgets.Icon(NativeWidgets.Sprite(InterfaceStorage.IsFluid(device)?"fluid-interface":"item-interface"),32,32),NativeWidgets.Text(status)));
        if(InterfaceStorage.IsFluid(device))
        {
            var liquid=LiquidStorage.Read(device); bool output=InterfaceStorage.Mode(device)==1;
            elements.Add(NativeWidgets.Dropdown(new[]{UiText.Get("ui_interface_input"),UiText.Get("ui_interface_output")},output?1:0,i=>InterfaceSetting(device,"interface_mode",i),240));
            if(!output)
                elements.Add(NativeWidgets.Toggle(UiText.Get("ui_interface_strict"),InterfaceStorage.Strict(device),v=>InterfaceSetting(device,"interface_strict",v?1:0),240));
            else
            {
                var recipes=new List<(string Signature,string Label)>{("",UiText.Get("ui_interface_no_output"))};
                if(view!=null)
                    foreach(var source in LiquidRoutes(view,true))
                    {
                        var entry=LiquidStorage.Read(source);
                        if(entry.Composition.Volume>0 && recipes.All(r=>r.Signature!=entry.Composition.Signature))
                            recipes.Add((entry.Composition.Signature,RecipeName(entry)));
                    }
                string selected=InterfaceStorage.Output(device);
                if(selected.Length>0 && recipes.All(r=>r.Signature!=selected))
                    recipes.Add((selected,PartStorage.Text(device,"interface_output_name")+UiText.Get("ui_interface_unavailable")));
                int index=recipes.FindIndex(r=>r.Signature==selected);
                elements.Add(NativeWidgets.Dropdown(recipes.Select(r=>r.Label).ToArray(),Math.Max(0,index),i=>{
                    PartStorage.String(device,"interface_output",recipes[i].Signature);
                    PartStorage.String(device,"interface_output_name",recipes[i].Label);
                    NativeStorageChanged(device); Visuals.Invalidate();
                },290));
                if(liquid.Composition.Volume>0 && selected.Length>0 && liquid.Composition.Signature!=selected)
                    elements.Add(NativeWidgets.Text(UiText.Get("ui_interface_empty_first")));
            }
            elements.Add(new MachineryProgressBar(Math.Min(1,(float)liquid.Composition.Volume/Math.Max(1,liquid.Capacity)),180));
            elements.Add(NativeWidgets.Text(LiquidSnapshot.Volume(liquid.Composition.Volume)+" / "+LiquidSnapshot.Volume(liquid.Capacity)));
            elements.Add(NativeWidgets.Text(liquid.Description));
            elements.Add(NativeWidgets.Text(UiText.Get(output?"ui_interface_output_hint":"ui_interface_input_hint")));
        }
        else
        {
            elements.Add(NativeWidgets.Text(UiText.Get("ui_item_interface_hint")));
            elements.Add(NativeWidgets.Toggle(UiText.Get("ui_interface_auto_purchase"),InterfaceStorage.AutoPurchase(device),
                v=>InterfaceSetting(device,"interface_auto_purchase",v?1:0),240));
            elements.Add(NativeWidgets.Text(UiText.Get("ui_interface_purchase_hint")));
            elements.Add(NativeWidgets.Text(UiText.Get("ui_interface_last",PartStorage.Int(device,"interface_moved"))));
            string last=PartStorage.Text(device,"interface_status");
            if(last.Length>0) elements.Add(NativeWidgets.Text(last));
        }
        string title=UiText.Get(InterfaceStorage.IsFluid(device)?"item_fluid_interface_name":"item_interface_name");
        partWindow=MachineHelper.CreateMachineryWindow(true,MachineHelper.Panel(title,MachineryStack.Vertical(elements.ToArray()),"interface",false));
        partWindow.closeCallback=ClosePart; partWindow.Show().Center();
    }
}
