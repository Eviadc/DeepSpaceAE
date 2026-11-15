using System.Collections.Generic;
using HarmonyLib;

namespace DeepSpaceAE.GameAdapter;

// Automatic pinning is a session overlay. The native, saved manual pin remains
// independent, so going offline never erases a player's own pin choice.
internal static class AutomaticPin
{
    private static readonly HashSet<GameItem> active=new();
    internal static bool Contains(GameItem item)=>item!=null && active.Contains(item);
    internal static void Set(GameItem item,bool enabled)
    {
        bool changed=enabled?active.Add(item):active.Remove(item);
        if(changed && item is GameItemElement element && !element.IsDestroyed()) item.Validate();
    }
    internal static void Clear()
    {
        var previous=new List<GameItem>(active); active.Clear();
        foreach(var item in previous) if(item is GameItemElement element && !element.IsDestroyed()) item.Validate();
    }
    internal static void Retain(IEnumerable<GameItem> current)
    {
        var present=new HashSet<GameItem>(current);
        foreach(var item in new List<GameItem>(active)) if(!present.Contains(item)) Set(item,false);
    }
}

[HarmonyPatch(typeof(ItemPinHelper),nameof(ItemPinHelper.IsPinned))]
internal static class AutomaticPinPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameItem item,ref bool __result)=>__result|=AutomaticPin.Contains(item);
}
