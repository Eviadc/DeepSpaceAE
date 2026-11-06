using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

internal sealed partial class Acquisition
{
    // Steve is an internal resource key; the localized merchant is the Fluix Researcher.
    internal const string Upgrade="deepspaceae.ae_engineer",Steve="deepspaceae.steve";
    private readonly ModLog log;
    private readonly GameSession session;
    private Sprite? portrait;
    private bool delivering,registered;
    private GameItem? pendingGift;
    internal Acquisition(ModLog log,GameSession session) { this.log=log; this.session=session; }
    private static int Get(string key,int fallback=0)=>ModHelper.GetModDataInt(ModEntry.ModId,key,fallback);
    private static void Put(string key,int value)=>ModHelper.SetModDataInt(ModEntry.ModId,key,value);
    private bool EngineerGift => Get("engineer_start_v1") == 1;
    private IReadOnlyList<string> GiftItems => EngineerGift ? EngineerStart.Items(Get("engineer_skip_intro_v1") != 0) : AcquisitionCatalog.UpgradeGifts;
    internal void Enable()
    {
        EnableContact();
        ModHelper.AddStartingScenario(ModEntry.ModId, EngineerStart.Scenario,
            () => UiText.Get("start_engineer_title"), () => UiText.Get("start_engineer_description"),
            new Func<string>[] { () => UiText.Get("start_engineer_base"), () => UiText.Get("start_engineer_kit"), () => UiText.Get("start_engineer_disks"), () => UiText.Get("start_engineer_routing"), () => UiText.Get("start_engineer_power"), () => UiText.Get("start_engineer_contact") },
            BeginEngineerStart, iconKey: ModEntry.ModId + ":engineer-start", tag: () => "AE", tagTone: StartingScenario.TagTone.Positive);
        // RegisterModUpgrade accesses the lazy PlayerStore singleton. OnEnable runs
        // before NewGameData exists, so defer until a real new/loaded world is ready.
        ModHook.OnNewGameStarted+=NewGameStarted;
        ModHook.OnGameLoadedInit+=RegisterUpgrade;
        ModHook.OnGenerateCustomerVeryLate+=QueueAppointment;
        ModHook.OnGameLoadedLate+=Loaded;
        ModHook.OnWakingUpLate+=Resume;
        ModHook.OnShutterOpenedLate+=Resume;
        if(PlayerStore.HasInstance && NewGameData.Instance!=null) RegisterUpgrade();
    }
    private void RegisterUpgrade()
    {
        if(!PlayerStore.HasInstance || NewGameData.Instance==null || PlayerStore.Instance.networkUpgrade==null) return;
        // The native runtime dictionary belongs to the current world, not this mod instance.
        if(registered && ModHelper.GetNetworkUpgrade(Upgrade)!=null) return;
        ModHelper.AddNetworkUpgrade(ModEntry.ModId,Upgrade,AcquisitionCatalog.EngineerCost,"Referral",
            ()=>UiText.Get("upgrade_engineer_name"),()=>UiText.Get("upgrade_engineer_desc"),StartGift,
            iconKey: ModEntry.ModId + ":core",
            alreadyBought:()=>Get("gift_inflight")!=0?UiText.Get("upgrade_engineer_fault"):
                UiText.Get(EngineerGift ? "start_engineer_pending" : "upgrade_engineer_done",Math.Max(0,GiftItems.Count-Get("gift_cursor"))));
        registered=true;
    }
    internal void Disable()
    {
        ModHelper.RemoveStartingScenarios(ModEntry.ModId);
        ModHook.OnNewGameStarted-=NewGameStarted;
        ModHook.OnGameLoadedInit-=RegisterUpgrade;
        ModHook.OnGenerateCustomerVeryLate-=QueueAppointment; ModHook.OnGameLoadedLate-=Loaded;
        ModHook.OnWakingUpLate-=Resume; ModHook.OnShutterOpenedLate-=Resume;
        DisableContact();
        if(registered && PlayerStore.HasInstance) ModHelper.RemoveNetworkUpgrades(ModEntry.ModId);
        registered=false;
        if(PlayerStore.HasInstance) PlayerStore.Instance.storeClientManager?.clientStack?.RemoveAll(c=>c?.identifier==Steve && c.sourceModId==ModEntry.ModId);
        var sprites=SpriteDict.Instance?.spriteDictionary;
        if(sprites!=null && sprites.TryGetValue(Steve,out var current) && ReferenceEquals(current,portrait)) sprites.Remove(Steve);
        if(portrait!=null) UnityEngine.Object.Destroy(portrait); portrait=null;
    }
    private void BeginEngineerStart()
    {
        if (!PlayerStore.HasInstance || !ModHelper.IsStartingScenario(EngineerStart.Scenario)) return;
        if (EngineerGift) return;
        // No item writes here: this callback precedes OnNewGameStarted/session readiness.
        Put("engineer_start_v1", 1); Put("engineer_skip_intro_v1", PlayerStore.Instance.skipIntro ? 1 : 0);
        Put("engineer_standard_bay", 1);
        Put("gift_cursor", 0); Put("gift_inflight", 0);
        PlayerStore.Instance.playerCash -= EngineerStart.GeneralistCost;
    }
    private void RestoreEngineerUnlock()
    {
        if (!EngineerGift) return;
        var upgrade = ModHelper.GetNetworkUpgrade(Upgrade);
        // Vanilla starting professions set state=2 directly; Unlock would invoke the gift callback.
        if (upgrade != null) upgrade.state = 2;
    }
    private void NewGameStarted(string _) { pendingGift=null; RegisterUpgrade(); RestoreEngineerUnlock(); EnsurePortrait(); Resume(); }
    internal void Loaded() { pendingGift=null; RegisterUpgrade(); RestoreEngineerUnlock(); EnsurePortrait(); Resume(); }
    internal void EnsurePortrait()
    {
        var dictionary=SpriteDict.Instance?.spriteDictionary;
        if(dictionary==null || dictionary.ContainsKey(Steve)) return;
        var image=NativeWidgets.Sprite("steve-portrait");
        // Match the verified 288x211 native merchant portrait, not an arbitrary
        // first sprite's aspect/height. The generated bust uses the same canvas.
        if(image==null || !dictionary.TryGetValue(SpriteDict.GetAshishSpriteName(),out var reference) || reference==null) return;
        var pivot=new Vector2(reference.pivot.x/reference.rect.width,reference.pivot.y/reference.rect.height);
        if(portrait!=null) UnityEngine.Object.Destroy(portrait);
        portrait=Sprite.Create(image.texture,image.rect,pivot,reference.pixelsPerUnit);
        portrait.name=Steve; dictionary[Steve]=portrait;
    }
    private void StartGift()
    {
        if(!ModHelper.HasModData(ModEntry.ModId,"gift_cursor")) { Put("gift_cursor",0); Put("gift_inflight",0); }
        Resume();
    }
    internal void Deliver()
    {
        if(delivering || session.Loading || !ModHelper.IsNetworkUpgradeUnlocked(Upgrade) || GameSession.MainInventory()==null) return;
        if(!ModHelper.HasModData(ModEntry.ModId,"gift_cursor")) return;
        // Old unresolved saves remain untouched, as requested. New grants use
        // an ordinary cursor and the native placement result, without this flag.
        if(Get("gift_inflight")!=0) return;
        delivering=true;
        try
        {
            var root=GameSession.MainInventory()!;
            var items = GiftItems;
            int index=Get("gift_cursor");
            while(index<items.Count)
            {
                string id = items[index];
                var item = pendingGift ?? (id.StartsWith("deepspaceae.", StringComparison.Ordinal) ? ItemSpawner.Spawn(id) : DirectoryMaster.Item(id));
                if(item==null) throw new InvalidOperationException("AE starter factory unavailable.");
                if(pendingGift==null)
                {
                    pendingGift=item;
                    // Preserve the old pending v1 grant; new starts receive a standard storage bay.
                    if (EngineerGift && id == "storage_bay" && Get("engineer_standard_bay") == 0) ItemFeatureHelper.SetShabby(item);
                    if (EngineerGift && id == EngineerStart.Battery)
                    {
                        int maximum = item.GetTagReadonly(PowerHelper.POWER_SOURCE_ITEM_MAX_ENERGY).GetInt();
                        PowerHelper.SetPowerSourceAt(item, maximum);
                    }
                    GeneralHelper.SetItemOwned(item,true);
                }
                var slot=root.TryFindOneValidInventorySlot(item);
                if(slot==null || slot.numTransfer<=0)
                {
                    log.Log("AE starter delivery pending: make room in the main inventory."); return;
                }
                while(item.unitCount>0)
                {
                    int count=item.unitCount;
                    int accepted=slot!.TryAcceptOnce(count);
                    if(accepted<=0) { log.Warning("AE starter placement was declined; delivery paused."); return; }
                    if(accepted>=count) break;
                    slot=root.TryFindOneValidInventorySlot(item);
                    if(slot==null || slot.numTransfer<=0) { log.Warning("AE starter delivery paused with a partial native stack."); return; }
                }
                Put("gift_cursor",++index); pendingGift=null;
            }
        }
        catch(Exception e) { log.Error("AE starter delivery stopped: "+Diagnostics.ConsoleText(e.ToString())); }
        finally { delivering=false; session.Visuals.Invalidate(); }
    }
    private void Resume()
    {
        Deliver();
        if(!PlayerStore.HasInstance || session.Loading || NewGameData.Instance==null) return;
        if(ModHelper.IsNetworkUpgradeUnlocked(Upgrade)) EnsureContact();
        var manager=PlayerStore.Instance.storeClientManager;
        if(manager!=null) QueueAppointment(manager);
    }
    private void QueueAppointment(StoreClientManager manager)
    {
        int day=StoreStation.GetDayCounter(),due=Get("steve_appointment_day");
        if(due<=0 || due>day || Get("steve_served_day",-1)==day) return;
        EnsurePortrait();
        if(manager.clientStack.Any(c=>c?.identifier==Steve)
            || PlayerStore.Instance.currentClientInstance?.GetClientBlueprint()?.identifier==Steve) return;
        manager.AddNextClient(CreateSteve(day));
    }
    private StoreClient CreateSteve(int day)
    {
        var planned=ModHelper.GetModData(ModEntry.ModId,"steve_stock","").Split('\n')
            .Where(s=>s.Length>0 && s!=EnergyReceiver.Id).ToArray();
        var client=new StoreClient { identifier=Steve,displayName=UiText.Get("npc_steve_name"),spriteName=Steve,
            clientFaction="FACTION_LOWER_LEVEL",clientIntent=StoreClient.ClientIntent.SELL,
            isOfferWholesale=false,wholesaleDiscount=0,isBargainAvailable=false,nonShootable=true };
        ModHelper.SetClientSource(client,ModEntry.ModId);
        client.mainDialogue.SetText(client.displayName,UiText.Get("npc_steve_greeting")).SetEndAction(()=>{
            if(Get("steve_served_day",-1)!=day)
            {
                Put("steve_served_day",day); Put("steve_appointment_day",0); // persist before creating sale stock
                var table=EmporiumEntry.Instance.frontInvinvElement;
                foreach(var id in planned)
                {
                    if(!table.HasFreeCell()) break;
                    if(!AcquisitionCatalog.Offers.Any(o=>o.Id==id)) continue;
                    var item=ItemSpawner.Spawn(id);
                    if(item==null) { log.Error("AE trader item factory unavailable: "+id); continue; }
                    // The native supplier also skips stock that cannot fit. Its
                    // placement API owns the candidate and reports the outcome.
                    PlayerStore.Instance.TryAddDirectSellingItemToTable(item);
                }
            }
            client.OnIntroduced();
        });
        AeTraderDialogue.Configure(client); client.CompleteClientCreation(); return client;
    }
}
