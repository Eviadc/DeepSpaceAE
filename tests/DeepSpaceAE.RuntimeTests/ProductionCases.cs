using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DeepSpaceAE.RuntimeTests;

public sealed partial class RuntimePilot
{
    private GameItem productionBay=null!,ordinaryMachineBay=null!,productionCore=null!,productionTerminal=null!,productionDrive=null!,productionDisk=null!,productionInterface=null!;
    private GameItem productionMachine=null!,productionImport=null!,productionDrawer=null!,productionStorageBus=null!,productionSeed=null!;
    private int[] productionIds=Array.Empty<int>();
    private readonly Dictionary<string,int> productionKeys=new();
    private bool CoreOnline(GameItem coreItem)
    {
        var board=Adapter.Board(); var id=Adapter.Id(coreItem);
        var membership=(IDictionary)Adapter.Property(Adapter.Evaluation(board),"ComponentCore")!;
        return membership.Contains(id) && Equals(membership[id],id)
            && ((IEnumerable)Adapter.Field(board,"Powered")!).Cast<string>().Contains(id);
    }
    private GameGridInventory BayGrid(GameItem bay)=>((List<GameInventory>)Adapter.Static("GameSession","Contents",bay)!).OfType<GameGridInventory>().First();
    private GameItem PlaceBay(GameGridInventory grid,string id,int gx,int gy)
    {
        var item=Spawn(id); Add(item); GridAt(grid,item,gx,gy); return item;
    }
    private GameItem[] StockDrawer(GameInventory data,int count)
    {
        // Toilet paper is natively unstackable: preserve separate real instances.
        var items=new List<GameItem>();
        for(int i=0;i<count;i++)
        { var item=Spawn("toilet_paper"); Add(item); Adapter.Move(item,data); items.Add(item); }
        return items.ToArray();
    }
    private void UsabilityMachineBays()
    {
        Adapter.Call(Adapter.Session,"Close");
        ordinaryMachineBay=Spawn("machine_bay"); Add(ordinaryMachineBay);
        var ordinary=BayGrid(ordinaryMachineBay);
        var ordinaryCore=PlaceBay(ordinary,Product+".core",0,0);
        var ordinaryTerminal=PlaceBay(ordinary,Product+".terminal",3,0);
        var ordinaryPower=Spawn("energy_credit"); Add(ordinaryPower); Adapter.Move(ordinaryPower,(GameInventory)Adapter.Static("PartStorage","Inventory",ordinaryCore)!);
        Check(CoreOnline(ordinaryCore) && Status(ordinaryTerminal)=="Online","Ordinary machine bay is inactive without a wireless connector.");
        productionBay=Spawn("machine_bay_ext"); Add(productionBay); var grid=BayGrid(productionBay);
        productionCore=PlaceBay(grid,Product+".core",0,0);
        var bayPower=Spawn("energy_credit"); Add(bayPower); Adapter.Move(bayPower,(GameInventory)Adapter.Static("PartStorage","Inventory",productionCore)!);
        productionDrive=PlaceBay(grid,Product+".disk_drive",3,0);
        productionTerminal=PlaceBay(grid,Product+".terminal",5,0);
        productionInterface=PlaceBay(grid,Product+".item_interface",3,2);
        productionDisk=Spawn(Product+".disk_small"); Add(productionDisk); Adapter.Move(productionDisk,Adapter.Slots("DiskStorage","Bays",productionDrive)[0]);
        PlaceBay(grid,Product+".pipeline",0,3); var intake=PlaceBay(grid,Product+".pipeline",0,4);
        productionMachine=PlaceBay(grid,"hydroponic",0,5);
        productionImport=Mount(intake,"import_bus",2);
        productionDrawer=PlaceBay(grid,Product+".drawer",5,2);
        PlaceBay(grid,Product+".pipeline",4,4); var storage=PlaceBay(grid,Product+".pipeline",5,4);
        productionStorageBus=Mount(storage,"item_storage_bus",0);
        productionSeed=Spawn("nutrifruit_seed"); Add(productionSeed); StowTopology(productionSeed);
        Check(CoreOnline(productionCore),"Expanded machine bay core is not powered.");
        foreach(var item in new[]{productionDrive,productionTerminal,productionInterface,productionImport,productionStorageBus})
            Check(Status(item)=="Online","Expanded machine bay component is offline: "+item.identifier);
        var evaluation=Adapter.Evaluation(Adapter.Board());
        Check((string)Adapter.Property(Adapter.Indexed(evaluation,"Devices",Adapter.Id(productionTerminal)),"CoreId")! ==Adapter.Id(productionCore)
            && Status(terminal)=="Online","Separate machine-bay coordinates merged unrelated cores.");
        var paper=Spawn("toilet_paper"); Add(paper);
        Check((int)Adapter.Call(Adapter.Session,"DepositInterface",productionInterface,paper,paper.unitCount)!>0
            && ReferenceEquals(paper.parentInventory,Adapter.Data(productionDisk)),"Machine bay interface failed real disk insertion.");
        Adapter.Move(paper,root); StowTopology(paper);
        productionKeys["core"]=productionCore.GetUniqueID(); productionKeys["terminal"]=productionTerminal.GetUniqueID();
        productionKeys["disk"]=productionDisk.GetUniqueID(); productionKeys["import"]=productionImport.GetUniqueID();
        productionKeys["drawer"]=productionDrawer.GetUniqueID(); productionKeys["storage"]=productionStorageBus.GetUniqueID();
        details="Owned native machine_bay and machine_bay_ext work without wireless connectors. Independent cores do not collide across inventories; a real interface deposit reached the local disk. Hydroponic intake points at the real machine.";
    }
    private void UsabilityEmptyFilters()
    {
        var sample=Spawn("small_bottled_water"); Add(sample);
        foreach(string kind in new[]{"item_storage_bus","import_bus","export_bus","formation_bus","annihilation_bus"})
        {
            var bus=Mount(usabilityCable,kind,3);
            Check((bool)Adapter.Property(Adapter.Static("PartStorage","Policy",bus)!,"Exclude")!,"New filter is not a blacklist: "+kind);
            Check((bool)Adapter.Static("IoBus","Matches",bus,sample)!,"Empty default blacklist rejects an item.");
            Adapter.Call(Adapter.Session,"ConfigureNumber",bus,"exclude",0);
            Check(!(bool)Adapter.Static("IoBus","Matches",bus,sample)!,"Empty item whitelist allows an item.");
            Adapter.Call(Adapter.Session,"ConfigureNumber",bus,"io_exact",1);
            Check(!(bool)Adapter.Static("IoBus","Matches",bus,sample)!,"Empty exact whitelist allows an item.");
            if(kind=="import_bus" || kind=="export_bus")
            {
                bus.ToggleSlotItem();
                Check(!(bool)Adapter.Static("IoBus","Matches",bus,sample)!,"Empty fluid whitelist allows a container.");
                Adapter.Call(Adapter.Session,"ConfigureNumber",bus,"exclude",1);
                Check((bool)Adapter.Static("IoBus","Matches",bus,sample)!,"Empty fluid blacklist rejects a container.");
                bus.ToggleSlotItem();
            }
            bus.state.dict.Remove(Product+".exclude");
            Adapter.Static("PartStorage","String",bus,"filters",sample.identifier);
            Check(!(bool)Adapter.Property(Adapter.Static("PartStorage","Policy",bus)!,"Exclude")!,"Legacy configured whitelist changed to a blacklist.");
            Adapter.Call(Adapter.Session,"SetFilter",bus,0,"");
            Check(!(bool)Adapter.Static("IoBus","Matches",bus,sample)!,"Clearing a legacy whitelist changed its mode.");
            var tagCard=Spawn(Product+".tag_upgrade"); Add(tagCard); Adapter.Move(tagCard,(GameInventory)Adapter.Static("PartStorage","Inventory",bus)!);
            Adapter.Call(Adapter.Session,"ConfigureNumber",bus,"exclude",0);
            Check(!(bool)Adapter.Static("IoBus","Matches",bus,sample)!,"Empty tag whitelist allows an item.");
            Adapter.Call(Adapter.Session,"ConfigureNumber",bus,"exclude",1);
            Check((bool)Adapter.Static("IoBus","Matches",bus,sample)!,"Empty tag blacklist rejects an item.");
            Unmount(bus);
        }
        StowTopology(sample);
        var disk=Adapter.Data(productionDisk); disk.LockInv();
        try
        {
            var paper=Spawn("toilet_paper"); Add(paper);
            Adapter.Call(Adapter.Session,"ConfigureNumber",productionStorageBus,"exclude",0);
            Check((int)Adapter.Call(Adapter.Session,"PreviewInterface",productionInterface,paper)! ==0,"Empty storage whitelist allowed a real interface deposit.");
            Adapter.Call(Adapter.Session,"ConfigureNumber",productionStorageBus,"exclude",1);
            Check((int)Adapter.Call(Adapter.Session,"DepositInterface",productionInterface,paper,paper.unitCount)!>0
                && ReferenceEquals(paper.parentInventory,Adapter.Static("DrawerStorage","Data",productionDrawer)),"Empty storage blacklist rejected a real interface deposit.");
            Adapter.Move(paper,root); StowTopology(paper);
        }
        finally { disk.UnlockInv(); }
        Adapter.Call(Adapter.Session,"ConfigureNumber",productionStorageBus,"exclude",0);
        details="All five real bus kinds default to blacklists. Empty item, exact, tag and fluid whitelists reject; empty blacklists allow. A real interface deposit obeys a storage bus's empty whitelist/blacklist while disk fallback is locked.";
    }
    private void UsabilityMachineProduction()
    {
        var waterBottle=Spawn("small_bottled_water"); Add(waterBottle);
        Adapter.Move(waterBottle,MachineHydroponic.GetWaterSlot(productionMachine));
        Check(((IEnumerable<GameItem>)Adapter.Static("IoBus","LiquidTargets",productionMachine,false)!).SequenceEqual(new[]{waterBottle}),
            "Hydroponic fluid export did not find its mounted water bottle.");
        Adapter.Move(waterBottle,root); StowTopology(waterBottle);
        var output=MachineHydroponic.GetOutputSlot(productionMachine); var old=output.mayInventoryAddItemFunc;
        output.mayInventoryAddItemFunc=(_,_)=>true; // Same output gate used during native harvest.
        var ctx=new PlantHarvestContext{seed=productionSeed,hydroponic=productionMachine,output=output,
            plant=HydroponicHelper.GetPlant(productionSeed),modules=MachineHelper.GetModules(productionMachine),health=100,harvestCount=2};
        var disk=Adapter.Data(productionDisk);
        try
        {
            PlantHarvest.DefaultHarvest(ctx);
            Check(output.childItems.Sum(i=>i.unitCount)==2 && disk.childItems.Count==0,"Fresh harvest did not stay in the output grid.");
            output.overrideLockInsert=true;
            Adapter.Call(Adapter.Session,"ConfigureNumber",productionImport,"io_enabled",0);
            PlantHarvest.DefaultHarvest(ctx);
            Check(output.childItems.Sum(i=>i.unitCount)==2 && disk.childItems.Sum(i=>i.unitCount)==2,"Overflow harvest failed with scheduled collection disabled.");
            Adapter.Call(Adapter.Session,"ConfigureNumber",productionImport,"io_overflow",0);
            var spill=Spawn("toilet_paper"); PlantHarvest.Output(ctx,spill);
            Check(spill.parentInventory!=null && spill.parentInventory.GetParentItem()==null,"Disabled overflow did not spill to ship storage.");
            output.UnlockInv();
            RunBus(productionImport);
            Check(output.childItems.Count==0 && disk.childItems.Sum(i=>i.unitCount)==4,"Disabling overflow blocked manual collection.");
            Adapter.Call(Adapter.Session,"ConfigureNumber",productionImport,"io_enabled",1);
            Adapter.Call(Adapter.Session,"ConfigureNumber",productionImport,"io_overflow",1);
            output.mayInventoryAddItemFunc=(_,inv)=>inv.childItems.Sum(i=>i.unitCount)<3;
            var partial=Spawn("toilet_paper"); partial.SetAmount(4);
            Check(GraphUtils.TryAcceptAll(output,partial)==0 && output.childItems.Sum(i=>i.unitCount)==3
                && disk.childItems.Where(i=>i.identifier==partial.identifier).Sum(i=>i.unitCount)==1,"Partial output did not split three local units and one overflow unit.");
            foreach(var item in output.childItems.Concat(disk.childItems).ToArray()) { Adapter.Move(item,root); StowTopology(item); }
        }
        finally { output.UnlockInv(); disk.UnlockInv(); output.mayInventoryAddItemFunc=old; }
        var grid=BayGrid(productionBay);
        Adapter.Move(productionMachine,root);
        foreach(var pair in new[]{("furnace","metal_ingot"),("chem_finisher","caffeine_pill")})
        {
            var machine=PlaceBay(grid,pair.Item1,0,5);
            var machineOutput=((List<GameInventory>)Adapter.Static("IoBus","MachineSlots",machine,true)!).Single();
            var gate=machineOutput.mayInventoryAddItemFunc;
            machineOutput.mayInventoryAddItemFunc=(_,_)=>true;
            var items=new[]{Spawn(pair.Item2),Spawn(pair.Item2),Spawn(pair.Item2)};
            foreach(var item in items)
            {
                if(pair.Item2=="metal_ingot") IngotPurityHelper.SetPurity(item,IngotPurityHelper.INGOT_PURITY_FAIR);
                else
                {
                    ChemicalHelper.RemoveAllLiquid(item);
                    ChemicalHelper.SetChemContentUL(item,"caffeine",5000);
                    ChemicalProductHelper.GradeProduct(item);
                }
                Add(item); Adapter.Move(item,machineOutput);
            }
            Adapter.Call(Adapter.Session,"CaptureGhost",productionImport,0,items[0]);
            Adapter.Call(Adapter.Session,"ConfigureNumber",productionImport,"exclude",0);
            Adapter.Call(Adapter.Session,"ConfigureNumber",productionImport,"io_exact",1);
            // The first two inputs are identical. Only the third differs in incidental state.
            GeneralHelper.TrackBuyPrice(items[2],27);
            items[2].GetTagReadonly("AE_TEST_ABSENT");
            Check(items.All(i=>(bool)Adapter.Static("IoBus","Matches",productionImport,i)!),"Exact whitelist rejected a matching item: "+pair.Item2);
            string legacy=(string)Adapter.Static("TerminalStackKey","Read",items[0],null,false,false)!;
            Check(Equals(Adapter.Static("ExactItemFilter","Normalize",legacy),Adapter.Static("ExactItemFilter","Read",items[0])),
                "Legacy exact sample did not produce the current matching fields.");
            Adapter.Static("PartStorage","String",productionImport,"filter_state_0",legacy);
            Adapter.Call(Adapter.Session,"OpenPart",productionImport);
            var window=(PixelWindow)Adapter.Field(Adapter.Session,"partWindow")!;
            var tile=window.handler.GetComponentsInChildren<TreeNodeRender>(true).Select(n=>n.element)
                .OfType<MachineryButton>().First(b=>b.description?.Contains(pair.Item2)==true);
            string qualityTag=pair.Item2=="metal_ingot"?"INGOT_PURITY_TAG":"CHEMICAL_PRODUCT_PURITY_TAG";
            Check(tile.HasTooltip() && tile.description.Contains(qualityTag)
                && tile.description.Contains(ModHelper.GetLocalized(Product,"ui_internal_tag",
                    ModHelper.GetLocalized(Product,"tag_"+qualityTag.ToLowerInvariant()))) && !tile.description.Contains("BOUGHT_PRICE_TAG"),
                "Filter tile did not expose its exact properties on hover.");
            Press(FindButton(window,ModHelper.GetLocalized(Product,"ui_io_run")));
            Check(items.All(i=>ReferenceEquals(i.parentInventory,disk)),"One manual click left an identical item in "+pair.Item1);
            Adapter.Call(Adapter.Session,"ClosePart");
            machineOutput.mayInventoryAddItemFunc=gate;
            if(pair.Item1=="furnace") CheckFurnaceOverflow(machine,(GameGridInventory)machineOutput,disk,items[0]);
            else CheckFinisherProduction(machine,(GameGridInventory)machineOutput,disk);
            foreach(var item in disk.childItems.ToArray()) { Adapter.Move(item,root); StowTopology(item); }
            machine.Destroy();
        }
        GridAt(grid,productionMachine,0,5);
        Adapter.Call(Adapter.Session,"SetFilter",productionImport,0,"");
        Adapter.Call(Adapter.Session,"ConfigureNumber",productionImport,"exclude",1);
        Adapter.Call(Adapter.Session,"ConfigureNumber",productionImport,"io_exact",0);
        details="Native harvest stays in available output space and spills only its remainder. One native button click collects identical ingots and pills, including a third instance with a different purchase price. Turbo furnace output and finisher output use the finished grade; the finisher takes liquid in place.";
    }

