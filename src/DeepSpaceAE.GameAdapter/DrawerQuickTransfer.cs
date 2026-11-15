using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DeepSpaceAE.GameAdapter;

internal static class DrawerQuickTransfer
{
    // Resolve one real top instance. No catalog construction, stock scan or
    // renderer borrowing is needed to use native quick-transfer destinations.
    internal static int Take(GameItem drawer,int destination)
    {
        if(!DrawerStorage.IsDrawer(drawer) || !GeneralHelper.IsItemEffectivelyOwned(drawer)
            || ModEntry.ActiveSession?.Loading!=false || MapUIManager.Instance?.IsOutside==true) return 0;
        var stock=DrawerStorage.Data(drawer); var item=stock.First;
        if(item==null || stock.IsRemoveLocked() || !item.MayRemove() || item.MaxNumRemove()<=0) return 0;
        GraphNodeStorage? target=destination==1?EmporiumEntry.Instance.showcaseWindow
            :destination==2?EmporiumEntry.Instance.backInvWindow:EmporiumEntry.Instance.invWindow;
        var slot=target?.TryFindOneValidInventorySlot(item);
        return slot?.TryAcceptOnce(item.unitCount) ?? 0;
    }
    internal static bool Release(ISet<KeyCode> released,KeyCode click,bool quick,ref GameItem current,int destination)
    {
        if(!quick || !released.Contains(click) || ItemPinHelper.BlocksClicks || current==null
            || !DrawerStorage.IsDrawer(current) || RenderHandler.RaycastFindParentElement<VirtualInventory.ItemTile>(Input.mousePosition)!=null
            || !ReferenceEquals(current,RenderHandler.RaycastElement<GameItem>(Input.mousePosition))) return false;
        Take(current,destination); current=null!;
        return true;
    }
}

[HarmonyPatch(typeof(ItemQuickTransferHandler),"OnEventRelease")]
internal static class DrawerQuickInventoryPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ItemQuickTransferHandler __instance,ISet<KeyCode> keysPressed,bool ___isQuickTransfer,ref GameItem ___currentItem)
    {
        if(!DrawerQuickTransfer.Release(keysPressed,__instance.key,___isQuickTransfer,ref ___currentItem,0)) return true;
        InputActionManager.current.ResetAfter(__instance); return false;
    }
}
[HarmonyPatch(typeof(ItemQuickTransferAltHandler),"OnEventRelease")]
internal static class DrawerQuickShowcasePatch
{
    [HarmonyPrefix]
    private static bool Prefix(ItemQuickTransferAltHandler __instance,ISet<KeyCode> keysPressed,bool ___isQuickTransfer,ref GameItem ___currentItem)
    {
        if(!DrawerQuickTransfer.Release(keysPressed,__instance.key,___isQuickTransfer,ref ___currentItem,1)) return true;
        InputActionManager.current.ResetAfter(__instance); return false;
    }
}
[HarmonyPatch(typeof(ItemQuickTransferAltHandler1),"OnEventRelease")]
internal static class DrawerQuickStoragePatch
{
    [HarmonyPrefix]
    private static bool Prefix(ItemQuickTransferAltHandler1 __instance,ISet<KeyCode> keysPressed,bool ___isQuickTransfer,ref GameItem ___currentItem)
    {
        if(!DrawerQuickTransfer.Release(keysPressed,__instance.key,___isQuickTransfer,ref ___currentItem,2)) return true;
        InputActionManager.current.ResetAfter(__instance); return false;
    }
}
