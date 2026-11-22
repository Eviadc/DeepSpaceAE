using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace DeepSpaceAE.RuntimeTests;

public sealed partial class RuntimePilot
{
    private GameItem wirelessA=null!,wirelessB=null!,remoteBay=null!,remoteTerminal=null!,planeCable=null!,formation=null!,annihilation=null!;
    private GameItem fluidStorage=null!,fluidImport=null!,fluidExport=null!,fluidA=null!,fluidB=null!,fluidC=null!;
    private GameGridInventory remoteGrid=null!;
    private GameItem unownedWire=null!;
    private int[] expansionIds=Array.Empty<int>();
    private void GridAt(GameGridInventory inventory,GameItem item,int gx,int gy)
    {
        var shape=new GridShapeBuilder(item.modifiedShape).SetTransform(gx,gy,false,0).Build();
        var slot=inventory.TryInventorySlot(item,item.unitCount,shape);
        Check(slot!=null && slot.targetItem==null && slot.numTransfer==item.unitCount,
            "Native grid placement declined: "+item.identifier+" at "+gx+","+gy+" in "+inventory.inventoryShape.width+"x"+inventory.inventoryShape.height);
        int count=item.unitCount; Check(slot!.TryAcceptOnce(count)==count && ReferenceEquals(item.parentInventory,inventory),"Grid move did not complete.");
    }
    private GameItem Mount(GameItem cable,string kind,int face)
    {
        var bus=Spawn(Product+"."+kind); Add(bus);
        Adapter.Move(bus,(GameInventory)Adapter.Static("PartStorage","Inventory",cable)!);
        Adapter.Static("PartStorage","Number",bus,"cable_face",face); return bus;
    }
    private void Unmount(GameItem bus)
    {
        var wrench=MainItems().First(i=>i.identifier==Product+".wrench");
        Check(wrench.MayTarget(bus) && wrench.CanTarget(bus),"Native wrench target declined.");
        wrench.Target(bus); Check(ReferenceEquals(bus.parentInventory,root),"Wrench did not remove installed bus.");
        Check(!ReferenceEquals(bus,Adapter.Field(Adapter.Session,"displayedPart")),"Detached bus configuration is still open.");
        Adapter.Call(Adapter.Session,"Close"); StowTopology(bus);
    }
    private void RunBus(GameItem bus)=>Adapter.Call(Adapter.Session,"RunAutomation",0,bus);
    private string Status(GameItem item)
    {
        var evaluation=Adapter.Evaluation(Adapter.Board());
        var devices=(IDictionary)Adapter.Property(evaluation,"Devices")!;
        return devices.Contains(Adapter.Id(item))?Adapter.DeviceStatus(evaluation,Adapter.Id(item)):"Absent";
    }
    private void Pair(GameItem a,GameItem b)
    {
        Check(a.MayTarget(b) && a.CanTarget(b),"Native wireless target declined."); a.Target(b);
        Check(ReferenceEquals(Adapter.Static("WirelessConnector","Peer",a),b),"Wireless pair not reciprocal.");
    }
    private void ExpansionSetup()
    {
        TopologySetup();
        wirelessA=Spawn(Product+".wireless_connector"); At(wirelessA,x+3,y);
        drive=Spawn(Product+".disk_drive"); At(drive,x,y+3);
        terminal=Spawn(Product+".terminal"); At(terminal,x+2,y+3);
        input=Spawn(Product+".item_interface"); At(input,x+5,y);
        small=Spawn(Product+".disk_small"); Add(small); Adapter.Move(small,Adapter.Slots("DiskStorage","Bays",drive)[0]);
        remoteBay=Spawn("storage_bay_large"); Add(remoteBay);
        remoteGrid=((List<GameInventory>)Adapter.Static("GameSession","Contents",remoteBay)!).OfType<GameGridInventory>().First();
        wirelessB=Spawn(Product+".wireless_connector"); Add(wirelessB); GridAt(remoteGrid,wirelessB,0,0);
        remoteTerminal=Spawn(Product+".terminal"); Add(remoteTerminal); GridAt(remoteGrid,remoteTerminal,2,0);
        planeCable=Spawn(Product+".pipeline"); Add(planeCable); GridAt(remoteGrid,planeCable,0,2);
        Check(wirelessA.shape.width==2 && wirelessA.shape.height==2,"Wireless connector is not 2x2.");
        Check(Status(remoteTerminal)=="NoCore","Unpaired remote network unexpectedly powered.");
        NativeCall(ItemMouseDoubleClickHandler.current,"ClickAction",wirelessA,Vector2.zero,true,false);
        Check(ReferenceEquals(typeof(ItemSelectHandler).GetField("currentItem",NativeFlags)!.GetValue(ItemSelectHandler.current),wirelessA),"Native single-click cannot select wireless connector.");
        Pair(wirelessA,wirelessB); ItemSelectHandler.current.EndSelect();
        Check(Status(remoteTerminal)=="Online","Closed remote inventory did not join the network.");
        Adapter.Call(Adapter.Session,"Open",remoteTerminal);
        Check(ReferenceEquals(Adapter.Field(Adapter.Session,"openTerminal"),remoteTerminal),"Remote terminal failed to open.");
        Adapter.Call(Adapter.Session,"Close");
        details="Fresh native slot; 2x2 wireless selection and native targeting pair main inventory to closed "+remoteGrid.inventoryShape.width+"x"+remoteGrid.inventoryShape.height+" storage bay; remote terminal opens on the same real network.";
    }
    private void ExpansionModes()
    {
        int id=input.GetUniqueID(); var inventory=Adapter.Field(Adapter.Static("InterfaceStorage","Get",input)!,"Inventory");
        Check(!(bool)Adapter.Static("InterfaceStorage","IsFluid",input)! && !input.IsTag(WaterHelper.LIQUID_CONTAINER_TAG),"Fresh interface is not item form.");
        Check(input.MayToggleSlotItem() && input.CanToggleSlotItem(),"Native right-click Toggle unavailable."); input.ToggleSlotItem();
        Check((bool)Adapter.Static("InterfaceStorage","IsFluid",input)! && input.IsTag(WaterHelper.LIQUID_CONTAINER_TAG),"Toggle did not enable native liquid form.");
        Adapter.Static("PartStorage","Number",input,"interface_mode",1); NativeWater(input,200);
        ItemSelectHandler.current.EndSelect();
        NativeCall(ItemMouseDoubleClickHandler.current,"ClickAction",input,Vector2.zero,true,false);
        Check(ReferenceEquals(typeof(ItemSelectHandler).GetField("currentItem",NativeFlags)!.GetValue(ItemSelectHandler.current),input),"Toggled fluid interface cannot use native single-click selection.");
        Check(!input.contentWindow.IsVisible(),"Fluid selection opened the item deposit proxy.");
        ItemSelectHandler.current.EndSelect();
        Check(!input.CanToggleSlotItem(),"Nonempty buffer allows item form."); input.ToggleSlotItem();
        Check(WaterHelper.GetTotalVolume(input)==200000 && (bool)Adapter.Static("InterfaceStorage","IsFluid",input)!,"Blocked toggle lost fluid.");
        WaterHelper.EmptyContainer(input); input.ToggleSlotItem();
        Check(input.GetUniqueID()==id && ReferenceEquals(Adapter.Field(Adapter.Static("InterfaceStorage","Get",input)!,"Inventory"),inventory)
            && !input.IsTag(WaterHelper.LIQUID_CONTAINER_TAG),"Interface toggle replaced identity/graph or retained fluid actions.");
        var cargo=Spawn("toilet_paper"); Add(cargo);
        Check((int)Adapter.Call(Adapter.Session,"DepositInterface",input,cargo,cargo.unitCount)!>0 && ReferenceEquals(cargo.parentInventory,Adapter.Data(small)),"Item form no longer accepts real items.");
        input.ToggleSlotItem();
        var rejected=Spawn("toothpaste"); Add(rejected);
        Check((int)Adapter.Call(Adapter.Session,"PreviewInterface",input,rejected)! ==0 && (int)Adapter.Call(Adapter.Session,"DepositInterface",input,rejected,rejected.unitCount)! ==0
            && ReferenceEquals(rejected.parentInventory,root),"Fluid form still accepts item writes.");
        input.ToggleSlotItem(); StowTopology(rejected);
        foreach(string kind in new[]{"item_storage_bus","import_bus","export_bus"})
        {
            var bus=Mount(planeCable,kind,3); var graph=(GameInventory)Adapter.Static("PartStorage","Inventory",bus)!;
            var card=Spawn(Product+".capacity_upgrade"); Add(card); Adapter.Move(card,graph);
            Adapter.Call(Adapter.Session,"CaptureGhost",bus,0,cargo); int busId=bus.GetUniqueID();
            bus.ToggleSlotItem();
            Check((bool)Adapter.Static("PartModes","IsFluid",bus)! && graph.childItems.Contains(card),"Bus form lost real upgrade.");
            bus.ToggleSlotItem();
            Check(!(bool)Adapter.Static("PartModes","IsFluid",bus)! && bus.GetUniqueID()==busId
                && ((string[])Adapter.Static("PartStorage","Filters",bus)!)[0]==cargo.identifier,"Bus form lost identity/filter.");
            Unmount(bus);
        }
        details="Native right-click Toggle switches one interface/three bus identities; real upgrades and filters survive; nonempty liquid buffers block switching; fluid form refuses item deposits and item form retains native routing.";
    }
    private void ExpansionWireless()
    {
        var spareBay=Spawn("storage_bay_large"); Add(spareBay);
        var grid=((List<GameInventory>)Adapter.Static("GameSession","Contents",spareBay)!).OfType<GameGridInventory>().First();
        var spare=Spawn(Product+".wireless_connector"); Add(spare); GridAt(grid,spare,0,0);
        Pair(wirelessA,spare);
        Check(Status(remoteTerminal)=="NoCore" && (int)Adapter.Static("WirelessConnector","Link",wirelessB)! ==0,"Re-pair retained old route.");
        Pair(wirelessA,wirelessB);
        Check((int)Adapter.Static("WirelessConnector","Link",spare)! ==0 && Status(remoteTerminal)=="Online","Pair did not recover remote network.");
        GridAt(grid,wirelessB,2,0);
        Check(Status(remoteTerminal)=="Absent","Moving connector did not deactivate its previous inventory.");
        GridAt(remoteGrid,wirelessB,0,0);
        Check(Status(remoteTerminal)=="Online","Moving connector back did not recover pairing.");
        Adapter.Move(wirelessB,Adapter.Data(small));
        Check(Status(wirelessB)=="Online" && Status(remoteTerminal)=="Absent","Connector failed in a real non-grid disk inventory or retained old contacts.");
        GridAt(remoteGrid,wirelessB,0,0);
        GeneralHelper.SetItemOwned(wirelessB,false);
        Check(Status(remoteTerminal)!="Online","Unowned peer still supplies remote network.");
        GeneralHelper.SetItemOwned(wirelessB,true);
        Check(Status(remoteTerminal)=="Online","Ownership recovery broke persistent pair.");
        var second=Spawn(Product+".core"); Add(second); GridAt(remoteGrid,second,4,0);
        Check(Status(remoteTerminal)=="ConflictingCores" && Status(terminal)=="ConflictingCores","Wireless bridge bypasses two-core conflict.");
        StowTopology(second);
        Check(Status(remoteTerminal)=="Online","Removing second core did not recover.");
        Adapter.Call(Adapter.Session,"OpenPart",wirelessA); StartCoroutine(Screenshot("wireless-settings"));
        details="Reciprocal native pairing clears both old links; peer works in a real non-grid disk inventory; moving, ownership loss/recovery and two controllers update remote routes without coordinate collisions or stale powered routes.";
    }
    private void ExpansionPlanes()
    {
        Adapter.Call(Adapter.Session,"Close");
        formation=Mount(planeCable,"formation_bus",2);
        Check(!formation.MayToggleSlotItem() && !(bool)Adapter.Static("IoBus","WholeSpace",formation)!,"Plane defaults are incorrect.");
        Check((bool)Adapter.Property(Adapter.Static("PartStorage","Policy",formation)!,"Exclude")!,"New formation filter is not a blacklist.");
        Adapter.Call(Adapter.Session,"ConfigureNumber",formation,"exclude",0);
        var paper=Adapter.Data(small).childItems.First(i=>i.identifier=="toilet_paper");
        RunBus(formation); Check(ReferenceEquals(paper.parentInventory,Adapter.Data(small)),"Unconfigured formation bus placed arbitrary stock.");
        Adapter.Call(Adapter.Session,"CaptureGhost",formation,0,paper); RunBus(formation);
        Check(ReferenceEquals(paper.parentInventory,remoteGrid) && paper.modifiedShape.minX==0 && paper.modifiedShape.minY==3,"Formation missed front cell.");
        var next=Spawn("toilet_paper"); Add(next); Adapter.Move(next,Adapter.Data(small));
        int count=paper.unitCount; RunBus(formation);
        Check(paper.unitCount==count && ReferenceEquals(next.parentInventory,Adapter.Data(small)),"Occupied front cell stacked or placed again.");
        Adapter.Call(Adapter.Session,"OpenPart",formation);
        var controls=LiveDropdowns(); Check(controls.Count==3,"Plane matching/precision/scope dropdowns unavailable."); SetDropdown(controls[2],1);
        Check((bool)Adapter.Static("IoBus","WholeSpace",formation)!,"Native scope dropdown did not save.");
        RunBus(formation); Check(!ReferenceEquals(next.parentInventory,Adapter.Data(small)),"Whole-space formation ignored free slots.");
        Unmount(formation);
        annihilation=Mount(planeCable,"annihilation_bus",2);
        var unrelated=Spawn("toothpaste"); Add(unrelated); GridAt(remoteGrid,unrelated,2,3);
        RunBus(annihilation);
        Check(!remoteGrid.childItems.Any(i=>i.modifiedShape.Get(0,3)==1) && ReferenceEquals(unrelated.parentInventory,remoteGrid),"Front absorption touched unrelated inventory cells.");
        var componentIds=new[]{"capacity_upgrade","tag_upgrade","disk_small","wrench","drawer","item_storage_bus"};
        var protectedItems=new List<GameItem>();
        foreach(var kind in componentIds)
        {
            var item=Spawn(Product+"."+kind); Add(item); Adapter.Move(item,remoteGrid); protectedItems.Add(item);
        }
        Adapter.Call(Adapter.Session,"OpenPart",annihilation); SetDropdown(LiveDropdowns()[2],1);
        Adapter.Call(Adapter.Session,"CaptureGhost",annihilation,0,unrelated);
        Adapter.Call(Adapter.Session,"ConfigureNumber",annihilation,"exclude",1);
        RunBus(annihilation); Check(ReferenceEquals(unrelated.parentInventory,remoteGrid),"Blacklist ignored.");
        Adapter.Call(Adapter.Session,"ConfigureNumber",annihilation,"exclude",0);
        Adapter.Data(small).LockInv(); RunBus(annihilation);
        Check(ReferenceEquals(unrelated.parentInventory,remoteGrid),"Absorbed item despite locked network destination.");
        Adapter.Data(small).UnlockInv(); RunBus(annihilation);
        Check(!ReferenceEquals(unrelated.parentInventory,remoteGrid),"Whitelist/whole-space absorption failed.");
        Adapter.Call(Adapter.Session,"SetFilter",annihilation,0,"");
        Adapter.Call(Adapter.Session,"ConfigureNumber",annihilation,"exclude",1); RunBus(annihilation);
        Check(protectedItems.All(i=>ReferenceEquals(i.parentInventory,remoteGrid)) && Status(remoteTerminal)=="Online","Whole-space absorption consumed an AE component/network.");
        foreach(var item in protectedItems) StowTopology(item);
        Adapter.Call(Adapter.Session,"OpenPart",annihilation); StartCoroutine(Screenshot("absorption-scope"));
        details="Both planes default to one front cell; occupied front never merges; native scope dropdown enables full inventory; whitelist/blacklist and locked destination enforced; six AE accessory types plus active network devices survive full-space absorption.";
    }
    private void ExpansionFluidBuses()
    {
        Adapter.Call(Adapter.Session,"Close");
        fluidA=Spawn("small_bottled_water"); fluidB=Spawn("small_bottled_water"); fluidC=Spawn("small_bottled_water");
        int stride=Math.Max(2,Math.Max(fluidA.modifiedShape.globalHeight,fluidB.modifiedShape.globalHeight));
        var cables=new List<GameItem>();
        for(int gy=0;gy<=stride*2;gy++)
        { var cable=Spawn(Product+".pipeline"); Add(cable); GridAt(remoteGrid,cable,4,gy); cables.Add(cable); }
        foreach(var pair in new[]{(fluidA,0),(fluidB,stride),(fluidC,stride*2)})
        { Add(pair.Item1); GridAt(remoteGrid,pair.Item1,5,pair.Item2); WaterHelper.EmptyContainer(pair.Item1); }
        fluidStorage=Mount(cables[0],"item_storage_bus",1);
        fluidImport=Mount(cables[stride],"import_bus",1); fluidExport=Mount(cables[stride*2],"export_bus",1);
        foreach(var bus in new[]{fluidStorage,fluidImport,fluidExport}) bus.ToggleSlotItem();
        NativeWater(fluidB,100); RunBus(fluidImport);
        Check(WaterHelper.GetTotalVolume(fluidA)==100000 && WaterHelper.GetTotalVolume(fluidB)==0,"Fluid import failed to store native composition.");
        RunBus(fluidExport);
        Check(WaterHelper.GetTotalVolume(fluidA)==0 && WaterHelper.GetTotalVolume(fluidC)==100000,"Fluid export failed to fill target without moving bottle.");
        Adapter.Call(Adapter.Session,"CaptureGhost",fluidExport,0,fluidC);
        Adapter.Call(Adapter.Session,"ConfigureNumber",fluidExport,"exclude",0);
        Adapter.Call(Adapter.Session,"ConfigureNumber",fluidExport,"io_exact",1);
        NativeWater(fluidA,30); RunBus(fluidExport);
        Check(WaterHelper.GetTotalVolume(fluidA)==0 && WaterHelper.GetTotalVolume(fluidC)==130000,"Exact fluid matching incorrectly compares volume.");
        Adapter.Call(Adapter.Session,"SetFilter",fluidExport,0,"");
        Adapter.Call(Adapter.Session,"ConfigureNumber",fluidExport,"exclude",1);
        NativeWater(fluidA,80,true); RunBus(fluidExport);
        Check(WaterHelper.GetTotalVolume(fluidA)==80000 && WaterHelper.GetTotalVolume(fluidC)==130000,"Strict fluid bus mixed compositions.");
        Adapter.Call(Adapter.Session,"ConfigureNumber",fluidExport,"fluid_strict",0);
        Adapter.Call(Adapter.Session,"CaptureGhost",fluidExport,0,fluidC);
        Adapter.Call(Adapter.Session,"ConfigureNumber",fluidExport,"exclude",0);
        Adapter.Call(Adapter.Session,"ConfigureNumber",fluidExport,"io_exact",1); RunBus(fluidExport);
        Check(WaterHelper.GetTotalVolume(fluidA)==80000,"Exact fluid filter ignored source composition.");
        Adapter.Call(Adapter.Session,"SetFilter",fluidExport,0,"");
        Adapter.Call(Adapter.Session,"ConfigureNumber",fluidExport,"exclude",1); RunBus(fluidExport);
        Check(WaterHelper.GetTotalVolume(fluidA)==0 && WaterHelper.GetTotalVolume(fluidC)==210000,"Optional native mixing failed.");
        fluidStorage.ToggleSlotItem(); NativeWater(fluidB,50); RunBus(fluidImport);
        Check(WaterHelper.GetTotalVolume(fluidB)==50000 && WaterHelper.GetTotalVolume(fluidA)==0,"Item storage bus still exposes liquid routes.");
        fluidStorage.ToggleSlotItem();
        Check(new[]{fluidA,fluidB,fluidC}.All(i=>ReferenceEquals(i.parentInventory,remoteGrid)),"Fluid automation moved actual containers.");
        details="Fluid storage/import/export toggle existing buses; native liquids transfer without moving bottles; strict composition, exact fluid filters and optional mixing work; item-form storage bus no longer exposes fluid routes.";
    }
    private void ExpansionSave()
    {
        input.ToggleSlotItem(); Adapter.Static("PartStorage","Number",input,"interface_mode",1); NativeWater(input,120);
        expansionIds=new[]{core,drive,terminal,input,small,wirelessA,wirelessB,remoteBay,remoteTerminal,planeCable,annihilation,
            fluidStorage,fluidImport,fluidExport,fluidA,fluidB,fluidC}.Select(i=>i.GetUniqueID()).ToArray();
        Save(); interfaceReloadWaiting=true; interfaceReloadReady=false; deadline=Time.realtimeSinceStartup+90;
        report.status="reloading_same_fresh_slot";
        NewGameData.Instance.saveSlot=report.saveSlot; NewGameData.Instance.saveFile=null!; NewGameData.Instance.isLoadFromMainMenu=true;
        SceneManager.LoadScene("InventoryScene",LoadSceneMode.Single);
        details="Saved 17 actual persistent item identities, reciprocal wireless pair, fluid modes/buffer and plane scope; reloads only this newly allocated test slot.";
    }
    private void ExpansionReload()
    {
        var all=MainItems(); foreach(int id in expansionIds) Check(all.Count(i=>i.GetUniqueID()==id)==1,"Saved identity missing/duplicated: "+id);
        input=all.Single(i=>i.GetUniqueID()==expansionIds[3]); wirelessA=all.Single(i=>i.GetUniqueID()==expansionIds[5]);
        wirelessB=all.Single(i=>i.GetUniqueID()==expansionIds[6]); remoteTerminal=all.Single(i=>i.GetUniqueID()==expansionIds[8]);
        annihilation=all.Single(i=>i.GetUniqueID()==expansionIds[10]); fluidStorage=all.Single(i=>i.GetUniqueID()==expansionIds[11]);
        Check(ReferenceEquals(Adapter.Static("WirelessConnector","Peer",wirelessA),wirelessB) && Status(remoteTerminal)=="Online","Wireless route lost across save/load.");
        Check((bool)Adapter.Static("InterfaceStorage","IsFluid",input)! && input.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)
            && WaterHelper.GetTotalVolume(input)==120000,"Fluid interface form/composition lost on load.");
        Check((bool)Adapter.Static("PartModes","IsFluid",fluidStorage)! && (bool)Adapter.Static("IoBus","WholeSpace",annihilation)!,"Bus form or plane scope lost on load.");
        Check(WaterHelper.GetTotalVolume(all.Single(i=>i.GetUniqueID()==expansionIds[16]))==210000,"Native fluid composition volume changed across load.");
        report.saveReloadTested=true; details="All 17 persistent identities survive; closed-inventory wireless route, mode state, native fluid volumes and whole-space plane setting survive native reload.";
    }
    private void ExpansionInvalidate()
    {
        unownedWire=Spawn(Product+".pipeline"); Add(unownedWire); GeneralHelper.SetItemOwned(unownedWire,false);
        Adapter.Call(Adapter.Session,"Open",remoteTerminal);
        Check(ReferenceEquals(Adapter.Field(Adapter.Session,"openTerminal"),remoteTerminal),"Remote terminal did not open before ownership test.");
        GeneralHelper.SetItemOwned(remoteBay,false);
        details="Native ownership change on the remote parent container; awaiting the coalesced UI refresh on the next frame.";
    }
    private void ExpansionRefresh()
    {
        Check(Adapter.Field(Adapter.Session,"openTerminal")==null,"Remote terminal kept a stale accessible view after its parent became unowned.");
        Check(unownedWire.spritePath.EndsWith("-off",StringComparison.Ordinal),"Unowned cable did not remain safely inactive.");
        GeneralHelper.SetItemOwned(unownedWire,true); StowTopology(unownedWire);
        GeneralHelper.SetItemOwned(remoteBay,true);
        Check(Status(remoteTerminal)=="Online","Restored parent ownership did not recover persistent wireless route.");
        Adapter.Call(Adapter.Session,"Close");
        details="Native inventory event closes inaccessible remote terminal without a manual refresh; restoring parent ownership recovers the existing wireless pair.";
    }
}
