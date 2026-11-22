using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeepSpaceAE.RuntimeTests;

public sealed partial class RuntimePilot
{
    private GameItem drawerController=null!,drawerPeer=null!,drawerMiddle=null!,drawerEnd=null!,drawerIsolated=null!;
    private GameItem drawerMachine=null!,drawerImport=null!,drawerExport=null!,drawerFluid=null!;
    private GameInventory drawerStock=null!;
    private readonly List<GameItem> drawerInstances=new();
    private int[] drawerSaved=Array.Empty<int>(),drawerStockIds=Array.Empty<int>();
    private Dictionary<int,int> drawerPrices=new();
    private double drawerSmallValidation;
    private GameInventory DrawerData(GameItem item)=>(GameInventory)Adapter.Static("DrawerStorage","Data",item)!;
    private long DrawerUsed(GameItem item)=>(long)Adapter.Property(DrawerData(item),"Used")!;
    private GameItem[] DrawerMembers(GameItem controller)
        =>((IEnumerable)Adapter.Call(Adapter.Field(Adapter.Board(),"Layout")!,"DrawerMembers",controller)!).Cast<GameItem>().ToArray();
    private GameItem Put(GameInventory inventory,string id)
    {
        var item=Spawn(id); var slot=inventory.TryFindOneValidInventorySlot(item);
        Check(slot!=null && slot.numTransfer>=item.unitCount,"Fixture destination rejected "+id);
        Check(slot!.TryAcceptOnce(item.unitCount)>0,"Fixture insertion failed"); return item;
    }
    private void DrawerSetup()
    {
        TopologySetup();
        root.SetShape(Math.Max(root.inventoryShape.width,x+28),Math.Max(root.inventoryShape.height,y+14));
        drawerController=Spawn(Product+".drawer_controller"); At(drawerController,x+3,y);
        terminal=Spawn(Product+".terminal"); At(terminal,x+5,y);
        drive=Spawn(Product+".disk_drive"); At(drive,x+7,y);
        small=Spawn(Product+".disk_large"); Add(small); Adapter.Move(small,Adapter.Slots("DiskStorage","Bays",drive)[0]);
        drawer=Spawn(Product+".drawer"); At(drawer,x+3,y+2);
        drawerMiddle=Spawn(Product+".drawer"); At(drawerMiddle,x+5,y+2);
        drawerEnd=Spawn(Product+".drawer"); At(drawerEnd,x+7,y+2);
        drawerPeer=Spawn(Product+".drawer_controller"); At(drawerPeer,x+9,y+2);
        drawerIsolated=Spawn(Product+".drawer"); At(drawerIsolated,x+10,y+6);
        drawerStock=DrawerData(drawer);
        for(int i=0;i<5;i++) Put((GameInventory)Adapter.Static("DrawerStorage","Upgrades",drawer)!,Product+".capacity_upgrade");
        for(int i=0;i<2;i++) Put((GameInventory)Adapter.Static("DrawerStorage","Upgrades",drawerEnd)!,Product+".capacity_upgrade");
        Check(Status(drawerController)=="Online" && Status(terminal)=="Online" && Status(drawerPeer)=="NoCore","Drawers relayed AE or controller failed direct contact.");
        Check(Status(drawer)=="Absent" && DrawerMembers(drawerController).Length==3,"Ordinary drawers entered the AE topology or adjacency membership differs.");
        details="New 2x2 controller relays AE through real contact; three edge-connected drawers are accessible, a separated drawer is excluded, and a second controller across only drawers remains offline.";
    }
    private double DrawerValidateTime(int iterations)
    {
        var watch=Stopwatch.StartNew(); for(int i=0;i<iterations;i++) drawerStock.Validate(); return watch.Elapsed.TotalMilliseconds;
    }
    private void DrawerSmallLoad()
    {
        for(int i=0;i<512;i++)
        {
            var item=Spawn("toilet_paper"); item.ModifyTag(PlayerStore.BOUGHT_PRICE_TAG,t=>t.Enable().SetInt(i),true);
            var slot=drawerStock.TryFindOneValidInventorySlot(item); Check(slot.numTransfer==1 && slot.TryAcceptOnce(1)==1,"Small drawer load rejected."); drawerInstances.Add(item);
        }
        Adapter.Call(drawerStock,"PrepareDisplay");
        drawerSmallValidation=DrawerValidateTime(200);
        Check(DrawerUsed(drawer)==512,"Small drawer counter differs.");
        details="512 separate native paper instances retained their individual purchase prices; 200 visible-stock validations took "+drawerSmallValidation.ToString("F2")+" ms.";
    }
    private void DrawerLargeLoad()
    {
        var load=Stopwatch.StartNew();
        for(int i=512;i<4096;i++)
        {
            var item=Spawn("toilet_paper"); item.ModifyTag(PlayerStore.BOUGHT_PRICE_TAG,t=>t.Enable().SetInt(i),true);
            var slot=drawerStock.TryFindOneValidInventorySlot(item); Check(slot.numTransfer==1 && slot.TryAcceptOnce(1)==1,"Large drawer load rejected."); drawerInstances.Add(item);
        }
        double inserted=load.Elapsed.TotalMilliseconds;
        Adapter.Call(drawerStock,"PrepareDisplay");
        // Count calls at the native item's sprite callback instead of gating on
        // wall-clock ratios, which are distorted by GC and other enabled mods.
        long validations=0;
        var callbacks=drawerStock.childItems.Select(item=>(Item:item,Original:item.getItemSpriteModifierForItemFunc)).ToArray();
        double elapsed;
        try
        {
            foreach(var pair in callbacks) pair.Item.getItemSpriteModifierForItemFunc=item=>{ validations++; return pair.Original?.Invoke(item); };
            elapsed=DrawerValidateTime(200);
        }
        finally { foreach(var pair in callbacks) pair.Item.getItemSpriteModifierForItemFunc=pair.Original; }
        Check(DrawerUsed(drawer)==4096 && drawerStock.childItems.Count==4096,"Large drawer lost real stock or quantity.");
        Check(drawerStock.childItems.OfType<GameItemElement>().Count(i=>i.handler.gameObject.activeSelf)==1,"Hidden stock was reactivated by validation.");
        Check(validations==200,"200 drawer layouts validated "+validations+" real items instead of only 200 visible representatives.");
        for(int i=0;i<drawerInstances.Count;i++) Check(drawerInstances[i].GetTagReadonly(PlayerStore.BOUGHT_PRICE_TAG).GetInt()==i,"Distinct stored state was overwritten.");
        details="4096 distinct real instances, exact count, one visible renderer and per-instance prices passed. 200 layouts invoked exactly 200 native item sprite callbacks. Additional 3584 insertions: "+inserted.ToString("F0")+" ms; 200 validations: "+elapsed.ToString("F2")+" ms (512-instance baseline "+drawerSmallValidation.ToString("F2")+" ms).";
    }
    private void DrawerCompatibilityModes()
    {
        var config=ModHelper.GetConfig(Product);
        var reads=config.Get("native_inventory_reads") as ConfigEntry<bool>;
        var layout=config.Get("native_drawer_validation") as ConfigEntry<bool>;
        Check(reads!=null && layout!=null,"Native compatibility settings are missing.");
        bool previousReads=reads!.Value,previousLayout=layout!.Value;
        var originals=drawerStock.childItems.ToArray();
        var callbacks=originals.Select(item=>(Item:item,Original:item.getItemSpriteModifierForItemFunc)).ToArray();
        var measurements=new List<string>();
        try
        {
            reads.Set(true); layout.Set(true);
            var probe=Spawn(Product+".drawer"); Add(probe);
            var probeData=DrawerData(probe); probeData.Validate();
            Check(Adapter.Field(probeData,"stock")==null && Adapter.Field(probeData,"nativeLayout")==null,
                "Full compatibility mode still bound private optimization members during construction.");
            probe.Destroy();
            foreach(bool nativeReads in new[]{false,true}) foreach(bool nativeLayout in new[]{false,true})
            {
                reads.Set(nativeReads); layout.Set(nativeLayout);
                var first=(List<GameItem>)Adapter.Property(drawerStock,"Stock")!;
                var second=(List<GameItem>)Adapter.Property(drawerStock,"Stock")!;
                Check(ReferenceEquals(first,second)!=nativeReads,"Inventory read path did not switch.");
                Check(first.SequenceEqual(originals) && second.SequenceEqual(originals),"Compatibility read changed stock identity/order.");
                long validations=0;
                foreach(var pair in callbacks) pair.Item.getItemSpriteModifierForItemFunc=item=>{validations++; return pair.Original?.Invoke(item);};
                double elapsed=DrawerValidateTime(3);
                Check(validations==3L*(nativeLayout?originals.Length:1),"Layout compatibility path validated an unexpected number of items: "+validations);
                foreach(var pair in callbacks) pair.Item.getItemSpriteModifierForItemFunc=pair.Original;
                Check(drawerStock.childItems.OfType<GameItemElement>().Count(i=>i.handler.gameObject.activeSelf)==1,"Compatibility layout exposed hidden stock.");
                var stock=originals[0]; int price=stock.GetTagReadonly(PlayerStore.BOUGHT_PRICE_TAG).GetInt();
                Adapter.Move(stock,root); Adapter.Move(stock,drawerStock);
                Check(DrawerUsed(drawer)==originals.Length && drawerStock.childItems.Count==originals.Length,"Compatibility transfer lost stock.");
                Check(stock.GetTagReadonly(PlayerStore.BOUGHT_PRICE_TAG).GetInt()==price,"Compatibility transfer changed item state.");
                // Native reinsertion appends the withdrawn instance; compare sets below.
                originals=drawerStock.childItems.ToArray();
                Check(Adapter.Static("TerminalStackKey","Read",small,null,false,false)!=null,"Empty native disk lost its terminal grouping key.");
                var nested=Put(Adapter.Data(small),"toilet_paper");
                Check(Adapter.Static("TerminalStackKey","Read",small,null,false,false)==null,"Nonempty native disk was incorrectly grouped as empty.");
                Adapter.Move(nested,root); nested.Destroy();
                measurements.Add($"reads={nativeReads},layout={nativeLayout}: {validations} callbacks/{elapsed:F2} ms");
            }
        }
        finally
        {
            foreach(var pair in callbacks) pair.Item.getItemSpriteModifierForItemFunc=pair.Original;
            reads.Set(previousReads); layout.Set(previousLayout);
        }
        details="All four live compatibility combinations preserved identities/count/state and native withdrawal/reinsertion; authoritative-list vs public snapshot reads and per-item native callbacks verified. "+string.Join("; ",measurements);
    }
    private void DrawerControllerRoutes()
    {
        EditPriority(drawerController,2);
        var incoming=Spawn("toilet_paper"); Add(incoming);
        int accepted=(int)Adapter.Call(Adapter.Session,"DepositInterface",drawerController,incoming,1)!;
        Check(accepted==1 && ReferenceEquals(incoming.parentInventory,drawerStock),"Direct controller deposit missed matching occupied drawer.");
        var probe=Spawn("toilet_paper"); Add(probe);
        var changed=drawerInstances.Last(); changed.ModifyTag("PILOT_DRAWER_MUTATION",t=>t.Enable().SetInt(1));
        Check(drawerStock.TryFindOneValidInventorySlot(probe).numTransfer==0,"A changed hidden instance bypassed homogeneous admission.");
        changed.state.dict.Remove("PILOT_DRAWER_MUTATION"); changed.ValidateShapeState();
        Check(drawerStock.TryFindOneValidInventorySlot(probe).numTransfer==1,"Restoring hidden state did not restore admission.");
        var view=Adapter.Call(Adapter.Session,"ReadInterface",terminal)!;
        var slot=(SlotMarker)Adapter.Call(Adapter.Session,"InterfacePlacement",view,probe)!;
        Check(ReferenceEquals(slot.inventory,drawerStock),"Controller network priority did not beat disk storage.");
        Adapter.Move(incoming,root); incoming.Destroy(); probe.Destroy();
        At(drawerMiddle,x+5,y+6);
        Check(DrawerMembers(drawerController).SequenceEqual(new[]{drawer}),"A gap did not split drawer adjacency.");
        Check(Status(drawerPeer)=="NoCore","Drawer adjacency accidentally carried AE signals.");
        At(drawerMiddle,x+5,y+2);
        GeneralHelper.SetItemOwned(drawerMiddle,false);
        Check(DrawerMembers(drawerController).SequenceEqual(new[]{drawer}),"Unowned drawer bridged the adjacency graph.");
        GeneralHelper.SetItemOwned(drawerMiddle,true);
        Check(DrawerMembers(drawerController).Length==3,"Restored ownership did not restore adjacency.");
        var isolatedBox=Spawn("storage_bay_large"); At(isolatedBox,x+20,y);
        var localGrid=BayGrid(isolatedBox);
        var localController=PlaceBay(localGrid,Product+".drawer_controller",0,0);
        var localDrawer=PlaceBay(localGrid,Product+".drawer",2,0);
        var localStock=Spawn("toothpaste"); Add(localStock);
        Check(Status(localController)=="Absent" && (int)Adapter.Call(Adapter.Session,"DepositInterface",localController,localStock,1)! == 1
            && ReferenceEquals(localStock.parentInventory,DrawerData(localDrawer)),"Local controller in an unnetworked box required AE connectivity.");
        CheckDrawerOverview(localController,localStock,1);
        Adapter.Move(localStock,root);
        Adapter.Call(Adapter.Session,"RefreshControllerStock",localController);
        Check(!OverviewElements().Any(e=>e.GetType().Name=="StockIcon"),"Empty controller retained a stale item icon.");
        CheckDrawerOverview(drawerController,drawerStock.childItems.First(),DrawerUsed(drawer));
        StartCoroutine(Screenshot("drawer-controller-priority"));
        details="Direct controller deposit chose matching stock before empty drawers; +20 priority beat the disk. Gap and unowned bridge immediately split membership. Local controller catalog and priority panel opened.";
    }
    private PixelElement[] OverviewElements()
    {
        var window=(PixelWindow)Adapter.Field(Adapter.Session,"partWindow")!;
        return window.handler.GetComponentsInChildren<TreeNodeRender>(true).Select(n=>n.element).OfType<PixelElement>().Distinct().ToArray();
    }
    private void CheckDrawerOverview(GameItem controller,GameItem sample,long count)
    {
        Adapter.Call(Adapter.Session,"OpenPart",controller);
        var elements=OverviewElements();
        Check(Adapter.Field(Adapter.Session,"terminalGrid")==null && !elements.OfType<GameInventory>().Any(),"Overview exposed an interactive inventory.");
        Check(elements.OfType<MachineryText>().Any(t=>t.GetText()=="× "+count),"Missing stock count on the first controller panel.");
        Check(elements.Where(e=>e.GetType().Name=="StockIcon").OfType<TooltipListener>().Any(icon=>icon.HasTooltip()
            && icon.GetTooltip().nakedText.Contains(sample.name)),"Controller icon lost its native item tooltip.");
    }
    private void DrawerQuickActions()
    {
        Adapter.Call(Adapter.Session,"Close");
        var counted=drawerStock.childItems.First(); counted.SetAmount(2);
        Check(DrawerUsed(drawer)==4097,"Direct native quantity validation did not update the cache."); counted.SetAmount(1);
        drawerStock.LockInv();
        try { Check((int)Adapter.Static("DrawerQuickTransfer","Take",drawer,0)! == 0,"Quick withdrawal ignored the drawer remove lock."); }
        finally { drawerStock.UnlockInv(); }
        foreach(int mode in new[]{0,1,2})
        {
            var first=drawerStock.childItems.First(); long before=DrawerUsed(drawer);
            int moved=(int)Adapter.Static("DrawerQuickTransfer","Take",drawer,mode)!;
            Check(moved==1 && DrawerUsed(drawer)==before-1 && !ReferenceEquals(first.parentInventory,drawerStock),"Drawer quick transfer failed for destination "+mode);
            if(mode==0) Check(ReferenceEquals(first.parentInventory,root),"Quick inventory destination differs.");
            Adapter.Move(first,drawerStock);
        }
        Check(DrawerUsed(drawer)==4096,"Quick transfers lost or duplicated stock.");
        details="Three quick-transfer routes moved the actual top item to main inventory, showcase and storage through native slots, preserving all 4096 instances on return; no catalog was built for these gestures.";
    }
    private void DrawerPinsDisconnect()
    {
        Check(ItemPinHelper.IsPinned(core) && ItemPinHelper.IsPinned(terminal) && ItemPinHelper.IsPinned(drawerController),"Online AE parts did not automatically pin.");
        Check(!ItemPinHelper.IsPinned(drawerPeer),"Offline controller was automatically pinned.");
        ItemPinHelper.SetPinned(terminal,true); StowTopology(core);
        details="Online core, terminal and controller are pinned; offline controller is free. Added a manual terminal pin and removed the core for next-frame offline verification.";
    }
    private void DrawerPinsRestore()
    {
        Check(!ItemPinHelper.IsPinned(core) && !ItemPinHelper.IsPinned(drawerController),"Automatic pins survived going offline.");
        Check(ItemPinHelper.IsPinned(terminal),"Going offline erased a player's manual pin.");
        ItemPinHelper.SetPinned(terminal,false);
        Check(!ItemPinHelper.IsPinned(terminal),"Native manual pin cannot be cleared while offline.");
        // The second controller's local access must work without an AE core.
        var item=Spawn("toothpaste"); Add(item);
        Check((int)Adapter.Call(Adapter.Session,"DepositInterface",drawerPeer,item,1)! == 1,"Offline local drawer controller could not store.");
        Adapter.Move(item,root); item.Destroy();
        At(core,x,y);
        details="Offline transition removed only automatic pins, preserved and then cleared the player's manual terminal pin, and retained standalone controller access. Core restored.";
    }
    private void DrawerManualIo()
    {
        for(int gx=9;gx<=12;gx++) At(Spawn(Product+".pipeline"),x+gx,y);
        At(Spawn(Product+".pipeline"),x+12,y+1);
        var importCable=Spawn(Product+".pipeline"); At(importCable,x+12,y+2);
        var exportCable=Spawn(Product+".pipeline"); At(exportCable,x+12,y+3);
        drawerMachine=Spawn("storage_bay_large"); At(drawerMachine,x+13,y+2);
        var source=((List<GameInventory>)Adapter.Static("IoBus","TargetSlots",drawerMachine,true)!).OfType<GameGridInventory>().First();
        source.SetShape(20,20);
        drawerImport=Mount(importCable,"import_bus",1); drawerExport=Mount(exportCable,"export_bus",1);
        for(int i=0;i<80;i++) Put(source,"toilet_paper");
        long before=DrawerUsed(drawer); RunBus(drawerImport);
        Check(source.childItems.Count==0 && DrawerUsed(drawer)==before+80,"Manual import stopped at 8/64 or failed to move all 80.");
        var exportTime=Stopwatch.StartNew(); RunBus(drawerExport); exportTime.Stop();
        int exported=(int)Adapter.Static("PartStorage","Int",drawerExport,"io_moved",0)!;
        Check(exported>64 && source.childItems.Count>64,"Manual export retained the 8/64 limit.");
        var probe=Spawn("toilet_paper");
        Check((source.TryFindOneValidInventorySlot(probe)?.numTransfer ?? 0)==0,"Export stopped while a destination slot was available."); probe.Destroy();
        Check(exportTime.Elapsed.TotalSeconds<20,"Manual export repeatedly scanned a full target: "+exportTime.Elapsed.TotalSeconds+" seconds.");
        RunBus(drawerImport);
        Check(source.childItems.Count==0 && DrawerUsed(drawer)==before+80,"Import did not drain the filled target back to drawers.");
        details="Manual import moved all 80 independent objects; manual export exceeded 64 and filled every native slot in "+exportTime.Elapsed.TotalMilliseconds.ToString("F0")+" ms; second manual import drained the whole container. No arbitrary batch cap applied.";
    }
    private void DrawerManualPlanes()
    {
        Adapter.Call(Adapter.Session,"Close");
        drawerStock.LockInv();
        var end=DrawerData(drawerEnd); for(int i=0;i<80;i++) Put(end,"toilet_paper");
        Adapter.Static("DrawerStorage","ToggleLock",drawerEnd);
        // Keep ordinary drawer storage local; only the two connectors join AE.
        var local=Spawn(Product+".wireless_connector"); At(local,x+13,y);
        var remote=Spawn(Product+".wireless_connector"); Add(remote);
        var grid=((List<GameInventory>)Adapter.Static("IoBus","TargetSlots",drawerMachine,true)!).OfType<GameGridInventory>().First();
        GridAt(grid,remote,0,0); Pair(local,remote);
        var cable=Spawn(Product+".pipeline"); Add(cable); GridAt(grid,cable,2,0);
        var plane=Mount(cable,"formation_bus",2);
        Adapter.Call(Adapter.Session,"ConfigureNumber",plane,"plane_whole",1);
        RunBus(plane);
        int placed=(int)Adapter.Static("PartStorage","Int",plane,"io_moved",0)!;
        Check(placed>64,"Manual whole-space formation stopped at 8/64.");
        Adapter.Static("PartStorage","WithUpgradeRemoval",cable,(Action)(()=>Adapter.Move(plane,root))); plane.Destroy();
        var absorb=Mount(cable,"annihilation_bus",2);
        Adapter.Call(Adapter.Session,"ConfigureNumber",absorb,"plane_whole",1); RunBus(absorb);
        Check(grid.childItems.All(i=>i.identifier.StartsWith(Product+".",StringComparison.Ordinal)) && DrawerUsed(drawerEnd)==80,
            "Manual absorption stopped early or consumed an AE component: remaining="+string.Join(",",grid.childItems.Select(i=>i.identifier))+"; end="+DrawerUsed(drawerEnd)+"; middle="+DrawerUsed(drawerMiddle));
        drawerStock.UnlockInv();
        details="Manual whole-space formation moved more than 64 real objects and absorption recovered every movable object. Wireless connector and cable remained intact; source drawer locks were honored.";
    }
    private void DrawerManualFluids()
    {
        // Remove the remote plane before fluid fixtures are put into its space.
        var grid=((List<GameInventory>)Adapter.Static("IoBus","TargetSlots",drawerMachine,true)!).OfType<GameGridInventory>().First();
        var cable=grid.childItems.Single(i=>i.identifier==Product+".pipeline");
        foreach(var bus in ((GameInventory)Adapter.Static("PartStorage","Inventory",cable)!).childItems.ToArray())
        { Adapter.Static("PartStorage","WithUpgradeRemoval",cable,(Action)(()=>Adapter.Move(bus,root))); bus.Destroy(); }
        for(int gx=2;gx<=4;gx++) At(Spawn(Product+".pipeline"),x+gx,y+4);
        At(Spawn(Product+".pipeline"),x+2,y+3);
        drawerFluid=Spawn(Product+".fluid_drawer"); At(drawerFluid,x+5,y+4); WaterHelper.EmptyContainer(drawerFluid);
        var fluidCable=root.childItems.Single(i=>i.identifier==Product+".pipeline" && i.modifiedShape.minX==x+4 && i.modifiedShape.minY==y+4);
        var fluidStorage=Mount(fluidCable,"item_storage_bus",1); fluidStorage.ToggleSlotItem();
        var bottles=new List<GameItem>();
        for(int i=0;i<10;i++)
        {
            var bottle=Put(grid,"small_bottled_water"); WaterHelper.EmptyContainer(bottle); WaterHelper.AddLiquid(bottle,Liquid.LIQUID_WATER,1000); bottles.Add(bottle);
        }
        drawerImport.ToggleSlotItem(); drawerExport.ToggleSlotItem();
        long volume=bottles.Sum(b=>(long)WaterHelper.GetTotalVolume(b));
        Check(volume==10000,"Fluid fixture was not filled."); RunBus(drawerImport);
        Check(bottles.All(b=>WaterHelper.GetTotalVolume(b)==0) && WaterHelper.GetTotalVolume(drawerFluid)==volume,"Manual fluid import stopped at one sink or 8 transfers.");
        RunBus(drawerExport);
        Check(WaterHelper.GetTotalVolume(drawerFluid)==0 && bottles.Sum(b=>(long)WaterHelper.GetTotalVolume(b))==volume,"Manual fluid export did not exhaust available source volume.");
        CheckFluidDrawer(grid,bottles,fluidStorage);
        details="Native fluid drawer passed bus, interface, controller and terminal pours, exact partial fill, manual mixing, all five capacity tiers and downgrade protection. Both preserved native composition and total volume beyond eight transfers.";
    }
    private void CheckFluidDrawer(GameGridInventory grid,List<GameItem> bottles,GameItem storageBus)
    {
        const string water="water", other="physical_contaminant";
        int Part(GameItem vessel,string id)=>vessel.GetTagReadonly(Liquid.GetCurrentPartFromId(id)).GetInt();
        void Mix(GameItem vessel,int a,int b)
        {
            WaterHelper.EmptyContainer(vessel);
            WaterHelper.AddLiquid(vessel,water,a); WaterHelper.AddLiquid(vessel,other,b);
        }
        foreach(var bottle in bottles) WaterHelper.EmptyContainer(bottle);
        var source=bottles[0];
        var tank=drawerFluid;
        Check(WaterHelper.GetTotalCapacityML(tank)==1000 && WaterHelper.CanUseWaterContainer(tank)
            && tank.MaySelectSlotItem() && Cells(tank)==4,"Fluid drawer did not initialize as a native 1 L container.");
        Check(DrawerMembers(drawerController).Contains(tank),"The controller did not discover its adjacent fluid drawer.");
        Unmount(storageBus); // The controller is now the only route to this tank.
        var routes=(IDictionary)Adapter.Field(Adapter.Call(Adapter.Session,"Read",terminal)!,"LiquidPolicies")!;
        Check(routes.Contains(tank),"The controller did not expose the tank to the fluid network.");

        Adapter.Static("PartStorage","Number",drawerImport,"fluid_strict",0);
        Mix(source,1000,2000); RunBus(drawerImport);
        Mix(source,1001,2000); RunBus(drawerImport);
        Check(Part(tank,water)==1000 && Part(source,water)==1001,"A nearly equal ratio bypassed the tank on a non-strict bus.");
        Mix(source,2000,4000); RunBus(drawerImport);
        Check(Part(tank,water)==3000 && Part(tank,other)==6000 && WaterHelper.IsEmpty(source),"Equal ratios at different volumes did not merge.");

        var device=Spawn(Product+".item_interface"); At(device,x+2,y+5); device.ToggleSlotItem();
        Adapter.Static("PartStorage","Number",device,"fluid_strict",0);
        Mix(device,2000,4001); Adapter.Call(Adapter.Session,"PumpInterfaces");
        Check(Part(device,other)==4001 && Part(tank,other)==6000,"A non-strict fluid interface diluted the drawer.");
        Mix(device,1000,2000); Adapter.Call(Adapter.Session,"PumpInterfaces");
        Check(WaterHelper.IsEmpty(device) && Part(tank,water)==4000,"A matching interface mixture missed the drawer.");
        device.Destroy();

        Adapter.Call(Adapter.Session,"Open",terminal);
        var auth=new object?[]{source,tank,null,terminal,false};
        Check((bool)Adapter.Call(Adapter.Session,"BeginLiquidAction",auth)! && (bool)auth[4]!,"Terminal denied a writable tank.");
        try
        {
            Mix(source,5,11); WaterHelper.TransferLiquid(source,tank);
            Check(WaterHelper.GetTotalVolume(source)==16,"A remote terminal accepted a mismatched mixture.");
            Mix(source,5,10); WaterHelper.TransferLiquid(source,tank);
            Check(WaterHelper.IsEmpty(source),"Remote terminal rejected an exact mixture.");
        }
        finally { Adapter.Call(Adapter.Session,"EndLiquidAction",(object?)null); Adapter.Call(Adapter.Session,"Close"); }

        // 999,996 uL of 1:2 leaves four uL free: only three can enter intact.
        Mix(tank,333332,666664); Mix(source,2,4); RunBus(drawerImport);
        Check(Part(tank,water)==333333 && Part(tank,other)==666666 && Part(source,water)==1 && Part(source,other)==2,
            "A partial network pour rounded the ratio or lost the remainder.");
        RunBus(drawerImport);
        Check(WaterHelper.GetFreeCapacity(tank)==1 && WaterHelper.GetTotalVolume(source)==3,"A fractional ratio unit entered the drawer.");

        var cards=(GameGridInventory)Adapter.Static("FluidDrawer","Upgrades",tank)!;
        for(int i=1;i<=5;i++)
        {
            Put(cards,Product+".capacity_upgrade");
            Check(WaterHelper.GetTotalCapacityML(tank)==1000*(1<<(2*i)),"Wrong capacity at upgrade "+i);
        }
        var extra=Spawn(Product+".capacity_upgrade"); Add(extra);
        var wrong=Spawn(Product+".tag_upgrade"); Add(wrong);
        Check(cards.TryFindOneValidInventorySlot(extra)==null || cards.TryFindOneValidInventorySlot(extra).numTransfer==0,"Sixth capacity card was accepted.");
        var card=cards.childItems.Last(); Adapter.Move(card,root);
        Check(cards.TryFindOneValidInventorySlot(wrong)==null || cards.TryFindOneValidInventorySlot(wrong).numTransfer==0,"Tag card accepted as a capacity upgrade.");
        Check(WaterHelper.GetTotalCapacityML(tank)==256000,"Safe downgrade did not recompute capacity.");
        Adapter.Move(card,cards); extra.Destroy();
        Mix(tank,0,65000001); // Removing either card alone fits; removing both would overflow.
        var selection=(List<GameItemElement>)Adapter.Field(ItemMultiSelectHandler.current,"selectedItems")!;
        var previous=selection.ToArray();
        int width=root.inventoryShape.width, height=root.inventoryShape.height;
        root.SetShape(width+8,height);
        try
        {
            selection.Clear(); selection.Add((GameItemElement)card);
            Check((bool)Adapter.Call(ItemMultiSelectHandler.current,"IsFormationValid",root,width,0)!,"Legal single-card group transfer declined.");
            selection.Add((GameItemElement)cards.childItems.First(c=>c!=card));
            Check(!(bool)Adapter.Call(ItemMultiSelectHandler.current,"IsFormationValid",root,width,0)!,"Precise group placement bypassed the downgrade limit.");
        }
        finally { selection.Clear(); selection.AddRange(previous); root.SetShape(width,height); }
        Mix(tank,0,256000001);
        Check(card.MaxNumRemove()==0 && !GraphUtils.SwapItemsInPlace(card,wrong),"Overfilled downgrade or invalid swap was allowed.");
        wrong.Destroy();
        Mix(source,17,0); Check(source.MayTarget(tank) && source.CanTarget(tank),"Native pouring target declined the fluid drawer.");
        source.Target(tank);
        Check(Part(tank,water)==17 && Part(tank,other)==256000001,"Manual pouring into a different mixture failed.");
        Check(tank.MayTarget(source) && tank.CanTarget(source),"The fluid drawer cannot act as a native pouring source.");
        tank.Target(source);
        Check(!WaterHelper.IsEmpty(source),"Native manual withdrawal failed.");
        Mix(tank,120000000,240000000);
        var glass=tank.getItemSpriteModifierForItemFunc(tank).Single().sprite;
        Check(((GameItemElement)tank).handler.GetComponentsInChildren<Component>(false)
            .Any(image=>image.GetType().FullName=="UnityEngine.UI.Image" && Equals(Adapter.Property(image,"sprite"),glass)),"Native item renderer did not show its level after a volume change.");
        Adapter.Call(Adapter.Session,"OpenPart",tank);
        var window=(PixelWindow)Adapter.Field(Adapter.Session,"partWindow")!;
        Check(FindButton(window,ModHelper.GetLocalized(Product,"ui_fluid_drawer_pour"))!=null
            && ((GameItemElement)tank).GetTooltip().nakedText.Length>0 && tank.getItemSpriteModifierForItemFunc(tank).Single().sprite!=null,
            "Fluid drawer window, tooltip or liquid-level artwork is missing.");
        StartCoroutine(Screenshot("fluid-drawer"));
        Adapter.Call(Adapter.Session,"ClosePart");
    }
    private void DrawerSave()
    {
        drawerSaved=new[]{core,drawerController,drawerPeer,drawer,drawerMiddle,drawerEnd,terminal,drawerFluid}.Select(i=>i.GetUniqueID()).ToArray();
        drawerStockIds=drawerStock.childItems.Select(i=>i.GetUniqueID()).ToArray();
        drawerPrices=drawerStock.childItems.ToDictionary(i=>i.GetUniqueID(),i=>i.GetTagReadonly(PlayerStore.BOUGHT_PRICE_TAG).GetInt());
        PlayerStore.Instance.SaveGame();
        interfaceReloadWaiting=true; interfaceReloadReady=false; deadline=Time.realtimeSinceStartup+180;
        NewGameData.Instance.saveSlot=report.saveSlot; NewGameData.Instance.saveFile=null!; NewGameData.Instance.isLoadFromMainMenu=true;
        SceneManager.LoadScene("InventoryScene",LoadSceneMode.Single);
        details="Saved populated drawer instances, controller priority and all new devices to this fresh test slot; requested native reload.";
    }
    private void DrawerReload()
    {
        var all=MainItems(); foreach(int id in drawerSaved) Check(all.Count(i=>i.GetUniqueID()==id)==1,"Saved device missing or duplicated.");
        core=all.Single(i=>i.GetUniqueID()==drawerSaved[0]); drawerController=all.Single(i=>i.GetUniqueID()==drawerSaved[1]);
        drawerPeer=all.Single(i=>i.GetUniqueID()==drawerSaved[2]); drawer=all.Single(i=>i.GetUniqueID()==drawerSaved[3]);
        drawerFluid=all.Single(i=>i.GetUniqueID()==drawerSaved[7]);
        var upgrades=(GameInventory)Adapter.Static("FluidDrawer","Upgrades",drawerFluid)!;
        Check(upgrades.childItems.Count==5 && WaterHelper.GetTotalCapacityML(drawerFluid)==1024000
            && drawerFluid.GetTagReadonly(Liquid.GetCurrentPartFromId("water")).GetInt()==120000000
            && drawerFluid.GetTagReadonly(Liquid.GetCurrentPartFromId("physical_contaminant")).GetInt()==240000000
            && upgrades.childItems.All(card=>card.MaxNumRemove()==0),"Native reload lost tank volume, composition, capacity or downgrade protection.");
        terminal=all.Single(i=>i.GetUniqueID()==drawerSaved[6]); drawerStock=DrawerData(drawer);
        Check(drawerStock.childItems.Select(i=>i.GetUniqueID()).OrderBy(i=>i).SequenceEqual(drawerStockIds.OrderBy(i=>i)),"Reload lost or duplicated stored real instances.");
        foreach(var item in drawerStock.childItems) Check(item.GetTagReadonly(PlayerStore.BOUGHT_PRICE_TAG).GetInt()==drawerPrices[item.GetUniqueID()],"Native reload changed a stored instance's purchase price.");
        Check(DrawerUsed(drawer)==drawerStock.childItems.Sum(i=>(long)i.unitCount) && Priority(drawerController)==20 && DrawerMembers(drawerController).Length==4,"Reload lost cached quantity, priority or adjacency.");
        Check(Status(drawerController)=="Online" && Status(drawerPeer)=="NoCore","Reload changed the independent AE/adjacency graphs.");
        Adapter.Call(Adapter.Session,"OpenPart",drawer); StartCoroutine(Screenshot("drawer-large-reloaded"));
        report.saveReloadTested=true;
        details="Native reload retained every stored identity, exact cached count, +20 controller priority and fluid drawer capacity and 360 L of 1:2 mixture. Drawer-only peer stayed AE-offline; populated drawer UI reopened.";
    }
}
