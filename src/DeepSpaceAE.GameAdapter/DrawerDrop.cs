using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace DeepSpaceAE.GameAdapter;

internal static class DrawerDrop
{
    internal static DrawerInventory? Target(GameItem? hit,Vector2 pointer,TreeNodeRender? ignore=null)
    {
        if(hit?.parentInventory is not DrawerInventory drawer) return null;
        // A terminal mirror has the same item identity, but is not this drawer's
        // local inventory. Never turn a terminal tile into a direct storage route.
        return ReferenceEquals(RenderHandler.RaycastFindParentElement<DrawerInventory>(pointer,ignore),drawer)?drawer:null;
    }
    internal static bool UsesItem(GameItem incoming,GameItem hit)
        =>incoming.MayTarget(hit) && incoming.CanTarget(hit);

    internal static bool DropGroup(ItemMultiSelectHandler handler,List<GameItemElement> selected)
    {
        var pointer=(Vector2)Input.mousePosition;
        var hit=RenderHandler.RaycastElement<GameItem>(pointer);
        var drawer=Target(hit,pointer);
        if(drawer==null || selected.Any(item=>ReferenceEquals(item,hit) || UsesItem(item,hit!))) return false;
        try
        {
            if(!GeneralHelper.MayPlayerInsertInto(drawer)) return true;
            foreach(var item in selected.ToArray())
                if(!item.IsDestroyed() && item.parentInventory!=null && !ReferenceEquals(item.parentInventory,drawer)
                    && item.MayRemove() && item.MaxNumRemove()>0) GraphUtils.TryAcceptAll(drawer,item);
        }
        finally { handler.Cancel(); ModEntry.ActiveSession?.DragEnded(); }
        return true;
    }
}

[HarmonyPatch(typeof(ItemMouseDragHandler),"Drag")]
internal static class DrawerDragTargetPatch
{
    [HarmonyPostfix]
    private static void Postfix(ItemMouseDragHandler __instance,bool ___isDragging,GameItemElement ___currentItem,
        int ___currentRemove,ref GameInventory ___lastInventory,ref GameItem ___lastItem,ref SlotMarker ___lastSlot,
        HighlightManager ___highlightManager,RichTextBuilder ___builder)
    {
        if(!___isDragging || ___currentItem==null) return;
        var drawer=DrawerDrop.Target(___lastItem,Input.mousePosition,___currentItem.fakeBackground);
        if(drawer==null || ReferenceEquals(___currentItem.parentInventory,drawer) || DrawerDrop.UsesItem(___currentItem,___lastItem)) return;
        ___lastInventory=drawer; ___lastItem=null!;
        ___lastSlot=drawer.TryInventorySlot(___currentItem,___currentRemove,___currentItem.GetFakeRotatorCenteredPosition(),
            ___currentItem.tempShape,___currentItem.modifiedState);
        bool accepts=GeneralHelper.MayPlayerInsertInto(drawer) && ___lastSlot.numTransfer>0;
        ___highlightManager.Clear();
        ___highlightManager.Try(___lastSlot,accepts?RenderHandler.BackgroundColor.Green:RenderHandler.BackgroundColor.Red);
        var offset=__instance.tooltipTranslation+new Vector2(___currentItem.tempShape.globalWidth*UISettings.current.gridSize/2,0);
        OverlayHandler.current.ShowTooltip(Input.mousePosition,offset,
            RichTextGenerator.InsertItemInOtherItem(___currentItem,drawer.Drawer,accepts,___builder));
        // EndDrag rechecks this actual inventory and invokes the native SlotMarker.
    }
}

[HarmonyPatch(typeof(ItemMultiSelectHandler),"UpdateDropTarget")]
internal static class DrawerGroupTargetPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameItem ___hoverItem,List<GameItemElement> ___selectedItems,List<TreeNodeRender> ___hoverHighlightNodes)
    {
        var drawer=DrawerDrop.Target(___hoverItem,Input.mousePosition);
        if(drawer==null || ___selectedItems.Any(item=>ReferenceEquals(item,___hoverItem) || DrawerDrop.UsesItem(item,___hoverItem))) return;
        bool accepts=GeneralHelper.MayPlayerInsertInto(drawer)
            && ___selectedItems.Any(item=>!ReferenceEquals(item.parentInventory,drawer) && drawer.MayHaveValidInventorySlot(item));
        ___hoverHighlightNodes.Clear();
        ___hoverHighlightNodes.AddRange(drawer.HighlightWholeInventory(accepts?RenderHandler.BackgroundColor.Green:RenderHandler.BackgroundColor.Red));
    }
}
