using System;
using System.Linq;
using System.Runtime.CompilerServices;
using DeepSpaceAE.Core.Network;
using HarmonyLib;

namespace DeepSpaceAE.GameAdapter;

// Both individual deals and wholesale purchases call OnItemBought, then
// MoveDealItemToInventory after payment. Route before native auto-placement
// can merge/destroy the purchased instance; never intercept payment/ownership.
[HarmonyPatch(typeof(PlayerStore),nameof(PlayerStore.OnItemBought))]
internal static class PurchasedItemRecordedPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameItem __0)=>ModEntry.ActiveSession?.RecordPurchase(__0);
}

[HarmonyPatch(typeof(PlayerStore),nameof(PlayerStore.MoveDealItemToInventory))]
internal static class PurchasedItemRoutingPatch
{
    [HarmonyPrefix]
    private static bool Prefix(GameItem __0)=>ModEntry.ActiveSession?.ReceivePurchase(__0)!=true;
}

internal sealed partial class GameSession
{
    private sealed class PurchaseReceipt { }
    private ConditionalWeakTable<GameItem,PurchaseReceipt> purchases=new();
    internal void RecordPurchase(GameItem item)
    {
        if(!Loading && item!=null) purchases.GetValue(item,_=>new PurchaseReceipt());
    }
    // True means the entire purchase was accepted; otherwise native placement
    // continues with the remainder. A declined transfer is not queued for retry.
    internal bool ReceivePurchase(GameItem item)
    {
        if(item==null || !purchases.Remove(item) || Loading || writing || MainInventory()==null
            || MapUIManager.Instance?.IsOutside==true || !GeneralHelper.IsItemEffectivelyOwned(item)) return false;
        if(!MainInventory()!.childItems.Any(i=>InterfaceStorage.IsItem(i) && InterfaceStorage.AutoPurchase(i))) return false;
        writing=true;
        try
        {
            var board=ReadBoard(); int generation=automationGeneration;
            var tried=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach(var device in board.Layout.Parts.Where(i=>InterfaceStorage.IsItem(i) && InterfaceStorage.AutoPurchase(i)).OrderBy(i=>i.GetUniqueID()))
            {
                if(generation!=automationGeneration) break;
                if(!board.Evaluation.Devices.TryGetValue(Id(device),out var connection) || connection.Status!=DeviceStatus.Online
                    || connection.CoreId==null || !board.Powered.Contains(connection.CoreId) || !tried.Add(connection.CoreId)) continue;
                var view=ReadStorage(board,connection.CoreId,catalog:false);
                if(!PowerView(view)) continue;
                int total=0;
                for(int attempt=0;attempt<64;attempt++)
                {
                    if(generation!=automationGeneration) break;
                    SlotMarker slot;
                    try { slot=InterfacePlacement(view,item); }
                    catch(Rejected) { break; }
                    int count=item.unitCount;
                    int accepted=slot.TryAcceptOnce(count);
                    if(accepted<=0) break;
                    total+=accepted;
                    PartStorage.Number(device,"interface_moved",total);
                    PartStorage.String(device,"interface_status","");
                    if(accepted>=count) return true;
                }
            }
        }
        catch(Exception e) { log.Warning("AE purchase intake: "+Diagnostics.ConsoleText(e.ToString())); }
        finally { writing=false; Visuals.Invalidate(); NativeStorageChanged(); }
        return false;
    }
}
