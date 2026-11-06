using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace DeepSpaceAE.RuntimeTests;

public sealed partial class RuntimePilot
{
    private const BindingFlags NativeFlags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private GameItem input=null!,fluid=null!,tankA=null!,tankB=null!,cargo=null!;
    private bool interfaceMenuOpened,interfaceReloadWaiting,interfaceReloadReady;
    private float interfaceMenuAt;
    private int[] interfaceIds=Array.Empty<int>();
    private int savedFluid,savedA,savedB,cargoId;
    private List<GameItem> MainItems()=>PlayerStore.Instance.FindAllItem(false).Distinct().ToList();
    private static object? NativeCall(object target,string method,params object?[] args)
    {
        var type=target is Type t?t:target.GetType();
        try { return type.GetMethods(NativeFlags).Single(m=>m.Name==method && m.GetParameters().Length==args.Length).Invoke(target is Type?null:target,args); }
        catch(TargetInvocationException e) { throw e.InnerException ?? e; }
    }
    private Sprite? NodeSprite(TreeNodeRender node)
    {
        var image=typeof(TreeNodeRender).GetProperty("image")!.GetValue(node)!;
        return (Sprite?)image.GetType().GetProperty("sprite")!.GetValue(image);
    }
    private IEnumerator Screenshot(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(config.outputDirectory,name+".png"));
    }
    private bool InterfaceMenuPreview()
    {
        if(!interfaceMenuOpened)
        {
            NewGameData.Instance.startType=NewGameData.StartType.MODDED; NewGameData.Instance.startScenarioId=ExpectedScenario;
            NewGameSpecializationUI.Open();
            var frame=typeof(NewGameSpecializationUI).GetField("frame",NativeFlags)!.GetValue(null);
            Check(frame!=null,"Native new-game specialization frame unavailable.");
            // Compare the actual menu's resolved sprite, rather than only the registry key.
            var images=((Component)frame!).GetComponentsInChildren<Component>(true).Where(i=>i.GetType().FullName=="UnityEngine.UI.Image");
            var expected=ModLoader.Mods.Single(m=>m.Manifest.ID==Product).Sprites["engineer-start"];
            report.nativeMenuIconVerified=images.Any(i=>ReferenceEquals(i.GetType().GetProperty("sprite")!.GetValue(i),expected));
            Check(report.nativeMenuIconVerified,"Native menu did not resolve the new engineer icon.");
            StartCoroutine(Screenshot("00-native-engineer-menu")); interfaceMenuOpened=true; interfaceMenuAt=Time.realtimeSinceStartup+1; return false;
        }
        if(Time.realtimeSinceStartup<interfaceMenuAt) return false;
        NewGameSpecializationUI.Close(); return true;
    }
    private void InterfaceStart()
    {
        var all=MainItems();
        foreach(var pair in new[]{("core",1),("pipeline",8),("item_storage_bus",2),("terminal",1),("disk_drive",1),("disk_small",2),("wrench",1),("tag_upgrade",1),("import_bus",2),("export_bus",2)})
            Check(all.Count(i=>i.identifier==Product+"."+pair.Item1)==pair.Item2,"Start quantity mismatch: "+pair.Item1);
        Check(all.Count(i=>i.identifier.StartsWith(Product+".",StringComparison.Ordinal))==21,"Expected exactly 21 start components, without interfaces.");
        Check(ModHelper.GetModDataInt(Product,"engineer_start_version",1)==2 && ModHelper.GetModDataInt(Product,"gift_cursor",-1)==32,"New v2 start did not finish.");
        Check(ModHelper.IsNetworkUpgradeUnlocked(Upgrade),"Wilde gift was not marked claimed.");
        core=Spawn(Product+".core"); drive=Spawn(Product+".disk_drive"); terminal=Spawn(Product+".terminal");
        small=Spawn(Product+".disk_small"); large=Spawn(Product+".disk_large");
        input=Spawn(Product+".item_interface"); fluid=Spawn(Product+".fluid_interface"); tankA=Spawn("water_tank"); tankB=Spawn("water_tank");
        foreach(var item in new[]{terminal,drive,input,fluid}) Check(item.shape.width==2 && item.shape.height==2,"2x2 shape mismatch: "+item.identifier);
        foreach(var disk in new[]{small,large}) Check(disk.shape.width==2 && disk.shape.height==1,"Disk is not horizontal 2x1.");
        var bays=Adapter.Slots("DiskStorage","Bays",drive);
        Check(bays.Length==6,"Drive needs six real bays.");
        foreach(var bay in bays)
        {
            var node=(TreeNodeRender)typeof(GameSlotInventory).GetField("backgroundImage",NativeFlags)!.GetValue(bay)!;
            var expected=ModLoader.Mods.Single(m=>m.Manifest.ID==Product).Sprites["disk-ghost"];
            Check(ReferenceEquals(NodeSprite(node),expected) && node.rectTransform.sizeDelta==new Vector2(32,16),
                "AE disk ghost is not bound to the real native bay: sprite="+NodeSprite(node)?.name+"; size="+node.rectTransform.sizeDelta);
        }
        Check(fluid.GetTagReadonly(WaterHelper.LIQUID_CONTAINER_CAPACITY).GetInt()==20000000,"Fluid interface buffer is not 20 litres.");
        details="Actual v2 start: tag card and two AE2-direction bus pairs; exact 21 AE items, no interfaces, 32/32 gifts. Native menu icon, 2x2 devices, horizontal disks, six AE ghost bays, 20L buffer.";
    }
    private void InterfaceNetwork()
    {
        // This fresh save includes other mods' starter items. Store movable
        // starting supplies in its real vanilla bay before laying out the fixture.
        var bay=MainItems().First(i=>i.identifier=="storage_bay");
        foreach(var item in root.childItems.ToArray())
        {
            if(ReferenceEquals(item,bay) || item.FindAllChildItems().Contains(bay)) continue;
            var storage=bay.TryFindOneValidInventorySlot(item);
            if(storage!=null && storage.numTransfer>0) storage.TryAcceptOnce(item.unitCount);
        }
        int tankBX=3+tankA.modifiedShape.globalWidth;
        int w=Math.Max(10,tankBX+tankB.modifiedShape.globalWidth),h=Math.Max(6,4+Math.Max(tankA.modifiedShape.globalHeight,tankB.modifiedShape.globalHeight));
        bool free=false;
        for(int gy=0;gy+h<=root.inventoryShape.height && !free;gy++) for(int gx=0;gx+w<=root.inventoryShape.width && !free;gx++)
        {
            bool occupied=root.childItems.Any(i=>i.modifiedShape.minX<gx+w && i.modifiedShape.maxX>=gx && i.modifiedShape.minY<gy+h && i.modifiedShape.maxY>=gy);
            if(!occupied) {x=gx;y=gy;free=true;}
        }
        Check(free,"No free native rectangle for the interface fixture ("+w+"x"+h+").");
        At(core,x,y); At(terminal,x+4,y+1); At(drive,x+6,y+1); At(input,x+8,y+1); At(fluid,x,y+4);
        At(tankA,x+3,y+4); At(tankB,x+tankBX,y+4);
        for(int dx=1;dx<=Math.Max(8,tankBX);dx++)
        {
            var wire=Spawn(Product+".pipeline"); At(wire,x+dx,y+3);
            if(dx==3 || dx==tankBX)
            {
                var bus=Spawn(Product+".item_storage_bus"); Add(bus);
                Adapter.Move(bus,(GameInventory)Adapter.Static("PartStorage","Inventory",wire)!);
                Adapter.Static("PartStorage","Number",bus,"cable_face",2);
                Adapter.Static("PartStorage","Number",bus,"priority",dx==3?20:10);
            }
        }
        Add(small); Add(large); var bays=Adapter.Slots("DiskStorage","Bays",drive); Adapter.Move(small,bays[0]); Adapter.Move(large,bays[1]);
        var power=MainItems().First(i=>i.identifier=="energy_credit"); Adapter.Move(power,(GameInventory)Adapter.Static("PartStorage","Inventory",core)!);
        Adapter.Call(Adapter.Property(Adapter.Session,"Visuals")!,"Invalidate");
        var board=Adapter.Board(); var evaluation=Adapter.Evaluation(board);
        foreach(var device in new[]{terminal,drive,input,fluid}) Check(Adapter.DeviceStatus(evaluation,Adapter.Id(device))=="Online","Fixture endpoint is offline: "+device.identifier);
        Check(((HashSet<string>)Adapter.Field(board,"Powered")!).Contains(Adapter.Id(core)),"Fixture network has no power.");
        details="Real native 2x2 interfaces/terminal, disks and two storage buses connect to one controller; no fake inventory or topology. Fixture rectangle="+w+"x"+h+".";
    }
    private void InterfaceItemInput()
    {
        // A real 3x3 item exceeds the interface's 2x2 footprint.
        cargo=Spawn(Product+".core"); bool placed=false;
        for(int gy=0;gy+3<=root.inventoryShape.height && !placed;gy++) for(int gx=0;gx+3<=root.inventoryShape.width && !placed;gx++)
        {
            if(root.childItems.Where(i=>i.identifier.StartsWith(Product+".",StringComparison.Ordinal)).Any(i=>i.modifiedShape.minX<gx+4 && i.modifiedShape.maxX>=gx-1 && i.modifiedShape.minY<gy+4 && i.modifiedShape.maxY>=gy-1)) continue;
            var shape=new GridShapeBuilder(cargo.modifiedShape).SetTransform(gx,gy,false,0).Build();
            var marker=root.TryInventorySlot(cargo,1,Vector2.negativeInfinity,shape,cargo.modifiedState);
            if(marker.numTransfer>0) { marker.TryAcceptOnce(1); placed=true; }
        }
        Check(placed,"No isolated native 3x3 source placement.");
        var inventory=(GameInventory)Adapter.Field(Adapter.Static("InterfaceStorage","Get",input)!,"Inventory")!;
        var slot=inventory.TryFindOneValidInventorySlot(cargo); Check(slot.numTransfer==1,"Large native item rejected by interface preview.");
        var handler=ItemMouseDragHandler.current;
        Adapter.SetField(handler,"currentItem",cargo); Adapter.SetField(handler,"lastMouseClickedPosition",((GameItemElement)cargo).handler.rectTransform.position);
        NativeCall(handler,"StartDrag"); Check(handler.IsDraggingItem,"Native drag handler did not start.");
        Adapter.SetField(handler,"lastInventory",null!); Adapter.SetField(handler,"lastItem",input); Adapter.SetField(handler,"lastSlot",slot);
        NativeCall(handler,"EndDrag");
        Check(ReferenceEquals(cargo.parentInventory,Adapter.Data(small)) || ReferenceEquals(cargo.parentInventory,Adapter.Data(large)),"Native drag did not route to a real network disk.");
        Check(inventory.childItems.Count==0 && cargo.unitCount==1,"Interface owned or duplicated cargo.");
        report.nativeDragHandlerTested=true; cargoId=cargo.GetUniqueID();
        details="Actual native StartDrag/EndDrag -> interface proxy -> native network slot: one real 3x3 controller stored in disk; interface remains empty. Hardware mouse was not driven.";
    }
    private void InterfaceTerminal()
    {
        Adapter.Call(Adapter.Session,"Open",terminal);
        var grid=(PixelElement)Adapter.Field(Adapter.Session,"terminalGrid")!;
        Check(terminal.child==null && (bool)Adapter.Property(Adapter.Session,"HasTagFilters")!,"Terminal still requires a physical tag card.");
        Check(((GameGridScrollableInventory)grid).GetVisibleHeight()==8,"Compact terminal viewport height changed.");
        var visuals=Adapter.Property(Adapter.Session,"Visuals")!;
        foreach(var device in new[]{core,terminal,drive,input,fluid})
            Check(((List<ItemSpriteModifier>)Adapter.Call(visuals,"DevicePorts",device)!).Count>0,"Device has no actual cable contact overlay: "+device.identifier);
        StartCoroutine(Screenshot("01-terminal-compact"));
        details="Live native terminal: 2x2 item, built-in tag filters without an upgrade inventory, compact viewport; devices retain real cable-port overlays. Screenshot requested.";
    }
    private void InterfaceStrict()
    {
        Adapter.Call(Adapter.Session,"Close");
        foreach(var item in new[]{fluid,tankA,tankB}) WaterHelper.EmptyContainer(item);
        NativeWater(tankA,500); NativeWater(tankB,700,true); NativeWater(fluid,200);
        Adapter.Call(Adapter.Session,"InterfaceSetting",fluid,"interface_mode",0); Adapter.Call(Adapter.Session,"InterfaceSetting",fluid,"interface_strict",1);
        Adapter.Call(Adapter.Session,"PumpInterfaces");
        Check(WaterHelper.GetTotalVolume(fluid)==0 && WaterHelper.GetTotalVolume(tankA)==700000 && WaterHelper.GetTotalVolume(tankB)==700000,"Strict input merged a different composition or failed to move.");
        Adapter.Call(Adapter.Session,"OpenPart",drive); StartCoroutine(Screenshot("02-horizontal-drive-bays"));
        details="Native exact-component input: 200ml pure water joins 500ml identical water; 700ml different-composition container is unchanged. Six horizontal real drive bays shown.";
    }
    private void InterfaceMixed()
    {
        foreach(var item in new[]{fluid,tankA,tankB}) WaterHelper.EmptyContainer(item);
        NativeWater(tankA,800,true); NativeWater(tankB,500); NativeWater(fluid,200);
        Adapter.Call(Adapter.Session,"InterfaceSetting",fluid,"interface_strict",0); Adapter.Call(Adapter.Session,"PumpInterfaces");
        Check(WaterHelper.GetTotalVolume(fluid)==0 && WaterHelper.GetTotalVolume(tankA)==1000000 && WaterHelper.GetTotalVolume(tankB)==500000,"Non-strict input did not use the first available priority route.");
        details="Strict disabled: native TransferLiquid mixes 200ml into the first eligible higher-priority 800ml container; second container remains 500ml.";
    }
    private List<Component> LiveDropdowns()=>((PixelWindow)Adapter.Field(Adapter.Session,"partWindow")!).handler.gameObject.GetComponentsInChildren<Component>(true)
        .Where(c=>c!=null && c.GetType().FullName=="TMPro.TMP_Dropdown").ToList();
    private static void SetDropdown(Component control,int value)=>control.GetType().GetProperty("value")!.SetValue(control,value);
    private void InterfaceOutput()
    {
        foreach(var item in new[]{fluid,tankA,tankB}) WaterHelper.EmptyContainer(item);
        NativeWater(tankA,500); NativeWater(tankB,700,true);
        Adapter.Call(Adapter.Session,"OpenPart",fluid); SetDropdown(LiveDropdowns()[0],1);
        Adapter.Call(Adapter.Session,"OpenPart",fluid); var controls=LiveDropdowns(); Check(controls.Count==2,"Native output selector missing.");
        SetDropdown(controls[1],1);
        Check((int)Adapter.Static("InterfaceStorage","Mode",fluid)! ==1 && ((string)Adapter.Static("InterfaceStorage","Output",fluid)!).Length>0,"Native UI callbacks did not save output selection.");
        details="Real TMP dropdown callbacks switch the interface to output and select one network composition; next case waits for event-driven native refill.";
    }
    private void InterfaceNativePour()
    {
        Check(WaterHelper.GetTotalVolume(fluid)==500000 && WaterHelper.GetTotalVolume(tankA)==0 && WaterHelper.GetTotalVolume(tankB)==700000,"Output failed to select the requested exact composition.");
        Adapter.Call(Adapter.Session,"OpenPart",fluid); StartCoroutine(Screenshot("03-native-fluid-interface"));
        ItemSelectHandler.current.EndSelect();
        NativeCall(ItemMouseDoubleClickHandler.current,"ClickAction",fluid,Vector2.zero,true,false);
        Check(ReferenceEquals(typeof(ItemSelectHandler).GetField("currentItem",NativeFlags)!.GetValue(ItemSelectHandler.current),fluid),"Native single-click cannot select the fluid interface.");
        var target=Spawn("water_tank"); Add(target);
        Check(fluid.MayTarget(target) && fluid.CanTarget(target),"Native container target rejected."); fluid.Target(target);
        Check(WaterHelper.GetTotalVolume(target)==500000 && WaterHelper.GetTotalVolume(fluid)==0,"Native interface pouring did not transfer real liquid.");
        ItemSelectHandler.current.EndSelect(); report.nativeLiquidClickTested=true;
        details="Event-driven output supplies only the chosen liquid. Actual native single-click selection and native Target pour 500ml into a real container; unmatched stock is untouched.";
    }
    private void InterfaceSave()
    {
        NativeWater(tankA,300); Adapter.Call(Adapter.Session,"PumpInterfaces");
        interfaceIds=new[]{core,terminal,drive,input,fluid,tankA,tankB,small,large}.Select(i=>i.GetUniqueID()).ToArray();
        savedFluid=WaterHelper.GetTotalVolume(fluid); savedA=WaterHelper.GetTotalVolume(tankA); savedB=WaterHelper.GetTotalVolume(tankB);
        Save(); interfaceReloadWaiting=true; interfaceReloadReady=false; deadline=Time.realtimeSinceStartup+90;
        report.status="reloading_same_fresh_slot";
        NewGameData.Instance.saveSlot=report.saveSlot; NewGameData.Instance.saveFile=null!; NewGameData.Instance.isLoadFromMainMenu=true;
        SceneManager.LoadScene("InventoryScene",LoadSceneMode.Single);
        details="Saved the newly created isolated slot, captured real IDs/volumes, and entered the game's normal InventoryScene load path for that same slot.";
    }
    private void InterfaceLoaded() { if(interfaceReloadWaiting) interfaceReloadReady=true; }
    private static void NativeWater(GameItem container,int ml,bool mixed=false)
    {
        WaterHelper.AddLiquid(container,Liquid.LIQUID_WATER,ml*(mixed?500:1000));
        if(mixed) WaterHelper.AddLiquid(container,Liquid.LIQUID_MINERAL,ml*500);
    }
    private void InterfaceReload()
    {
        var all=MainItems();
        foreach(int id in interfaceIds) Check(all.Count(i=>i.GetUniqueID()==id)==1,"Saved real instance missing/duplicated: "+id);
        fluid=all.Single(i=>i.GetUniqueID()==interfaceIds[4]); tankA=all.Single(i=>i.GetUniqueID()==interfaceIds[5]); tankB=all.Single(i=>i.GetUniqueID()==interfaceIds[6]);
        Check(all.Single(i=>i.GetUniqueID()==interfaceIds[1]).shape.width==2 && all.Single(i=>i.GetUniqueID()==interfaceIds[7]).shape.height==1,"Saved new geometry did not survive reload.");
        Check(WaterHelper.GetTotalVolume(fluid)==savedFluid && WaterHelper.GetTotalVolume(tankA)==savedA && WaterHelper.GetTotalVolume(tankB)==savedB,"Native liquid amounts changed across save/load.");
        Check((int)Adapter.Static("InterfaceStorage","Mode",fluid)! ==1 && ((string)Adapter.Static("InterfaceStorage","Output",fluid)!).Length>0,"Interface settings were not saved.");
        Check(all.Count(i=>i.GetUniqueID()==cargoId)==1,"Routed disk cargo was not saved.");
        Check(ModHelper.GetModDataInt(Product,"gift_cursor",-1)==32,"Start gift cursor changed after reload.");
        report.saveReloadTested=true;
        details="Actual native save/reload of the same fresh slot: IDs, disk cargo, horizontal disk/2x2 terminal, exact liquid volumes, output mode/filter and v2 gift cursor survive without duplication.";
    }
}
