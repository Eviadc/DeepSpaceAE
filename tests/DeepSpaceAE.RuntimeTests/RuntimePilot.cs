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
    private bool booted,done;
    private int step;
    private GameItem core=null!,drive=null!,receiver=null!,terminal=null!,small=null!,large=null!,overflow=null!;
    private GameGridInventory root=null!;
    private readonly List<GameItem> stored=new();
    private readonly List<GameItem> fixture=new();
    private int x,y,area;
    internal void Configure(PilotConfig settings)
    {
        config=settings; report.runId=config.runId; report.productCommit=config.productCommit;
        report.startedUtc=DateTime.UtcNow.ToString("O"); report.unityVersion=Application.unityVersion;
        deadline=Time.realtimeSinceStartup+120; Write();
    }
    private void Update()
    {
        if(config==null || done || Time.realtimeSinceStartup<next) return;
        next=Time.realtimeSinceStartup+0.25f;
        try
        {
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
                if(Adapter.Session==null || (bool)Adapter.Property(Adapter.Session,"Loading")!) return;
                root=EmporiumEntry.Instance.invElement;
                report.saveSlot=PlayerStore.Instance.saveSlotId; report.day=StoreStation.GetDayCounter();
                report.freshScenarioConfirmed=true; report.status="running"; Write();
            }
            Guard();
            var actions=new Action[]{Load,Slots,Network,Power,Capacity,Terminal,Disconnect,Save};
            if(step==actions.Length) { Finish(); return; }
            var watch=System.Diagnostics.Stopwatch.StartNew();
            string id="PILOT-"+(step+1).ToString("00");
            try { actions[step](); report.cases.Add(new CaseResult{id=id,status="PASS",detail=details,elapsedMs=watch.Elapsed.TotalMilliseconds}); }
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
        for(int dx=3;dx<=5;dx++) At(Spawn("deepspaceae.pipeline"),x+dx,y+1);
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
        At(terminal,x+6,y+4);
        Check(Adapter.DeviceStatus(Adapter.Evaluation(Adapter.Board()),Adapter.Id(terminal))!="Online","Detached terminal stayed online.");
        At(terminal,x+6,y+1);
        Check(Adapter.DeviceStatus(Adapter.Evaluation(Adapter.Board()),Adapter.Id(terminal))=="Online","Terminal did not reconnect.");
        Check(ReferenceEquals(overflow.parentInventory,Adapter.Data(large)),"Disconnect changed disk data ownership.");
        details="Real root movement caused offline/online transitions; disk contents retained.";
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
        File.WriteAllText(Path.Combine(config.outputDirectory,"result.json"),JsonUtility.ToJson(report,true));
    }
}
