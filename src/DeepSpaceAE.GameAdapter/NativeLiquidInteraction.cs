using System;
using HarmonyLib;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

// Keep the native select -> target -> callbacks path. Only network authorization
// and invalidation are added; this code never edits component tags or pours twice.
internal static class NativeLiquidSelection
{
    internal static GameItem? Source, Terminal;
    internal static VirtualInventory.ItemTile? Hit(GameItem item)
    {
        if(ModEntry.ActiveSession?.IsLiquidEntry(item)!=true || RenderHandler.current==null) return null;
        var tile=RenderHandler.RaycastFindParentElement<VirtualInventory.ItemTile>(Input.mousePosition);
        return tile?.Surface.IsLiquid==true && ReferenceEquals(tile.Item,item)
            && ReferenceEquals(tile.Surface.At(Input.mousePosition),item)?tile:null;
    }
    internal static void Clear() { Source=null; Terminal=null; }
    internal static void EndFor(GameItem? terminal)
    { if(terminal!=null && ReferenceEquals(Terminal,terminal)) { Clear(); ItemSelectHandler.current?.EndSelect(); } }
}
[HarmonyPatch(typeof(ItemSelectHandler),"SelectItem")]
internal static class LiquidSelectionPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameItem __0)
    {
        NativeLiquidSelection.Clear(); var tile=NativeLiquidSelection.Hit(__0);
        if(tile!=null) { NativeLiquidSelection.Source=__0; NativeLiquidSelection.Terminal=tile.Surface.Owner; }
    }
}
[HarmonyPatch(typeof(ItemSelectHandler),"OnEventReset")]
internal static class LiquidSelectionResetPatch
{
    [HarmonyPostfix]
    private static void Postfix()=>NativeLiquidSelection.Clear();
}
[HarmonyPatch(typeof(GameItem),nameof(GameItem.Target))]
internal static class LiquidNativeTargetPatch
{
    [HarmonyPrefix]
    private static bool Prefix(GameItem __instance,GameItem __0,out bool __state)
    {
        __state=false; var targetTile=__0==null?null:NativeLiquidSelection.Hit(__0);
        var sourceTerminal=ReferenceEquals(NativeLiquidSelection.Source,__instance)?NativeLiquidSelection.Terminal:null;
        if(sourceTerminal==null && targetTile==null) return true;
        return ModEntry.ActiveSession?.BeginLiquidAction(__instance,__0!,sourceTerminal,targetTile?.Surface.Owner,out __state)==true;
    }
    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception,bool __state)
    { if(__state) ModEntry.ActiveSession?.EndLiquidAction(__exception); return __exception; }
}
[HarmonyPatch(typeof(WaterHelper),nameof(WaterHelper.TransferLiquid))]
internal static class LiquidNativeTransferPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameItem __0,GameItem __1)=>ModEntry.ActiveSession?.LiquidChanged(__0,__1);
}
[HarmonyPatch(typeof(GameItemElement),nameof(GameItemElement.GetTooltip))]
internal static class LiquidTooltipPatch
{
    [HarmonyPrefix]
    internal static bool Prefix(GameItemElement __instance,ref RichText __result)
    {
        var tile=NativeLiquidSelection.Hit(__instance); if(tile==null) return true;
        __result=tile.GetTooltip(); return false;
    }
}
[HarmonyPatch(typeof(GameItemElement),nameof(GameItemElement.GetTooltipAdvanced))]
internal static class LiquidAdvancedTooltipPatch
{
    [HarmonyPrefix]
    private static bool Prefix(GameItemElement __instance,ref RichText __result)=>LiquidTooltipPatch.Prefix(__instance,ref __result);
}

internal sealed partial class GameSession
{
    internal bool IsLiquidEntry(GameItem item)=>terminalGrid?.IsLiquid==true && displayedView?.LiquidPolicies.ContainsKey(item)==true;
    internal void LiquidChanged(GameItem source,GameItem target)
    {
        if(openTerminal==null) return;
        if((source.parentInventory!=null && observed.Contains(source.parentInventory))
            || (target.parentInventory!=null && observed.Contains(target.parentInventory)))
        { terminalDirty=true; ScheduleTerminalRefresh(); }
    }
    internal bool BeginLiquidAction(GameItem source,GameItem target,GameItem? sourceTerminal,GameItem? targetTerminal,out bool entered)
    {
        entered=false;
        try
        {
            if(writing || writeFault) throw new Rejected(UiText.Get("error_transfer_locked"));
            if(target==null || !GeneralHelper.IsItemEffectivelyOwned(source) || !GeneralHelper.IsItemEffectivelyOwned(target))
                throw new Rejected(UiText.Get("error_not_owned"));
            // Native empty droppers draw from their target; all other native tools
            // apply to the target. Preserve the game's own volume/filter semantics.
            bool reverse=WaterHelper.IsFaucet(target) || (target.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)
                && source.IsTag(WaterHelper.LIQUID_CONTAINER_DROPPER_TAG) && WaterHelper.IsEmpty(source));
            void Check(GameItem terminal,GameItem container,bool? extracts)
            {
                var view=Read(terminal);
                if(!ReferenceEquals(openTerminal,terminal) || !view.LiquidPolicies.TryGetValue(container,out var policies))
                    throw new Rejected(UiText.Get("error_item_inaccessible"));
                var tags=PartStorage.Tags(container);
                if(!policies.Exists(p=>extracts.HasValue?(extracts.Value?p.CanExtract(container.identifier,tags):p.CanInsert(container.identifier,tags)):
                    p.CanExtract(container.identifier,tags) || p.CanInsert(container.identifier,tags)))
                    throw new Rejected(UiText.Get("error_liquid_access"));
            }
            if(sourceTerminal!=null) Check(sourceTerminal,source,!reverse);
            if(targetTerminal!=null) Check(targetTerminal,target,source.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)?reverse:(bool?)null);
            writing=true; entered=true; return true;
        }
        catch(Exception e) { Error(e); return false; }
    }
    internal void EndLiquidAction(Exception? error)
    {
        if(error!=null) { writeFault=true; log.Error("Native liquid action unknown; terminal writes locked: "+Diagnostics.ConsoleText(error.ToString())); }
        writing=false; terminalDirty=openTerminal!=null; ScheduleTerminalRefresh(); RefreshPartLater();
    }
}
