using System;
using System.Collections;
using System.Linq;

namespace DeepSpaceAE.RuntimeTests;

public sealed partial class RuntimePilot
{
    private GameItem relayCargo=null!,relayDeposit=null!;

    private void TopologyMountedBuses()
    {
        var wrench=Spawn(Product+".wrench"); Add(wrench);
        topologyStub=Spawn(Product+".pipeline"); At(topologyStub,x+3,y+1);
        topologyRemote=Spawn(Product+".terminal"); At(topologyRemote,x+4,y+1);
        foreach(var id in new[]{Product+".item_storage_bus",Product+".import_bus",Product+".export_bus"})
        {
            topologyDevice=Spawn(id); Add(topologyDevice);
            Adapter.Move(topologyDevice,(GameInventory)Adapter.Static("PartStorage","Inventory",topologyStub)!);
            Adapter.Static("PartStorage","Number",topologyDevice,"cable_face",1); // East: toward the terminal.
            ObserveTopology("mounted bus blocks its facing connection","Online","NoCore",1);
            Adapter.Static("PartStorage","Number",topologyDevice,"cable_face",0); // North: empty face.
            ObserveTopology("turn mounted bus away","Online","Online",1);
            string kind=topologyDevice.identifier;
            // Installed buses are intentionally locked against ordinary inventory
            // removal. Exercise the production wrench callback before stowing.
            Adapter.Call(Adapter.Session,"UseWrench",wrench,topologyDevice);
            Check(ReferenceEquals(topologyDevice.parentInventory,root),"Wrench callback did not detach the mounted bus.");
            Adapter.Call(Adapter.Session,"ClosePart");
            StowTopology(topologyDevice); topologyDevice=topologyRemote;
            ObserveTopology("remove "+kind,"Online","Online",1);
        }
        StowTopology(topologyStub); StowTopology(topologyRemote); StowTopology(wrench);
        Check(topologyObservations.All(o=>o.passed),"Mounted buses changed their host-only or blocked-face behavior.");
        details="All three real mounted bus types keep one host link; an occupied cable face blocks transmission to the terminal; turning away or removing the bus restores it.";
    }

    private void CheckRelayNetwork(int devices,int nightCost)
    {
        var evaluation=Adapter.Evaluation(Adapter.Board());
        Check(((IDictionary)Adapter.Property(evaluation,"Devices")!).Count==devices,"Device count changed in the relay graph.");
        foreach(var item in new[]{drive,input,fluid,receiver,terminal}.Where(i=>i!=null && ReferenceEquals(i.parentInventory,root)))
            Check(Adapter.DeviceStatus(evaluation,Adapter.Id(item))=="Online","Relay fixture is offline: "+item.identifier);
        Check((int)Adapter.Indexed(evaluation,"EndpointCounts",Adapter.Id(core))==devices
            && (int)Adapter.Indexed(evaluation,"NightCosts",Adapter.Id(core))==nightCost,"Loop duplicated devices or their nominal energy cost.");
    }

    private void CheckRelayStorage()
    {
        Adapter.Call(Adapter.Session,"Open",terminal);
        var view=Adapter.Field(Adapter.Session,"displayedView")!;
        var live=(IDictionary)Adapter.Field(view,"Live")!;
        foreach(var item in new[]{relayCargo,relayDeposit}.Where(i=>i!=null))
            Check(live.Values.Cast<GameItem>().Count(i=>ReferenceEquals(i,item))==1 && ReferenceEquals(item.parentInventory,Adapter.Data(small)),
                "Relay terminal lost, duplicated or moved the real stored instance.");
    }

    private void TopologyDeviceChain()
    {
        drive=Spawn(Product+".disk_drive"); input=Spawn(Product+".item_interface");
        fluid=Spawn(Product+".fluid_interface"); terminal=Spawn(Product+".terminal");
        At(drive,x+3,y); At(input,x+5,y); At(fluid,x+5,y+2); At(terminal,x+7,y+2);
        topologyDevice=drive; topologyRemote=terminal;
        ObserveTopology("cable-free device chain","Online","Online"); CheckRelayNetwork(4,6);
        var route=((IEnumerable)Adapter.Property(Adapter.Indexed(Adapter.Evaluation(Adapter.Board()),"Devices",Adapter.Id(terminal)),"Route")!).Cast<string>();
        Check(route.SequenceEqual(new[]{core,drive,input,fluid,terminal}.Select(Adapter.Id)),"Native chain bypassed a relay device.");
        small=Spawn(Product+".disk_small"); Add(small); Adapter.Move(small,Adapter.Slots("DiskStorage","Bays",drive)[0]);
        relayCargo=Spawn("toilet_paper"); relayCargo.ModifyTag("PILOT_RELAY_ITEM",t=>t.Enable().SetInt(1)); Add(relayCargo); Adapter.Move(relayCargo,Adapter.Data(small));
        CheckRelayStorage();
        var grid=Adapter.Field(Adapter.Session,"terminalGrid")!;
        relayDeposit=Spawn("toilet_paper"); relayDeposit.ModifyTag("PILOT_RELAY_ITEM",t=>t.Enable().SetInt(2)); Add(relayDeposit);
        StowTopology(input);
        ObserveTopology("remove chain middle device","Online","NoCore");
        int accepted=(int)Adapter.Call(Adapter.Session,"DepositFromNative",grid,relayDeposit)!;
        Check(accepted==0 && ReferenceEquals(relayDeposit.parentInventory,root),"Stale terminal wrote through a disconnected relay.");
        At(input,x+5,y); ObserveTopology("restore chain middle device","Online","Online");
        Adapter.Call(Adapter.Session,"Open",terminal); grid=Adapter.Field(Adapter.Session,"terminalGrid")!;
        var handler=ItemMouseDragHandler.current;
        Adapter.SetField(handler,"currentItem",relayDeposit);
        Adapter.SetField(handler,"lastMouseClickedPosition",((GameItemElement)relayDeposit).handler.rectTransform.position);
        NativeCall(handler,"StartDrag");
        Adapter.SetField(handler,"lastInventory",grid); Adapter.SetField(handler,"lastItem",null!);
        Adapter.SetField(handler,"lastSlot",((GameInventory)grid).TryFindOneValidInventorySlot(relayDeposit));
        NativeCall(handler,"EndDrag",false);
        Check(!handler.IsDraggingItem && ReferenceEquals(relayDeposit.parentInventory,Adapter.Data(small)),"Native terminal drop failed through the device chain.");
        report.nativeDragHandlerTested=true; CheckRelayStorage();
        receiver=Spawn(Product+".energy_receiver"); At(receiver,x+3,y+2);
        ObserveTopology("close cable-free device loop","Online","Online"); CheckRelayNetwork(5,6);
        StowTopology(input);
        ObserveTopology("alternate path after removing original relay","Online","Online"); CheckRelayNetwork(4,5); CheckRelayStorage();
        At(input,x+5,y); ObserveTopology("restore device loop","Online","Online"); CheckRelayNetwork(5,6); CheckRelayStorage();
        Check(topologyObservations.All(o=>o.passed),"A native relay chain or loop observation failed.");
        StartCoroutine(Screenshot("05-device-relay-storage"));
        details="Cable-free chain and loop: real disk inventory visible once; broken middle device denies stale-window writes; native drag deposits after reconnection; receiver provides an alternate path with no duplicated cost.";
    }
}