    private void CheckFurnaceOverflow(GameItem furnace,GameGridInventory output,GameInventory disk,GameItem sample)
    {
        var input=((List<GameInventory>)Adapter.Static("IoBus","MachineSlots",furnace,false)!).Single();
        var power=MachineHelper.GetBatterySlot(furnace);
        var battery=Spawn("energy_credit"); Add(battery); Adapter.Move(battery,power);
        var shape=output.inventoryShape;
        output.SetShape("1",1); // No usable cells, as in a fully occupied grid.
        foreach(var ore in new[]{Spawn("scrap_metal"),Spawn("scrap_metal")}) { Add(ore); Adapter.Move(ore,input); }
        Adapter.Call(Adapter.Session,"ConfigureNumber",productionImport,"io_overflow",0);
        MachineTurboBoosterAdv.BoostMachine(furnace);
        Check(input.childItems.Count==2,"Disabled overflow let a full furnace consume its inputs.");
        var before=new HashSet<int>(MainItems().Select(i=>i.GetUniqueID()));
        Adapter.Call(Adapter.Session,"ConfigureNumber",productionImport,"io_overflow",1);
        MachineTurboBoosterAdv.BoostMachine(furnace);
        var ingot=MainItems().Single(i=>i.identifier=="metal_ingot" && !before.Contains(i.GetUniqueID()));
        Check(input.childItems.Count==0 && ingot.parentInventory!=null && ingot.parentInventory.GetParentItem()==null
            && IngotPurityHelper.GetPurity(ingot)!=IngotPurityHelper.GetPurity(sample),"Wrong-grade ingot failed to spill after the furnace ran.");
        Adapter.Call(Adapter.Session,"CaptureGhost",productionImport,0,ingot);
        foreach(var ore in new[]{Spawn("scrap_metal"),Spawn("scrap_metal")}) { Add(ore); Adapter.Move(ore,input); }
        MachineTurboBoosterAdv.BoostMachine(furnace);
        Check(input.childItems.Count==0 && disk.childItems.Any(i=>i.identifier=="metal_ingot" &&
            IngotPurityHelper.GetPurity(i)==IngotPurityHelper.GetPurity(ingot)),"Matching finished ingot missed overflow collection.");
        output.SetShape(shape.width,shape.height);
    }
    private void CheckFinisherProduction(GameItem machine,GameGridInventory output,GameInventory disk)
    {
        var grid=BayGrid(productionBay);
        var intakeCable=productionImport.parentInventory.GetParentItem();
        Unmount(productionImport);
        var export=Mount(intakeCable,"export_bus",2);
        export.ToggleSlotItem();
        Adapter.Call(Adapter.Session,"ConfigureNumber",productionStorageBus,"cable_face",1);
        var source=Spawn("small_bottled_water"); Add(source);
        GridAt(grid,source,6,4);
        WaterHelper.EmptyContainer(source);
        WaterHelper.AddLiquid(source,"dream_cap_extract",8000);
        productionStorageBus.ToggleSlotItem();
        Adapter.Call(Adapter.Session,"ConfigureNumber",productionStorageBus,"exclude",1);
        var receptacle=((List<GameInventory>)Adapter.Static("GameSession","Contents",machine)!).OfType<GameSlotInventory>().Single();
        var reagent=Spawn("small_bottled_water"); Add(reagent); WaterHelper.EmptyContainer(reagent);
        RunBus(export);
        Check(WaterHelper.GetTotalVolume(source)==8000,"Fluid export consumed liquid with no bottle mounted.");
        Adapter.Move(reagent,receptacle);
        RunBus(export);
        Check(WaterHelper.GetTotalVolume(source)==0 && WaterHelper.GetTotalVolume(reagent)==8000 &&
            ReferenceEquals(reagent.parentInventory,receptacle),"Finisher did not receive liquid in its mounted bottle.");

        Unmount(export);
        Adapter.Move(productionImport,(GameInventory)Adapter.Static("PartStorage","Inventory",intakeCable)!);
        Adapter.Static("PartStorage","Number",productionImport,"cable_face",2);
        productionStorageBus.ToggleSlotItem();
        Adapter.Call(Adapter.Session,"ConfigureNumber",productionStorageBus,"exclude",0);
        Adapter.Move(source,root); StowTopology(source);
        Adapter.Call(Adapter.Session,"ConfigureNumber",productionStorageBus,"cable_face",0);

        machine.ModifyTag("SELECTED_CHEM_ID_TAG",tag=>tag.Enable().SetString("dream_dust"));
        var panel=((MachineryPanelRow)machine.contentWindow.childElement).Panels.Single(p=>p.role=="control");
        var button=((MachineryStack)panel.content).elements.OfType<MachineryButton>().Last();
        Press(button);
        var sample=output.childItems.Single();
        Adapter.Call(Adapter.Session,"CaptureGhost",productionImport,0,sample);
        output.overrideLockInsert=true;
        Press(button);
        var captured=disk.childItems.Single(i=>i.identifier=="dream_dust");
        Check((bool)Adapter.Static("IoBus","Matches",productionImport,captured)!,"Finisher overflow did not match the final graded sample.");
        Adapter.Call(Adapter.Session,"CaptureGhost",productionImport,0,disk.childItems.First(i=>i.identifier=="caffeine_pill"));
        var before=new HashSet<int>(MainItems().Select(i=>i.GetUniqueID()));
        Press(button);
        Check(MainItems().Any(i=>i.identifier=="dream_dust" && !before.Contains(i.GetUniqueID()) && i.parentInventory?.GetParentItem()==null),
            "Rejected finisher product failed to spill into ship storage.");
        output.UnlockInv();
    }
    private void UsabilityVoidOverflow()
    {
        var data=(GameInventory)Adapter.Static("DrawerStorage","Data",productionDrawer)!;
        Check(!(bool)Adapter.Static("DrawerStorage","VoidOverflow",productionDrawer)!,"Overflow destruction is enabled by default.");
        var original=StockDrawer(data,15);
        var incoming=Enumerable.Range(0,4).Select(_=>{ var item=Spawn("toilet_paper"); Add(item); return item; }).ToArray();
        Check(data.TryFindOneValidInventorySlot(incoming[0]).numTransfer==1,"Normal drawer did not respect remaining capacity.");
        Adapter.Call(Adapter.Session,"OpenPart",productionDrawer);
        var panel=(PixelWindow)Adapter.Field(Adapter.Session,"partWindow")!;
        var field=panel.handler.GetComponentsInChildren<TreeNodeRender>(true).Select(n=>n.element)
            .Where(e=>e?.GetType().FullName=="DeepSpaceAE.GameAdapter.NativeWidgets+Field").Distinct().Single();
        ((CustomUIElement)Adapter.Field(field!,"Control")!).SetBool(true,true);
        Check((bool)Adapter.Static("DrawerStorage","VoidOverflow",productionDrawer)!,"Actual overflow toggle callback did not save.");
        Check(incoming.All(i=>data.TryFindOneValidInventorySlot(i).TryAcceptOnce(1)==1)
            && original.All(i=>i.unitCount==1 && ReferenceEquals(i.parentInventory,data))
            && incoming.Count(i=>((GameItemElement)i).IsDestroyed())==3 && data.childItems.Sum(i=>i.unitCount)==16,
            "Overflow changed original stock or retained too many new units.");
        var extra=Spawn("toilet_paper"); Add(extra); var stale=data.TryFindOneValidInventorySlot(extra);
        Adapter.Static("DrawerStorage","SetVoidOverflow",productionDrawer,false);
        Check(stale.TryAcceptOnce(1)==0 && ReferenceEquals(extra.parentInventory,root),"Stale void preview detached an item after disabling overflow.");
        Adapter.Static("DrawerStorage","SetVoidOverflow",productionDrawer,true);
        root.LockInv();
        try { Check(data.TryFindOneValidInventorySlot(extra).TryAcceptOnce(1)==0,"Void ignored a locked source inventory."); }
        finally { root.UnlockInv(); }
        Check(data.TryFindOneValidInventorySlot(extra).TryAcceptOnce(1)==1 && ((GameItemElement)extra).IsDestroyed()
            && data.childItems.Sum(i=>i.unitCount)==16,"Full drawer did not consume only the new overflow.");
        var wrong=Spawn("toothpaste"); Add(wrong);
        Check(data.TryFindOneValidInventorySlot(wrong).numTransfer==0 && !((GameItemElement)wrong).IsDestroyed(),"Void accepted/destroyed a wrong item kind.");
        StowTopology(wrong); Adapter.Static("DrawerStorage","ToggleLock",productionDrawer);
        var unpaid=Spawn("toilet_paper"); Add(unpaid); GeneralHelper.SetItemOwned(unpaid,false);
        Check(data.TryFindOneValidInventorySlot(unpaid).numTransfer==0 && !((GameItemElement)unpaid).IsDestroyed(),"Overflow consumed an unpaid item.");
        GeneralHelper.SetItemOwned(unpaid,true); StowTopology(unpaid);
        Adapter.Call(Adapter.Session,"OpenPart",productionDrawer); StartCoroutine(Screenshot("07-drawer-overflow"));
        details="Real native overflow toggle defaults off. Partial capacity retains 1 of 4 incoming units; full capacity voids only new matching units. Wrong kind, stale toggles and locked source remain intact; 16 existing units and type lock are ready for reload.";
    }
    private void UsabilityReducedStock()
    {
        var coreAssembly=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="DeepSpaceAE.Core");
        var stock=(IEnumerable<string>)coreAssembly.GetType("DeepSpaceAE.Core.Network.AcquisitionCatalog",true)!.GetMethod("RollStock")!
            .Invoke(null,new object[]{new Func<int,bool>(_=>true),new Func<int,int,int>((_,max)=>max)})!;
        var table=EmporiumEntry.Instance.frontInvinvElement;
        Check(table.childItems.Count==0,"Reduced-stock fixture requires an empty counter.");
        int width=table.inventoryShape.width,height=table.inventoryShape.height;
        var oldClient=PlayerStore.Instance.currentClientInstance;
        var keys=new[]{"steve_stock","steve_served_day","steve_appointment_day"};
        var backup=keys.ToDictionary(k=>k,k=>ModHelper.GetModData(Product,k,null));
        try
        {
            table.SetShape(18,4);
            ModHelper.SetModData(Product,"steve_stock",string.Join("\n",stock)); ModHelper.SetModDataInt(Product,"steve_served_day",-1);
            var client=(StoreClient)Adapter.Call(AcquisitionInstance(),"CreateSteve",StoreStation.GetDayCounter())!;
            PlayerStore.Instance.currentClientInstance=StoreClientInstance.CreateClientInstance(client); client.mainDialogue.ExecuteEndAction();
            foreach(string kind in new[]{"item_storage_bus","import_bus","export_bus","pipeline"})
                Check(table.childItems.Any(i=>i.identifier==Product+"."+kind),"Late-listed stock was crowded out: "+kind);
            Check(table.childItems.Count(i=>i.identifier==Product+".wireless_connector")%2==0,"Reduced stock contains half a wireless pair.");
        }
        finally
        {
            foreach(var item in table.childItems.ToArray()) item.Destroy();
            table.SetShape(width,height); PlayerStore.Instance.currentClientInstance=oldClient;
            foreach(var pair in backup) if(pair.Value==null) ModHelper.RemoveModData(Product,pair.Key); else ModHelper.SetModData(Product,pair.Key,pair.Value);
        }
        details="All stock chances forced present at maximum reduced quantities: actual supplier placement on the native 18x4 counter still offers storage/import/export buses and cables, with only complete wireless pairs. Original counter and appointment state restored.";
    }
    private void UsabilityContainerBuses()
    {
        var outputs=(List<GameInventory>)Adapter.Static("IoBus","TargetSlots",productionMachine,true)!;
        var inputs=(List<GameInventory>)Adapter.Static("IoBus","TargetSlots",productionMachine,false)!;
        Check(outputs.SequenceEqual(new[]{MachineHydroponic.GetOutputSlot(productionMachine)})
            && !inputs.Contains(MachineHelper.GetBatterySlot(productionMachine))
            && !inputs.Contains(MachineHelper.GetModuleInv(productionMachine)),"General storage support exposed a machine's auxiliary bays.");
        // Native storage bays cannot nest inside machine bays. Keep the real
        // containers in the main inventory, linked to the bay network wirelessly.
        var containers=new[]{Spawn("storage_bay"),Spawn("hypertech_bay")};
        int width=3+containers.Max(i=>i.modifiedShape.globalWidth),height=Math.Max(2,containers.Max(i=>i.modifiedShape.globalHeight));
        int cx=0,cy=0; bool free=false;
        for(int gy=1;gy+height<=root.inventoryShape.height && !free;gy++)
            for(int gx=1;gx+width<=root.inventoryShape.width && !free;gx++)
                if(!root.childItems.Any(i=>i.modifiedShape.minX<gx+width && i.modifiedShape.maxX>=gx-1
                    && i.modifiedShape.minY<gy+height && i.modifiedShape.maxY>=gy-1))
                { cx=gx; cy=gy; free=true; }
        if(!free)
        {
            Guard(); cx=root.inventoryShape.width+1; cy=1;
            root.SetShape(cx+width+1,Math.Max(root.inventoryShape.height,height+2));
        }
        var remote=PlaceBay(BayGrid(productionBay),Product+".wireless_connector",7,0);
        var local=Spawn(Product+".wireless_connector"); At(local,cx,cy); Pair(local,remote);
        var incomingCable=Spawn(Product+".pipeline"); At(incomingCable,cx+2,cy);
        var outgoingCable=Spawn(Product+".pipeline"); At(outgoingCable,cx+2,cy+1);
        var importBus=Mount(incomingCable,"import_bus",1); var export=Mount(outgoingCable,"export_bus",1);
        var disk=Adapter.Data(productionDisk);
        Adapter.Call(Adapter.Session,"ConfigureNumber",export,"exclude",0);
        Adapter.Call(Adapter.Session,"SetFilter",export,0,"toilet_paper");
        foreach(var container in containers)
        {
            string kind=container.identifier; At(container,cx+3,cy);
            var slots=(List<GameInventory>)Adapter.Static("IoBus","TargetSlots",container,true)!;
            var destinations=(List<GameInventory>)Adapter.Static("IoBus","TargetSlots",container,false)!;
            Check(slots.Count==(kind=="hypertech_bay"?2:1) && slots.SequenceEqual(destinations),"Ordinary/two-page storage discovery differs by direction.");
            Check(((List<GameInventory>)Adapter.Static("IoBus","MachineSlots",container,true)!).Count==0,"Ordinary insertion was classified as new machine output.");
            var paper=Spawn("toilet_paper"); Add(paper); Adapter.Move(paper,slots.Last());
            Check(ReferenceEquals(paper.parentInventory,slots.Last()),"Ordinary storage insertion was intercepted as production.");
            Adapter.Call(Adapter.Session,"ConfigureNumber",importBus,"exclude",0);
            Adapter.Call(Adapter.Session,"SetFilter",importBus,0,""); RunBus(importBus);
            Check(ReferenceEquals(paper.parentInventory,slots.Last()),"Container import ignored an empty whitelist.");
            Adapter.Call(Adapter.Session,"SetFilter",importBus,0,paper.identifier);
            slots.Last().LockInv();
            try { RunBus(importBus); Check(ReferenceEquals(paper.parentInventory,slots.Last()),"Import ignored a locked ordinary source."); }
            finally { slots.Last().UnlockInv(); }
            RunBus(importBus); Check(ReferenceEquals(paper.parentInventory,disk),"Import did not extract from ordinary storage/inactive tab.");
            foreach(var slot in slots) slot.LockInv();
            try
            {
                RunBus(export); Check(ReferenceEquals(paper.parentInventory,disk),"Export ignored locked ordinary storage.");
                slots.Last().UnlockInv(); RunBus(export);
                Check(ReferenceEquals(paper.parentInventory,slots.Last()),"Export did not reach the available ordinary storage/inactive tab.");
            }
            finally { foreach(var slot in slots) slot.UnlockInv(); }
            GeneralHelper.SetItemOwned(container,false);
            try { RunBus(importBus); Check(ReferenceEquals(paper.parentInventory,slots.Last()),"Import took goods from an unowned container."); }
            finally { GeneralHelper.SetItemOwned(container,true); }
            var bottle=Spawn("small_bottled_water"); Add(bottle); Adapter.Move(bottle,slots.Last());
            foreach(bool import in new[]{true,false})
                Check(((IEnumerable<GameItem>)Adapter.Static("IoBus","LiquidTargets",container,import)!).Contains(bottle),"Fluid bus did not discover an ordinary stored liquid container.");
            Adapter.Move(bottle,root); StowTopology(bottle);
            if(kind=="storage_bay") { Adapter.Move(paper,root); StowTopology(paper); container.Destroy(); }
            else
            {
                productionKeys["container"]=container.GetUniqueID(); productionKeys["container_export"]=export.GetUniqueID();
                productionKeys["container_import"]=importBus.GetUniqueID();
                productionKeys["container_stock"]=paper.GetUniqueID();
            }
        }
        var drawerSlots=(List<GameInventory>)Adapter.Static("IoBus","TargetSlots",productionDrawer,true)!;
        Check(drawerSlots.Count==1 && ReferenceEquals(drawerSlots[0],Adapter.Static("DrawerStorage","Data",productionDrawer)),"Drawer automation exposed upgrade cards.");
        productionIds=ordinaryMachineBay.FindAllChildItems().Concat(productionBay.FindAllChildItems())
            .Concat(containers[1].FindAllChildItems()).Concat(incomingCable.FindAllChildItems()).Concat(outgoingCable.FindAllChildItems())
            .Concat(new[]{ordinaryMachineBay,productionBay,containers[1],local,incomingCable,outgoingCable})
            .Distinct().Select(i=>i.GetUniqueID()).ToArray();
        details="Real import/export transfers passed for an ordinary storage bay and the inactive page of a two-page native bay. Empty whitelists, locked sources/destinations and unowned containers remain protected. Fluid targets include stored bottles; machine auxiliaries and drawer cards are excluded.";
    }
    private void UsabilityProductionReload()
    {
        var all=MainItems(); foreach(int id in productionIds) Check(all.Count(i=>i.GetUniqueID()==id)==1,"Production fixture missing/duplicated after reload: "+id);
        GameItem Get(string key)=>all.Single(i=>i.GetUniqueID()==productionKeys[key]);
        Check(CoreOnline(Get("core")) && Status(Get("terminal"))=="Online" && Status(Get("import"))=="Online","Machine bay network did not restore.");
        var containerSlots=(List<GameInventory>)Adapter.Static("IoBus","TargetSlots",Get("container"),true)!;
        var stored=Get("container_stock");
        Check(containerSlots.Contains(stored.parentInventory),"Ordinary storage contents did not restore.");
        RunBus(Get("container_import")); Check(ReferenceEquals(stored.parentInventory,Adapter.Data(Get("disk"))),"Ordinary container import failed after reload.");
        RunBus(Get("container_export")); Check(containerSlots.Contains(stored.parentInventory),"Ordinary container export failed after reload.");
        var saved=Get("drawer"); var data=(GameInventory)Adapter.Static("DrawerStorage","Data",saved)!;
        Check((bool)Adapter.Static("DrawerStorage","VoidOverflow",saved)! && (bool)Adapter.Static("DrawerStorage","IsLocked",saved)!
            && data.childItems.Sum(i=>i.unitCount)==16,"Reload lost overflow/type-lock state or destroyed saved stock.");
        var probe=Spawn("toilet_paper"); Add(probe);
        var policy=Adapter.Static("PartStorage","Policy",Get("storage"))!;
        Check(!(bool)Adapter.Property(policy,"Exclude")! && !(bool)Adapter.Call(policy,"CanInsert",probe.identifier,Array.Empty<string>())!,"Saved empty whitelist no longer rejects items.");
        probe.Destroy(); Adapter.Call(Adapter.Session,"OpenPart",saved); StartCoroutine(Screenshot("08-reloaded-overflow"));
        details="Native save/reload retained both machine bays and every recorded child identity, the active bay network, an empty rejecting whitelist and a locked drawer with overflow enabled and all 16 saved units intact.";
    }
}
