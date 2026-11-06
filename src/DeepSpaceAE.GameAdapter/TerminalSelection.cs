using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

// Scope is only the terminal where banding began. All other native selections stay native.
internal static class TerminalSelection
{
    internal static VirtualInventory? Surface;
    internal static readonly Dictionary<GameItemElement, ProjectionGesture> Origins = new();
    internal static void Clear()
    { Surface?.ClearSelectionVisuals(); Surface=null; Origins.Clear(); }
    internal static void Cancel(VirtualInventory surface)
    { if(ReferenceEquals(Surface,surface)) { ItemMultiSelectHandler.current?.Cancel(); Clear(); } }
}
[HarmonyPatch(typeof(ItemMultiSelectHandler),"ComputeBandItems")]
internal static class TerminalBandPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Rect __0, GameInventory ___bandInventory, PixelWindow ___bandWindow, ref List<GameItemElement> __result)
    {
        var surface=ModEntry.ActiveSession?.BandSurface(___bandInventory,___bandWindow);
        if(surface==null) return true;
        TerminalSelection.Surface=surface; __result=surface.BandItems(__0); return false;
    }
}
[HarmonyPatch(typeof(ItemMultiSelectHandler),"RefreshSelectionHighlight")]
internal static class TerminalSelectionHighlightPatch
{
    [HarmonyPrefix]
    private static bool Prefix(List<GameItemElement> ___selectedItems, List<TreeNodeRender> ___selectionHighlights)
    {
        var surface=TerminalSelection.Surface; if(surface==null) return true;
        // These nodes are our previous mirror highlights; native cleanup uses the same list.
        surface.ClearSelectionVisuals(); ___selectionHighlights.Clear();
        ___selectionHighlights.AddRange(surface.HighlightSelection(___selectedItems)); return false;
    }
}
[HarmonyPatch(typeof(ItemMultiSelectHandler),"ClearSelection")]
internal static class TerminalClearSelectionPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        bool terminal=TerminalSelection.Surface!=null; TerminalSelection.Clear();
        if(terminal) LetterboxManager.RunNextUpdate(()=>ModEntry.ActiveSession?.DragEnded());
    }
}
[HarmonyPatch(typeof(ItemMultiSelectHandler),"StartGroupDrag")]
internal static class TerminalGroupStartPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ItemMultiSelectHandler __instance,List<GameItemElement> ___selectedItems,Vector3 ___pressPosition)
    {
        var surface=TerminalSelection.Surface; if(surface==null) return true;
        if(!ReferenceEquals(RenderHandler.RaycastFindParentElement<PixelWindow>(___pressPosition),surface.handler.FindParentWindow()))
        { TerminalSelection.Clear(); return true; }
        NativeDrag.Gesture=null;
        if(ModEntry.ActiveSession?.BeginTerminalGroup(surface,___selectedItems)==true) return true;
        __instance.Cancel(); return false;
    }
    [HarmonyPostfix]
    private static void Postfix(ItemMultiSelectHandler __instance,List<GameItemElement> ___selectedItems,Dictionary<GameItemElement,Vector2> ___dragOffsets)
    {
        var surface=TerminalSelection.Surface; if(surface==null || !__instance.IsGroupDragging) return;
        var overlay=RenderHandler.current.overlayRoot.rectTransform; var pointer=overlay.InverseTransformPoint(Input.mousePosition);
        foreach(var item in ___selectedItems)
        {
            if(TerminalSelection.Origins.TryGetValue(item,out var gesture)) item.UpdateFakeReal(gesture.Count);
            var center=overlay.InverseTransformPoint(surface.ItemCenter(item));
            item.SetCenteredPosition(center.x,center.y); ___dragOffsets[item]=new Vector2(center.x-pointer.x,center.y-pointer.y);
        }
    }
}
[HarmonyPatch(typeof(ItemMultiSelectHandler),"EndGroupDrag")]
internal static class TerminalGroupEndPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ItemMultiSelectHandler __instance,List<GameItemElement> ___selectedItems)
    {
        var surface=TerminalSelection.Surface; if(surface==null) return !DrawerDrop.DropGroup(__instance,___selectedItems);
        try { ModEntry.ActiveSession?.DropTerminalGroup(surface,___selectedItems.ToArray()); }
        finally { __instance.Cancel(); ModEntry.ActiveSession?.DragEnded(); }
        return false;
    }
}
internal sealed partial class GameSession
{
    internal bool BeginTerminalGroup(VirtualInventory surface,IReadOnlyList<GameItemElement> selected)
    {
        TerminalSelection.Origins.Clear();
        try
        {
            if(writing) return false;
            var view=Read(surface.Owner);
            if(!PowerView(view)) return false;
            foreach(var item in selected)
            {
                var id=GuidFor(item);
                if(!view.Live.ContainsKey(id) || !view.Sources.TryGetValue(id,out var source)) continue;
                TerminalSelection.Origins[item]=new ProjectionGesture{Surface=surface,Item=item,Source=source,Count=1};
            }
            return TerminalSelection.Origins.Count>0;
        }
        catch(Exception e) { log.Warning(Diagnostics.ConsoleText(e.ToString())); return false; }
    }
    internal void DropTerminalGroup(VirtualInventory surface,IReadOnlyList<GameItemElement> selected)
    {
        var pointer=(Vector2)Input.mousePosition; var hovered=RenderHandler.RaycastElement<GameItem>(pointer);
        if(hovered is GameItemElement element && selected.Contains(element)) hovered=null;
        var inventory=hovered==null?RenderHandler.RaycastFindParentElement<GameInventory>(pointer):null;
        if(DrawerDrop.Target(hovered,pointer) is DrawerInventory drawer && selected.All(item=>!DrawerDrop.UsesItem(item,hovered!)))
        { inventory=drawer; hovered=null; }
        if(ReferenceEquals(inventory,surface)) return; // no physical move within this directory
        var previousItem=NativeDrag.OriginItem; var previousSource=NativeDrag.OriginSource;
        try
        {
            foreach(var item in selected.Distinct())
            {
                if(!TerminalSelection.Origins.TryGetValue(item,out var gesture)) continue;
                NativeDrag.OriginItem=item; NativeDrag.OriginSource=gesture.Source;
                NativeDrop(gesture,inventory as VirtualInventory,item,gesture.Count,inventory,hovered);
            }
        }
        finally { NativeDrag.OriginItem=previousItem; NativeDrag.OriginSource=previousSource; }
    }
}
