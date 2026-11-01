using HarmonyLib;
using UnityEngine;

namespace DeepSpaceAE.GameAdapter;

// The loader applies these typed Harmony patches. They only change this mod's own items.
[HarmonyPatch(typeof(ItemMouseDoubleClickHandler), "DoubleClickAction")]
internal static class DoubleClickPatch
{
    [HarmonyPrefix]
    private static bool Prefix(GameItem __0, Vector2 __1)
    {
        if (__0 == null || __0.isDebugMenu || !GameSession.IsPart(__0) || ModEntry.ActiveSession == null) return true;
        if (__0.identifier == "deepspaceae.pipeline") ModEntry.ActiveSession.OpenCableBusAt(__0, __1);
        else ModEntry.ActiveSession.OpenPart(__0);
        return false;
    }
}

[HarmonyPatch(typeof(ItemMouseDoubleClickHandler), "ClickAction")]
internal static class SingleClickPatch
{
    [HarmonyPrefix]
    private static bool Prefix(GameItem __0)
        => __0 == null || __0.isDebugMenu || !GameSession.IsPart(__0) || ModEntry.ActiveSession == null;
}

[HarmonyPatch(typeof(GameItem), nameof(GameItem.ValidateShapeState))]
internal static class ShapeChangedPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameItem __instance) => ModEntry.ActiveSession?.Visuals.ShapeChanged(__instance);
}

// Native battery swapping can enter unchecked movement without MayRemove.
[HarmonyPatch(typeof(GraphUtils), nameof(GraphUtils.SwapItemsInPlace))]
internal static class LockedBatterySwapPatch
{
    [HarmonyPrefix]
    private static bool Prefix(GameItem source, GameItem target, ref bool __result)
    {
        bool Locked(GameItem item)
        {
            var inv = item?.parentInventory;
            var owner = inv?.GetParentItem();
            return owner != null && (owner.identifier == "deepspaceae.core" || EnergyReceiver.IsReceiver(owner) || IoBus.IsBus(owner) || owner.identifier == "deepspaceae.pipeline")
                && (inv!.IsRemoveLocked() || item!.MaxNumRemove() < item.unitCount);
        }
        bool DriveBay(GameItem item)=>item.parentInventory?.GetParentItem() is GameItem owner && DiskStorage.IsDrive(owner);
        bool PowerBay(GameItem item)=>item.parentInventory?.GetParentItem() is GameItem owner && (owner.identifier=="deepspaceae.core" || EnergyReceiver.IsReceiver(owner));
        if(PowerBay(source) && !PartStorage.IsPowerItem(target) || PowerBay(target) && !PartStorage.IsPowerItem(source))
        { __result=false; return false; }
        // Native swapping skips slot validation. Disk data is accessed by terminal
        // transfers, which re-check capacity; it must not enter unchecked swaps.
        if(source.parentInventory is DiskAreaInventory || target.parentInventory is DiskAreaInventory)
        { __result=false; return false; }
        if((DriveBay(source) && !DiskStorage.IsDisk(target)) || (DriveBay(target) && !DiskStorage.IsDisk(source)))
        { __result=false; return false; }
        if (!Locked(source) && !Locked(target)) return true;
        __result = false; return false;
    }
}

[HarmonyPatch(typeof(StoreStation), nameof(StoreStation.StartDay))]
internal static class DayStartedPatch
{
    [HarmonyPostfix]
    private static void Postfix() => ModEntry.ActiveSession?.DayStarted();
}

// The cable's native content window is a hidden save-graph host, never a user-facing UI.
[HarmonyPatch(typeof(ItemMouseDoubleClickHandler), nameof(ItemMouseDoubleClickHandler.OpenContentAction))]
internal static class CableContentWindowPatch
{
    [HarmonyPrefix]
    private static bool Prefix(GameItem __0) => __0?.identifier != "deepspaceae.pipeline" && (__0==null || (!DiskStorage.IsDisk(__0) && !DiskStorage.IsDrive(__0) && !EnergyReceiver.IsReceiver(__0)));
}
