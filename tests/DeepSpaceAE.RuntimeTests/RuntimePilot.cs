using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeepSpaceAE.RuntimeTests;

public sealed class RuntimePilot : MonoBehaviour
{
    internal static bool FreshScenarioStarted;
    private PilotConfig config=null!;
    private readonly PilotReport report=new();
    private float next,deadline;
    private float menuReadyAt=-1;
    private float heartbeat;
    private bool priorBackground;
    private bool booted,done,hintChecked;
    private int step;
    private GameItem core=null!,drive=null!,receiver=null!,terminal=null!,small=null!,large=null!,overflow=null!;
    private GameItem drawer=null!,drawerCable=null!;
    private GameGridInventory root=null!;
    private readonly List<GameItem> stored=new();
    private readonly List<GameItem> fixture=new();
    private int x,y,area;
    internal void Configure(PilotConfig settings)
    {
        config=settings; report.runId=config.runId; report.productCommit=config.productCommit;
        priorBackground=Application.runInBackground; Application.runInBackground=true;
        report.startedUtc=DateTime.UtcNow.ToString("O"); report.unityVersion=Application.unityVersion;
        deadline=Time.realtimeSinceStartup+120; Write();
    }
    private void Update()
    {
        if(config==null || done || Time.realtimeSinceStartup<next) return;
        next=Time.realtimeSinceStartup+0.25f;
        try
        {
            report.activeScene=SceneManager.GetActiveScene().name;
            report.hasGameRoot=EmporiumEntry.Instance!=null;
            report.hasPlayerStore=PlayerStore.HasInstance;
            report.scenarioCallbackSeen=FreshScenarioStarted;
            if(PlayerStore.HasInstance) {report.nativeScenario=ModHelper.GetStartingScenarioId();report.nativeSaveSlot=PlayerStore.Instance.saveSlotId;}
            if(Time.realtimeSinceStartup>=heartbeat) {heartbeat=Time.realtimeSinceStartup+5;Write();}
            if(Time.realtimeSinceStartup>deadline) throw new TimeoutException("Pilot timed out at "+report.status);
            if(!booted)
            {
                var menu=UnityEngine.Object.FindObjectOfType<MainMenuUIController>();
                if(menu==null || NewGameData.Instance==null || !NewGameData.Instance.isInMainMenu
                    || ModLoader.Mods.All(m=>m.Manifest.ID!="deepspaceae" || !ModLoader.IsModEnabled("deepspaceae"))) return;
                if(menuReadyAt<0) {menuReadyAt=Time.realtimeSinceStartup+5;return;}
                if(Time.realtimeSinceStartup<menuReadyAt) return;
                if(!ModLoader.Mods.Any(m=>m.Manifest.ID=="deepspaceae" && m.IsEnabled))
                    throw new InvalidOperationException("Production mod failed to load; no fixture writes allowed.");
                if(EmporiumEntry.Instance!=null) throw new InvalidOperationException("Existing game scene is active; refusing to start a pilot.");
                booted=true; report.status="creating_fresh_scenario"; Write();
                var data=NewGameData.Instance; data.startType=NewGameData.StartType.MODDED; data.startScenarioId=Plugin.Scenario;
                data.saveSlot=-1; data.saveFile=null!; data.skipIntro=true; data.disableTutorialHint=true;
                data.startingPerks.Clear();
                // Same native scene entry as StartNewGameClick, without depending
                // on the menu's scaled-time fade animation (not part of this pilot).
                SceneManager.LoadScene("InventoryScene",LoadSceneMode.Single); return;
            }
            if(root==null)
            {
                if(EmporiumEntry.Instance?.invElement==null || PlayerStore.Instance==null || PlayerStore.Instance.saveSlotId<0 || !FreshScenarioStarted) return;
                if(!ModHelper.IsStartingScenario(Plugin.Scenario) || config.existingSlots.Contains(PlayerStore.Instance.saveSlotId))
                    throw new InvalidOperationException("Not a newly allocated pilot slot; no test writes permitted.");
                if(Adapter.Session==null) return;
                report.sessionLoading=(bool)Adapter.Property(Adapter.Session,"Loading")!;
                if(report.sessionLoading) return;
                root=EmporiumEntry.Instance.invElement;
                report.saveSlot=PlayerStore.Instance.saveSlotId; report.day=StoreStation.GetDayCounter();
                report.freshScenarioConfirmed=true; report.status="running"; Write();
            }
            Guard();
            if(!hintChecked)
            {
                var ui=StoreUIManager.Instance;
                if(ui==null || ui.controlHintOverlay==null) throw new InvalidOperationException("Native startup hint UI is unavailable.");
                report.startupHintInitiallyVisible=ui.controlHintOverlay.activeSelf;
                if(report.startupHintInitiallyVisible)
                {
                    ui.ToggleControlHintOverlay();
                    report.startupHintHandledBy="StoreUIManager.ToggleControlHintOverlay -> TutorialUIManager.OnHintClosed";
                }
                else report.startupHintHandledBy="already closed";
                report.startupHintClosed=!ui.controlHintOverlay.activeSelf;
                Check(report.startupHintClosed,"Native startup hint did not close.");
                hintChecked=true; Write(); return;
            }
            var actions=new Action[]{Load,Slots,Network,Power,Capacity,Terminal,Disconnect,Drawer,Filters,Save};
            if(step==actions.Length) { Finish(); return; }
            var watch=System.Diagnostics.Stopwatch.StartNew();
            string id="PILOT-"+(step+1).ToString("00");
            try { actions[step](); report.cases.Add(new CaseResult{id=id,status="PASS",detail=details,elapsedMs=watch.Elapsed.TotalMilliseconds}); Debug.Log("[AE-PILOT] "+id+" PASS: "+details); }
            catch(Exception e)
            {
                report.cases.Add(new CaseResult{id=id,status="FAIL",detail=e.ToString(),elapsedMs=watch.Elapsed.TotalMilliseconds});
                throw;
            }
            step++; Write();
        }
        catch(Exception e)
        {
            done=true; report.status="failed"; report.error=e.ToString(); report.finishedUtc=DateTime.UtcNow.ToString("O"); Write();
            Debug.LogError("[AE-PILOT] "+e);
            // Leave the isolated game open for diagnosis. No retry, rollback or cleanup writes.
        }
    }
    private string details="";
    private void Guard()
    {
        if(!FreshScenarioStarted || !ModHelper.IsStartingScenario(Plugin.Scenario) || PlayerStore.Instance.saveSlotId!=report.saveSlot
            || config.existingSlots.Contains(report.saveSlot)) throw new InvalidOperationException("Pilot save identity changed.");
    }
    private void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    private GameItem Spawn(string id)
    {
        var item=ItemSpawner.Spawn(id) ?? throw new InvalidOperationException("Factory missing: "+id);
        GeneralHelper.SetItemOwned(item,true); fixture.Add(item); return item;
    }
    private void At(GameItem item,int gx,int gy)
    {
        var shape=new GridShapeBuilder(item.modifiedShape).SetTransform(gx,gy,false,0).Build();
        var slot=root.TryInventorySlot(item,item.unitCount,Vector2.negativeInfinity,shape,item.modifiedState);
        Check(slot!=null && slot.targetItem==null && slot.numTransfer==item.unitCount,"Native root placement refused at "+gx+","+gy+" for "+item.identifier);
        int count=item.unitCount; Check(slot!.TryAcceptOnce(count)==count && ReferenceEquals(item.parentInventory,root),"Native fixture placement unknown.");
    }
    private void Add(GameItem item)
    {
        var slot=root.TryFindOneValidInventorySlot(item);
        if(slot==null || slot.targetItem!=null || slot.numTransfer<item.unitCount)
        {
            // Fixture items must retain separate identities. Find a native empty
            // position rather than overriding the real item's stacking callbacks.
            for(int gy=0;gy<root.inventoryShape.height;gy++) for(int gx=0;gx<root.inventoryShape.width;gx++)
            {
                var shape=new GridShapeBuilder(item.modifiedShape).SetTransform(gx,gy,false,0).Build();
                var candidate=root.TryInventorySlot(item,item.unitCount,Vector2.negativeInfinity,shape,item.modifiedState);
                if(candidate!=null && candidate.targetItem==null && candidate.numTransfer==item.unitCount) { slot=candidate; goto Found; }
            }
        }
        Found:
        Check(slot!=null && slot.targetItem==null && slot.numTransfer==item.unitCount,"No native fixture staging slot.");
        Check(slot!.TryAcceptOnce(item.unitCount)==item.unitCount && ReferenceEquals(item.parentInventory,root),"Fixture staging result unknown.");
    }
    private static int Cells(GameItem item)
    {
        var shape=item.modifiedShape; int count=0;
        for(int cy=0;cy<shape.height;cy++) for(int cx=0;cx<shape.width;cx++) if(shape.GetLocal(cx,cy)==1) count++;
        return count;
    }
    private void Load()
    {
        core=Spawn("deepspaceae.core"); drive=Spawn("deepspaceae.disk_drive"); receiver=Spawn("deepspaceae.energy_receiver");
        terminal=Spawn("deepspaceae.terminal"); small=Spawn("deepspaceae.disk_small"); large=Spawn("deepspaceae.disk_large");
        drawer=Spawn("deepspaceae.drawer"); Check(Cells(drawer)==4,"Drawer footprint mismatch.");
        Check(Cells(core)==9 && Cells(drive)==4 && Cells(receiver)==4 && Cells(small)==2 && Cells(large)==2,"Factory footprint mismatch.");
        details="Real registered factories; core=9, drive/receiver=4, both disks=2 occupied cells.";
    }
    private void Slots()
    {
        // Find a free native 7x6 rectangle; retain all original starting items.
        var occupied=new HashSet<(int,int)>();
        foreach(var item in root.childItems)
            for(int cy=0;cy<item.modifiedShape.height;cy++) for(int cx=0;cx<item.modifiedShape.width;cx++)
                if(item.modifiedShape.GetLocal(cx,cy)==1) occupied.Add(item.modifiedShape.ForwardTransform(cx,cy));
        bool found=false;
        for(int gy=0;gy<5 && !found;gy++) for(int gx=0;gx<18 && !found;gx++)
            if(Enumerable.Range(0,42).All(i=>!occupied.Contains((gx+i%7,gy+i/7)))) {x=gx;y=gy;found=true;}
        Check(found,"No free fixture rectangle; no starting items were removed.");
        At(core,x,y); At(drive,x+4,y+2); At(receiver,x+1,y+3); At(terminal,x+6,y+1);
        // Occupy the complete network fixture before staging ordinary items;
        // otherwise native automatic placement may use a future cable cell.
        for(int dx=3;dx<=5;dx++) At(Spawn("deepspaceae.pipeline"),x+dx,y+1);
        At(Spawn("deepspaceae.pipeline"),x+3,y+2);
        drawerCable=Spawn("deepspaceae.pipeline"); At(drawerCable,x+3,y+3); At(drawer,x+3,y+4);
        var bays=Adapter.Slots("DiskStorage","Bays",drive); var power=Adapter.Slots("EnergyReceiver","Slots",receiver);
        Check(bays.Length==6 && power.Length==3,"Actual bay count mismatch.");
        var paper=Spawn("toilet_paper"); Add(paper);
        Check(bays.All(b=>b.TryFindOneValidInventorySlot(paper)?.numTransfer is not >0),"Drive accepted ordinary paper.");
        Check(power.All(b=>b.TryFindOneValidInventorySlot(paper)?.numTransfer is not >0),"Receiver accepted ordinary paper.");
        Add(small); Adapter.Move(small,bays[0]); Add(large); Adapter.Move(large,bays[1]);
        Check(ReferenceEquals(bays[0].childItem,small) && ReferenceEquals(bays[1].childItem,large),"Disk identity changed.");
        details="Six real disk bays, three real power bays; ordinary item rejected; two real disks installed.";
    }
    private void Network()
    {
        var evaluation=Adapter.Evaluation(Adapter.Board()); string coreId=Adapter.Id(core);
        foreach(var item in new[]{drive,receiver,terminal})
            Check(Adapter.DeviceStatus(evaluation,Adapter.Id(item))=="Online" && Equals(Adapter.Property(Adapter.Indexed(evaluation,"Devices",Adapter.Id(item)),"CoreId"),coreId),"Physical leaf not connected: "+item.identifier);
        Check((int)Adapter.Indexed(evaluation,"NightCosts",coreId)==4 && (int)Adapter.Indexed(evaluation,"ChannelsUsed",coreId)==3,"Cost/channel mismatch.");
        details="Native occupied-cell topology: three leaves, 3 channels, 4 kJ cost; no fixture edges injected.";
    }
    private void Power()
    {
        var slots=Adapter.Slots("EnergyReceiver","Slots",receiver);
        var sources=new[]{PreBuiltItemHelper.EnergyCredit(3),PreBuiltItemHelper.EnergyCredit(2),PreBuiltItemHelper.EnergyCredit(1)};
        for(int i=0;i<sources.Length;i++) {GeneralHelper.SetItemOwned(sources[i],true);fixture.Add(sources[i]);Add(sources[i]);Adapter.Move(sources[i],slots[i]);}
        var board=Adapter.Board(); var powered=(HashSet<string>)Adapter.Field(board,"Powered")!;
        Check(powered.Contains(Adapter.Id(core)),"Shared source combination did not power the actual network.");
        Check(sources[0].MaxNumRemove()==0 && sources[1].MaxNumRemove()==0 && sources[2].MaxNumRemove()==1,"Selected/spare lock mismatch.");
        var lease=Adapter.Static("PartStorage","Lease",core)!;
        Check(Adapter.Property(lease,"Required") is int budget && budget==4 && Adapter.Property(lease,"Phase")!.ToString()=="Reserved","Native saved reservation mismatch.");
        Check(PowerHelper.GetAvailableEnergyFromItem(sources[0],false)==3 && PowerHelper.GetAvailableEnergyFromItem(sources[1],false)==2,"Reservation debited energy early.");
        details="Actual 3+2+1 kJ batteries: two selected, spare removable, peak=4; reservation did not debit. Night settlement not tested.";
    }
    private void Capacity()
    {
        var data=Adapter.Data(small); var first=Spawn("toilet_paper"); area=Cells(first);
        Check(area>0 && area<=64,"Unsuitable native capacity fixture.");
        int count=64/area;
        for(int i=0;i<count;i++)
        {
            var paper=i==0?first:Spawn("toilet_paper"); Add(paper); Adapter.Move(paper,data); stored.Add(paper);
        }
        overflow=Spawn("toilet_paper"); Add(overflow);
        Check(data.TryFindOneValidInventorySlot(overflow)?.numTransfer is not >0,"Small disk exceeded capacity.");
        Check(data.childItems.Count==count && data.childItems.Sum(Cells)==count*area,"Stored identity/area mismatch.");
        Check(ReferenceEquals(overflow.parentInventory,root),"Rejected item left its real source.");
        details="Native item area="+area+", stored="+count+", used="+(count*area)+"/64; overflow rejected in source.";
    }
    private void Terminal()
    {
        Adapter.Call(Adapter.Session,"Open",terminal);
        var grid=Adapter.Field(Adapter.Session,"terminalGrid"); Check(grid!=null,"Terminal did not create a real UI directory.");
        int accepted=(int)Adapter.Call(Adapter.Session,"DepositFromNative",grid,overflow)!;
        Check(accepted==1 && ReferenceEquals(overflow.parentInventory,Adapter.Data(large)),"Terminal routing did not use second disk for overflow.");
        Adapter.Move(stored[0],root); Check(ReferenceEquals(stored[0].parentInventory,root),"Actual withdrawal failed.");
        Adapter.Call(Adapter.Session,"Open",terminal);
        var view=Adapter.Field(Adapter.Session,"displayedView")!;
        var search=Adapter.Field(view,"Search")!;
        Check(Adapter.SearchCount(search,"toilet_paper")==stored.Count,"Directory did not match actual disks after deposit/withdraw.");
        Check(ReferenceEquals(overflow.parentInventory,Adapter.Data(large)),"Opening directory changed item ownership.");
        details="Real terminal built; native deposit routed overflow to large disk; same-instance withdrawal; refreshed directory count matches. No mouse input.";
    }
    private void Disconnect()
    {
        At(terminal,root.inventoryShape.width-1,root.inventoryShape.height-1);
        Check(Adapter.DeviceStatus(Adapter.Evaluation(Adapter.Board()),Adapter.Id(terminal))!="Online","Detached terminal stayed online.");
        At(terminal,x+6,y+1);
        Check(Adapter.DeviceStatus(Adapter.Evaluation(Adapter.Board()),Adapter.Id(terminal))=="Online","Terminal did not reconnect.");
        Check(ReferenceEquals(overflow.parentInventory,Adapter.Data(large)),"Disconnect changed disk data ownership.");
        details="Real root movement caused offline/online transitions; disk contents retained.";
    }
    private void Drawer()
    {
        var data=(GameInventory)Adapter.Static("DrawerStorage","Data",drawer)!;
        var cards=(GameInventory)Adapter.Static("DrawerStorage","Upgrades",drawer)!;
        GameItem Paper(int value)
        {
            var paper=Spawn("toilet_paper"); paper.ModifyTag("PILOT_DRAWER_STATE",t=>t.Enable().SetInt(value)); Add(paper); return paper;
        }
        var first=Paper(1); Adapter.Move(first,data);
        var different=Paper(2);
        Check(data.TryFindOneValidInventorySlot(different)?.numTransfer is not >0,"Drawer accepted a different tag value.");
        for(int i=1;i<16;i++) Adapter.Move(Paper(1),data);
        Check(first.MaxNumAdd()==0,"Native stacking could bypass the drawer budget.");
        var extra=Paper(1); Check(data.TryFindOneValidInventorySlot(extra)?.numTransfer is not >0,"Drawer exceeded base 16 units.");
        var card=Spawn("deepspaceae.capacity_upgrade"); Add(card); Adapter.Move(card,cards);
        Check((int)Adapter.Static("DrawerStorage","Capacity",drawer)! == 64,"Drawer upgrade was not x4.");
        Adapter.Call(Adapter.Session,"Open",terminal); var terminalWindow=Adapter.Field(Adapter.Session,"terminalWindow");
        Adapter.Call(Adapter.Session,"OpenPart",drawer);
        Check(Adapter.Field(Adapter.Session,"partWindow")!=null && ReferenceEquals(terminalWindow,Adapter.Field(Adapter.Session,"terminalWindow")),"Drawer panel closed or replaced the terminal.");
        Check(((PixelWindow)terminalWindow!).handler.gameObject.activeInHierarchy && ((PixelWindow)Adapter.Field(Adapter.Session,"partWindow")!).handler.gameObject.activeInHierarchy,"Both native windows are not visible together.");
        Adapter.Move(extra,data);
        Check(data.childItems.Count==17 && data.childItems.OfType<GameItemElement>().Count(i=>i.handler.gameObject.activeSelf)==1,"Full drawer did not render as one real pile representative.");
        Check(ReferenceEquals(extra.parentInventory,data) && card.MaxNumRemove()==0,"Over-capacity card removal was not blocked.");
        Adapter.Move(extra,root); Check(extra is GameItemElement removed && removed.handler.gameObject.activeSelf,"Withdrawn item stayed hidden in its real destination.");
        Check(card.MaxNumRemove()>0,"Card stayed locked at the lower capacity."); Adapter.Move(card,root);
        foreach(var item in data.childItems.ToArray()) Adapter.Move(item,Adapter.Data(large));
        Adapter.Move(different,data); Check(data.childItems.Count==1 && ReferenceEquals(data.childItems[0],different),"Empty drawer could not change its stored state.");
        var bus=Spawn("deepspaceae.item_storage_bus"); Add(bus);
        Adapter.Move(bus,(GameInventory)Adapter.Static("PartStorage","Inventory",drawerCable)!);
        Check((int)Adapter.Static("PartStorage","Int",bus,"cable_face",-1)! == 2,"Bus did not choose the free drawer-facing side.");
        Adapter.Call(Adapter.Session,"Open",terminal);
        var live=(System.Collections.IDictionary)Adapter.Field(Adapter.Field(Adapter.Session,"displayedView")!,"Live")!;
        Check(live.Values.Cast<GameItem>().Any(i=>ReferenceEquals(i,different)) && ReferenceEquals(different.parentInventory,data),"Storage bus did not expose the same drawer instance.");
        Check(GameSessionContentsAreDataOnly(),"Drawer upgrade inventory was exposed as stock.");
        Check(Adapter.Field(Adapter.Session,"partWindow")!=null,"Terminal refresh closed the native drawer panel.");
        Check(data.childItems.OfType<GameItemElement>().Count(i=>i.handler.gameObject.activeSelf)==1,"Native drawer pile did not expose exactly one real representative.");
        details="Real drawer: 16-unit limit, differing tag value rejected, x4 card, guarded removal, independent native panel coexists with terminal, empty/retype, physical bus and same-instance network visibility.";
        bool GameSessionContentsAreDataOnly()
        {
            var contents=(System.Collections.IList)Adapter.Static("GameSession","Contents",drawer)!;
            return contents.Count==1 && ReferenceEquals(contents[0],data);
        }
    }
    private void Filters()
    {
        // The final boolean is skipValidate, not force/immediate validation.
        overflow.ModifyTag("PILOT_FILTER_TAG",t=>t.Enable().SetInt(7));
        Adapter.Call(Adapter.Session,"Open",terminal);
        var view=Adapter.Field(Adapter.Session,"displayedView")!;
        Check(!(bool)Adapter.Property(Adapter.Session,"HasTagUpgrade")! && Adapter.SearchCount(Adapter.Field(view,"Search")!,"#PILOT_FILTER_TAG")==0,"Advanced tags available without a card.");
        var card=Spawn("deepspaceae.tag_upgrade"); Add(card); Adapter.Move(card,(GameInventory)Adapter.Static("PartStorage","Inventory",terminal)!);
        Adapter.Call(Adapter.Session,"Open",terminal);
        Check((bool)Adapter.Property(Adapter.Session,"HasTagUpgrade")!,"Terminal did not detect installed tag card.");
        Adapter.SetField(Adapter.Session,"tag","PILOT_FILTER_TAG");
        int Count()=>(int)Adapter.Property(Adapter.Call(Adapter.Session,"TerminalRows")!,"Count")!;
        int filtered=Count();
        Check(filtered==1,"Raw tag filter count="+filtered+" selected="+Adapter.Field(Adapter.Session,"tag")
            +" enabled="+overflow.modifiedState.IsTag("PILOT_FILTER_TAG")+"; expected exactly one real instance.");
        var prior=PlayerStore.Instance.currentClientInstance;
        try
        {
            var buyer=new StoreClient {identifier="deepspaceae.pilot_buyer",displayName="Pilot buyer",clientIntent=StoreClient.ClientIntent.BUY,useClientBudget=false};
            buyer.clientBuyingIdList.Add("toilet_paper"); PlayerStore.Instance.currentClientInstance=StoreClientInstance.CreateClientInstance(buyer);
            Adapter.SetField(Adapter.Session,"tag","deepspaceae.customer_demand"); Check(Count()>0,"Native customer demand was not matched.");
            buyer.clientBlackIdList.Add("toilet_paper"); Check(Count()==0,"Native customer refusal was ignored.");
            PlayerStore.Instance.currentClientInstance=null!; Check(Count()==0,"No-customer filter exposed items.");
        }
        finally { PlayerStore.Instance.currentClientInstance=prior; }
        Adapter.Move(card,root); Adapter.Call(Adapter.Session,"Open",terminal);
        var options=(string[])Adapter.Field(Adapter.Session,"displayedTags")!;
        Check(!(bool)Adapter.Property(Adapter.Session,"HasTagUpgrade")! && !options.Contains("PILOT_FILTER_TAG") && !options.Contains("deepspaceae.customer_demand"),"Advanced filters survived card removal.");
        details="Real terminal card gates raw tags/customer demand; native buyer accepts, native blacklist rejects, no customer matches none, removing card restores basic categories. No mouse input or live NPC transaction.";
    }
    private void Save()
    {
        Adapter.Call(Adapter.Session,"Close"); Guard(); PlayerStore.Instance.SaveGame();
        string file=SaveFiles.AbsolutePath(SaveFiles.Slot(report.saveSlot));
        Check(File.Exists(file) && new FileInfo(file).Length>0,"Native test-slot save missing.");
        details="PlayerStore.SaveGame completed for newly allocated slot "+report.saveSlot+"; nonempty native save exists. Reload not tested.";
    }
    private void Finish()
    {
        done=true; report.status="passed"; report.finishedUtc=DateTime.UtcNow.ToString("O"); Write();
        Debug.Log("[AE-PILOT] PASS "+report.cases.Count+" real-runtime cases; slot="+report.saveSlot);
        if(config.exitAfterRun) Application.Quit();
    }
    private void Write()
    {
        // Unity's dynamic-mod serializer omits the nested List<CaseResult> on
        // this build. Serialize each flat record explicitly, preserving real rows.
        string meta=JsonUtility.ToJson(report,true).TrimEnd();
        string rows=string.Join(",\n",report.cases.Select(c=>JsonUtility.ToJson(c,true)));
        File.WriteAllText(Path.Combine(config.outputDirectory,"result.json"),meta.Substring(0,meta.LastIndexOf('}'))+",\n\"cases\": [\n"+rows+"\n]}\n");
    }
    private void OnDestroy() { if(config!=null) Application.runInBackground=priorBackground; }
}
