using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DeepSpaceAE.Core.Network;
namespace DeepSpaceAE.GameAdapter;

internal sealed class LiquidSnapshot
{
    internal LiquidComposition Composition=null!;
    internal int Capacity;
    internal string Name="", Description="", Stamp="";
    internal bool Readable;
    internal static string Volume(long amount)=>(amount/1000m).ToString("0.###",CultureInfo.CurrentCulture)+" ml";
}
internal static class LiquidStorage
{
    // The legacy drinking-water helper shares the tag but has no chemical amounts.
    // Require the actual modern capacity field, never infer a composition from a name.
    internal static bool IsContainer(GameItem item)=>item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)
        && item.IsTag(WaterHelper.LIQUID_CONTAINER_CAPACITY) && item.GetTagReadonly(WaterHelper.LIQUID_CONTAINER_CAPACITY).GetInt()>0;
    internal static LiquidSnapshot Read(GameItem item)
    {
        var result=new LiquidSnapshot {
            Composition=new LiquidComposition(Liquid.Liquids.Select(l=>new KeyValuePair<string,int>(l.identifier,item.GetTagReadonly(l.currentPart).GetInt()))),
            Capacity=item.GetTagReadonly(WaterHelper.LIQUID_CONTAINER_CAPACITY).GetInt() };
        result.Name=result.Composition.Volume==0?UiText.Get("ui_liquid_empty"):
            string.Join(" + ",result.Composition.Parts.Select(p=>Liquid.LiquidDict.TryGetValue(p.Key,out var liquid)?liquid.displayName:p.Key));
        result.Description=item.name+"\n"+LiquidSnapshot.Volume(result.Composition.Volume)+" / "+LiquidSnapshot.Volume(result.Capacity)+"\n"+
            string.Join("\n",result.Composition.Parts.Select(p=>(Liquid.LiquidDict.TryGetValue(p.Key,out var l)?l.displayName:p.Key)+": "+LiquidSnapshot.Volume(p.Value)+
                " ("+((decimal)p.Value*100/result.Composition.Volume).ToString("0.######",CultureInfo.CurrentCulture)+"%)"));
        if(item.IsTag("CONTAINER_TEMP_TAG")) result.Description+="\n"+item.GetTagReadonly("CONTAINER_TEMP_TAG").GetDouble().ToString("0.###",CultureInfo.CurrentCulture)+" °C";
        result.Stamp=result.Composition.Signature+"/"+result.Composition.Volume+"/"+result.Capacity+"/"+
            item.GetTagReadonly("CONTAINER_TEMP_TAG").GetDouble().ToString("R",CultureInfo.InvariantCulture);
        return result;
    }
}
