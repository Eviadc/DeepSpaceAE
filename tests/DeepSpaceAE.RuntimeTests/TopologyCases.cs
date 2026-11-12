using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace DeepSpaceAE.RuntimeTests;

public sealed partial class RuntimePilot
{
    [Serializable]
    private sealed class TopologyObservation
    {
        public string device="",stage="",deviceStatus="",remoteStatus="";
        public string[] remoteRoute=Array.Empty<string>();
        public int deviceConnections;
        public bool passed;
    }
    private GameItem topologyBay=null!,topologyDevice=null!,topologyStub=null!,topologyRemote=null!;
    private int[] topologyIds=Array.Empty<int>();
    private readonly List<TopologyObservation> topologyObservations=new();
    private void TopologySetup()
    {
        StartRegistry(); StartSupplies();
        topologyBay=MainItems().First(i=>i.identifier=="storage_bay");
        foreach(var item in root.childItems.ToArray())
        {
            if(ReferenceEquals(item,topologyBay) || item.FindAllChildItems().Contains(topologyBay)) continue;
            var slot=topologyBay.TryFindOneValidInventorySlot(item);
            if(slot!=null && slot.numTransfer>0) slot.TryAcceptOnce(item.unitCount);
        }
        // Other enabled mods also grant starting items; reserve a separate native
        // container so cleanup capacity cannot depend on those unrelated gifts.
        topologyBay=Spawn("storage_bay_large"); Add(topologyBay);
        bool free=false;
        for(int gy=1;gy+6<=root.inventoryShape.height && !free;gy++)
            for(int gx=1;gx+10<=root.inventoryShape.width && !free;gx++)
                if(!root.childItems.Any(i=>i.modifiedShape.minX<gx+10 && i.modifiedShape.maxX>=gx-1
                    && i.modifiedShape.minY<gy+6 && i.modifiedShape.maxY>=gy-1))
                { x=gx; y=gy; free=true; }
        Check(free,"No isolated native rectangle for workshop topology reproduction.");
        core=Spawn(Product+".core"); At(core,x,y);
        var power=MainItems().First(i=>i.identifier=="energy_credit");
        Adapter.Move(power,(GameInventory)Adapter.Static("PartStorage","Inventory",core)!);
        details="Fresh engineer slot; original supplies stored in a real storage bay; native 3x3 controller placed in an isolated rectangle.";
    }
    private void StowTopology(GameItem item)
    {
        var slot=topologyBay.TryFindOneValidInventorySlot(item);
        Check(slot!=null && slot.numTransfer==item.unitCount,"No native bay space to stow topology fixture.");
        int count=item.unitCount;
        Check(slot!.TryAcceptOnce(count)==count && !ReferenceEquals(item.parentInventory,root),"Topology fixture did not leave the main inventory.");
    }
    private void ObserveTopology(string stage,string expectedDevice,string expectedRemote,int expectedConnections=-1)
    {
        var board=Adapter.Board(); var evaluation=Adapter.Evaluation(board);
        var connection=Adapter.Indexed(evaluation,"Devices",Adapter.Id(topologyDevice));
        var remote=Adapter.Indexed(evaluation,"Devices",Adapter.Id(topologyRemote));
        string status=Adapter.Property(connection,"Status")!.ToString()!, remoteStatus=Adapter.Property(remote,"Status")!.ToString()!;
        string? owner=(string?)Adapter.Property(connection,"CoreId"), remoteOwner=(string?)Adapter.Property(remote,"CoreId");
        var topology=Adapter.Field(board,"Topology")!;
        int connections=((ICollection)Adapter.Call(topology,"Neighbors",Adapter.Id(topologyDevice))!).Count;
        var memberships=(IDictionary)Adapter.Property(evaluation,"ComponentCore")!;
        bool passed=status==expectedDevice && remoteStatus==expectedRemote
            && (expectedConnections<0 || connections==expectedConnections)
            && (expectedDevice!="Online" || owner==Adapter.Id(core))
            && (expectedRemote!="Online" || remoteOwner==Adapter.Id(core));
        if(expectedRemote!="Online") passed &= !memberships.Contains(Adapter.Id(topologyRemote));
        if(expectedDevice=="ConflictingCores") passed &= owner==null && remoteOwner==null
            && ((IDictionary)Adapter.Property(evaluation,"NightCosts")!).Count==0;
        var observation=new TopologyObservation {device=topologyDevice.identifier,stage=stage,deviceStatus=status,remoteStatus=remoteStatus,
            deviceConnections=connections,remoteRoute=((IEnumerable)Adapter.Property(remote,"Route")!).Cast<string>().ToArray(),passed=passed};
        topologyObservations.Add(observation);
        Debug.Log("[AE-PILOT] TOPOLOGY "+JsonUtility.ToJson(observation));
        File.WriteAllText(Path.Combine(config.outputDirectory,"topology-observations.json"),"[\n"+
            string.Join(",\n",topologyObservations.Select(o=>JsonUtility.ToJson(o,true)))+"\n]\n");
    }
    private void TopologyContacts()
    {
        foreach(var id in new[]{Product+".terminal",Product+".disk_drive",Product+".item_interface",Product+".fluid_interface",Product+".energy_receiver"})
        {
            topologyDevice=Spawn(id); At(topologyDevice,x+3,y);
            topologyRemote=Spawn(Product+".terminal"); At(topologyRemote,x+6,y);
            topologyStub=Spawn(Product+".pipeline");
            // Stage the loose cable away from the network before attaching it.
            At(topologyStub,x+9,y+4);
            ObserveTopology("controller contact","Online","NoCore",1);
            At(topologyStub,x+5,y);
            ObserveTopology("relay to remote terminal","Online","Online",2);
            var bridge=new[]{Spawn(Product+".pipeline"),Spawn(Product+".pipeline"),Spawn(Product+".pipeline"),Spawn(Product+".pipeline")};
            At(bridge[0],x+5,y+1); At(bridge[1],x+5,y+2); At(bridge[2],x+4,y+2); At(bridge[3],x+3,y+2);
            ObserveTopology("cable and device loop","Online","Online");
            foreach(var wire in bridge) StowTopology(wire);
            var second=Spawn(Product+".core"); At(second,x+6,y+2);
            At(bridge[0],x+5,y+1); At(bridge[1],x+5,y+2);
            ObserveTopology("controllers joined through device","ConflictingCores","ConflictingCores");
            StowTopology(bridge[0]); StowTopology(bridge[1]); StowTopology(second);
            ObserveTopology("remove second controller","Online","Online",2);
            foreach(var item in new[]{topologyDevice,topologyRemote,topologyStub}) StowTopology(item);
        }
        Check(topologyObservations.All(o=>o.passed),"Workshop topology regression: "+string.Join("; ",
            topologyObservations.Where(o=>!o.passed).Select(o=>o.device+" / "+o.stage+" = "+o.deviceStatus+", remote="+o.remoteStatus)));
        details="25 real placement observations: terminal, drive, item/fluid interfaces and receiver relay to a remote terminal; loops remain online; joined controllers conflict across the whole network; removal recovers.";
    }
    private void TopologySave()
    {
        topologyIds=new[]{core,drive,input,fluid,receiver,terminal,small,relayCargo,relayDeposit}.Select(i=>i.GetUniqueID()).ToArray();
        Save(); interfaceReloadWaiting=true; interfaceReloadReady=false; deadline=Time.realtimeSinceStartup+90;
        report.status="reloading_same_fresh_slot";
        NewGameData.Instance.saveSlot=report.saveSlot; NewGameData.Instance.saveFile=null!; NewGameData.Instance.isLoadFromMainMenu=true;
        SceneManager.LoadScene("InventoryScene",LoadSceneMode.Single);
        details="Saved the cable-free device loop and its two real stored items; reloaded only the same fresh test slot.";
    }
    private void TopologyReload()
    {
        var all=MainItems();
        foreach(int id in topologyIds) Check(all.Count(i=>i.GetUniqueID()==id)==1,"Saved topology instance missing or duplicated: "+id);
        core=all.Single(i=>i.GetUniqueID()==topologyIds[0]); drive=all.Single(i=>i.GetUniqueID()==topologyIds[1]);
        input=all.Single(i=>i.GetUniqueID()==topologyIds[2]); fluid=all.Single(i=>i.GetUniqueID()==topologyIds[3]);
        receiver=all.Single(i=>i.GetUniqueID()==topologyIds[4]); terminal=all.Single(i=>i.GetUniqueID()==topologyIds[5]);
        small=all.Single(i=>i.GetUniqueID()==topologyIds[6]); relayCargo=all.Single(i=>i.GetUniqueID()==topologyIds[7]);
        relayDeposit=all.Single(i=>i.GetUniqueID()==topologyIds[8]); topologyDevice=drive; topologyRemote=terminal;
        ObserveTopology("save reload device loop","Online","Online"); CheckRelayNetwork(5,6); CheckRelayStorage();
        Check(topologyObservations.All(o=>o.passed),"A native device-relay observation failed.");
        report.saveReloadTested=true;
        details="Native save/reload preserves nine real item IDs, two disk items, all five relaying devices, one count per device and unchanged 6 kJ nominal cost.";
    }
}
