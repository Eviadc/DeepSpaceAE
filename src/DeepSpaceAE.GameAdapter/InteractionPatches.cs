using HarmonyLib;

namespace DeepSpaceAE.GameAdapter;

// The loader applies these typed Harmony patches. They only change this mod's own items.
[HarmonyPatch(typeof(ItemMouseDoubleClickHandler), "DoubleClickAction")]
internal static class DoubleClickPatch
{
    [HarmonyPrefix]
    private static bool Prefix(GameItem __0)
    {
        if (__0 == null || __0.isDebugMenu || !GameSession.IsPart(__0) || ModEntry.ActiveSession == null) return true;
        ModEntry.ActiveSession.OpenPart(__0);
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
            return owner != null && (owner.identifier == "deepspaceae.core" || owner.identifier == "deepspaceae.item_storage_bus" || owner.identifier == "deepspaceae.pipeline")
                && (inv!.IsRemoveLocked() || item!.MaxNumRemove() < item.unitCount);
        }
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
