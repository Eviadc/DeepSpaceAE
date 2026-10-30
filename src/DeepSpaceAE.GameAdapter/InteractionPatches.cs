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
