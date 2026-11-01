using System;
using System.Linq;
using DeepSpaceAE.Core.Network;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

internal sealed class Acquisition
{
    internal const string Upgrade="deepspaceae.ae_engineer",Steve="deepspaceae.steve";
    private readonly ModLog log;
    private readonly GameSession session;
    private Sprite? portrait;
    private bool delivering,registered;
    internal Acquisition(ModLog log,GameSession session) { this.log=log; this.session=session; }
    private static int Get(string key,int fallback=0)=>ModHelper.GetModDataInt(ModEntry.ModId,key,fallback);
    private static void Put(string key,int value)=>ModHelper.SetModDataInt(ModEntry.ModId,key,value);
    internal void Enable()
    {
        // RegisterModUpgrade accesses the lazy PlayerStore singleton. OnEnable runs
        // before NewGameData exists, so defer until a real new/loaded world is ready.
        ModHook.OnNewGameStarted+=NewGameStarted;
        ModHook.OnGameLoadedInit+=RegisterUpgrade;
        ModHook.OnGenerateCustomerNormal+=Generate;
        ModHook.OnGameLoadedLate+=Loaded;
        ModHook.OnWakingUpLate+=Deliver;
        ModHook.OnShutterOpenedLate+=Deliver;
        if(PlayerStore.HasInstance && NewGameData.Instance!=null) RegisterUpgrade();
    }
    private void RegisterUpgrade()
    {
        if(registered || !PlayerStore.HasInstance || NewGameData.Instance==null) return;
        ModHelper.AddNetworkUpgrade(ModEntry.ModId,Upgrade,AcquisitionCatalog.EngineerCost,"Referral",
            ()=>UiText.Get("upgrade_engineer_name"),()=>UiText.Get("upgrade_engineer_desc"),StartGift,
            alreadyBought:()=>Get("gift_inflight")!=0?UiText.Get("upgrade_engineer_fault"):
                UiText.Get("upgrade_engineer_done",Math.Max(0,AcquisitionCatalog.Starter.Count-Get("gift_cursor"))));
        registered=true;
    }
    internal void Disable()
    {
        ModHook.OnNewGameStarted-=NewGameStarted;
        ModHook.OnGameLoadedInit-=RegisterUpgrade;
        ModHook.OnGenerateCustomerNormal-=Generate; ModHook.OnGameLoadedLate-=Loaded;
        ModHook.OnWakingUpLate-=Deliver; ModHook.OnShutterOpenedLate-=Deliver;
        if(registered && PlayerStore.HasInstance) ModHelper.RemoveNetworkUpgrades(ModEntry.ModId);
        registered=false;
        if(PlayerStore.HasInstance) PlayerStore.Instance.storeClientManager?.clientStack?.RemoveAll(c=>c?.identifier==Steve && c.sourceModId==ModEntry.ModId);
        var sprites=SpriteDict.Instance?.spriteDictionary;
        if(sprites!=null && sprites.TryGetValue(Steve,out var current) && ReferenceEquals(current,portrait)) sprites.Remove(Steve);
        if(portrait!=null) UnityEngine.Object.Destroy(portrait); portrait=null;
    }
    private void NewGameStarted(string _) { RegisterUpgrade(); EnsurePortrait(); Deliver(); }
    internal void Loaded() { RegisterUpgrade(); EnsurePortrait(); Deliver(); }
    internal void EnsurePortrait()
    {
        var dictionary=SpriteDict.Instance?.spriteDictionary;
        if(dictionary==null || dictionary.ContainsKey(Steve)) return;
        var image=NativeWidgets.Sprite("steve-portrait");
        var reference=dictionary.Values.FirstOrDefault(s=>s!=null && s.bounds.size.y>0);
        if(image==null || reference==null) return;
        var pivot=new Vector2(reference.pivot.x/reference.rect.width,reference.pivot.y/reference.rect.height);
        if(portrait!=null) UnityEngine.Object.Destroy(portrait);
        portrait=Sprite.Create(image.texture,image.rect,pivot,image.rect.height/reference.bounds.size.y);
        portrait.name=Steve; dictionary[Steve]=portrait;
    }
    private void StartGift()
    {
        if(!ModHelper.HasModData(ModEntry.ModId,"gift_cursor")) { Put("gift_cursor",0); Put("gift_inflight",0); }
        Deliver();
    }
    internal void Deliver()
    {
        if(delivering || session.Loading || !ModHelper.IsNetworkUpgradeUnlocked(Upgrade) || GameSession.MainInventory()==null) return;
        if(!ModHelper.HasModData(ModEntry.ModId,"gift_cursor")) return;
        if(Get("gift_inflight")!=0) { log.Error("AE starter delivery unresolved; no automatic replay."); return; }
        delivering=true;
        try
        {
            var root=GameSession.MainInventory()!;
            var progress=new DeliveryProgress(AcquisitionCatalog.Starter.Count,Get("gift_cursor"),Get("gift_inflight"));
            while(progress.Next<progress.Total)
            {
                int index=progress.Next;
                var item=ItemSpawner.Spawn(AcquisitionCatalog.Starter[index]);
                if(item==null) throw new InvalidOperationException("AE starter factory unavailable.");
                SlotMarker slot;
                try
                {
                    GeneralHelper.SetItemOwned(item,true);
                    slot=root.TryFindOneValidInventorySlot(item);
                    if(slot==null || slot.numTransfer<1 || slot.targetItem!=null || slot.itemGridShape==null || !ReferenceEquals(slot.inventory,root))
                    {
                        // This fresh, never-delivered candidate has no owner in the
                        // save graph. The entitlement remains queued; no real gift
                        // or player item is destroyed after a native write.
                        if(item.parentInventory==null) item.Destroy();
                        log.Log("AE starter delivery pending: make room in the main inventory."); return;
                    }
                }
                catch { if(item.parentInventory==null) item.Destroy(); throw; }
                if(!progress.Begin()) return;
                Put("gift_inflight",progress.InFlight);
                try
                {
                    int accepted=slot.TryAcceptOnce(1);
                    if(accepted!=1 || !ReferenceEquals(item.parentInventory,root) || !root.childItems.Contains(item))
                        throw new InvalidOperationException("AE starter native placement result unknown.");
                    progress.Confirm(); Put("gift_cursor",progress.Next); Put("gift_inflight",0);
                }
                catch { session.AcquisitionFault(item); throw; }
            }
        }
        catch(Exception e) { log.Error("AE starter delivery stopped: "+Diagnostics.ConsoleText(e.ToString())); }
        finally { delivering=false; session.Visuals.Invalidate(); }
    }
    private void Generate(StoreClientManager manager)
    {
        int day=StoreStation.GetDayCounter(); if(day<AcquisitionCatalog.FirstSteveDay || Get("steve_served_day",-1)==day) return;
        EnsurePortrait();
        if(AcquisitionCatalog.CanRoll(day,Get("steve_roll_day",-1),Get("steve_served_day",-1)))
        {
            manager.clientStack.RemoveAll(c=>c?.identifier==Steve && c.sourceModId==ModEntry.ModId);
            bool visit=RNG.Roll(AcquisitionCatalog.VisitChance);
            var stock=visit?string.Join("\n",AcquisitionCatalog.RollStock(RNG.Roll,(min,max)=>RNG.GetRandomInt(min,max))):"";
            ModHelper.SetModData(ModEntry.ModId,"steve_stock",stock);
            Put("steve_visit",visit?1:0); Put("steve_roll_day",day);
        }
        if(Get("steve_visit")==0 || manager.clientStack.Any(c=>c?.identifier==Steve)
            || PlayerStore.Instance.currentClientInstance?.GetClientBlueprint()?.identifier==Steve) return;
        manager.AddClient(CreateSteve(day));
    }
    private StoreClient CreateSteve(int day)
    {
        var planned=ModHelper.GetModData(ModEntry.ModId,"steve_stock","").Split('\n').Where(s=>s.Length>0).ToArray();
        var client=new StoreClient { identifier=Steve,displayName=UiText.Get("npc_steve_name"),spriteName=Steve,
            clientFaction="FACTION_LOWER_LEVEL",clientIntent=StoreClient.ClientIntent.SELL,
            isOfferWholesale=false,wholesaleDiscount=0,isBargainAvailable=false,nonShootable=true };
        ModHelper.SetClientSource(client,ModEntry.ModId);
        client.mainDialogue.SetText(client.displayName,UiText.Get("npc_steve_greeting")).SetEndAction(()=>{
            if(Get("steve_served_day",-1)!=day)
            {
                Put("steve_served_day",day); // persist before creating sale stock
                foreach(var id in planned)
                {
                    if(!AcquisitionCatalog.Offers.Any(o=>o.Id==id)) throw new InvalidOperationException("Unknown AE stock identifier.");
                    var item=ItemSpawner.Spawn(id);
                    if(item==null || !PlayerStore.Instance.TryAddDirectSellingItemToTable(item))
                        log.Error("Steve stock could not be placed on native sale table: "+id);
                }
            }
            client.OnIntroduced();
        });
        client.AddBasicDialog(); client.CompleteClientCreation(); return client;
    }
}
internal sealed partial class GameSession
{
    internal void AcquisitionFault(GameItem item)
    { writeFault=true; if(item.parentInventory==null) uncertainItems.Add(item); }
}
