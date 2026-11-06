using HarmonyLib;

namespace DeepSpaceAE.GameAdapter;

internal static class AeTraderDialogue
{
    internal static bool IsResearcher(StoreClient? client)
        =>client?.identifier==Acquisition.Steve && client.sourceModId==ModEntry.ModId;

    internal static string Line(string kind)=>UiText.Get("npc_steve_"+kind+"_"+RNG.GetRandomInt(1,3));

    internal static void Configure(StoreClient client)
    {
        client.acceptDealDialogue=new Dialogue().SetText(client.displayName,Line("accepted"));
        client.acceptDealDialogue.priority=Dialogue.REACTION_PRIORITY;
        client.allDoneDialogue=new Dialogue().SetText(client.displayName,Line("done"))
            .SetEndAction(()=>PlayerStore.Instance.DismissCurrentClient(callNextWhenPossible:false));
        client.allDoneDialogue.priority=Dialogue.REACTION_PRIORITY;
        client.repeatDialogue=new Dialogue().SetText(client.displayName,Line("more"));
        // CompleteClientCreation still attaches the original OnDealAccepted flow.
    }
}

[HarmonyPatch(typeof(StoreClientDialogList),nameof(StoreClientDialogList.CreateAfterBuyingFromClientDialog))]
internal static class ResearcherAfterPurchaseDialoguePatch
{
    [HarmonyPostfix]
    private static void Postfix(Dialogue __result)
    {
        var client=PlayerStore.HasInstance?PlayerStore.Instance.currentClientInstance?.GetClientBlueprint():null;
        if(!AeTraderDialogue.IsResearcher(client)) return;
        // Keep the native dialogue priority, actions and remaining-stock decision.
        __result.SetText(client!.displayName,AeTraderDialogue.Line("more"));
    }
}
