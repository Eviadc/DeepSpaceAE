using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeepSpaceAE.RuntimeTests;

public sealed partial class RuntimePilot
{
    private GameItem usabilityDrive=null!,usabilityDisk=null!,usabilityDrawer=null!,usabilityCable=null!,usabilityBus=null!,usabilityPaper=null!,usabilityOther=null!;
    private Vector2 usabilityPosition;
    private int[] usabilityIds=Array.Empty<int>(),usabilityGiftIds=Array.Empty<int>();
    private PixelWindow UsabilityWindow=>(PixelWindow)Adapter.Field(Adapter.Session,"terminalWindow")!;
    private object UsabilityWindowState=>Adapter.Field(Adapter.Session,"terminalWindowState")!;
    private static void Press(MachineryButton button)=>NativeCall(button,"OnSelect");
    private static MachineryButton FindButton(PixelWindow window,string text)=>window.handler.GetComponentsInChildren<TreeNodeRender>(true)
        .Select(n=>n.element).OfType<MachineryButton>().Distinct().Single(b=>b.text==text);
    private int Priority(GameItem part)=>(int)Adapter.Static("PartStorage","Int",part,"priority",0)!;
    private object AcquisitionInstance()=>Adapter.Field(ModLoader.Mods.Single(m=>m.Manifest.ID==Product).Instance,"acquisition")!;

    private void UsabilitySetup()
    {
        if(ModLoader.Mods.Where(m=>m.IsEnabled).All(m=>m.Manifest.ID==Product || m.Manifest.ID==Plugin.Id))
            Check(!MainItems().Any(i=>i.identifier=="energy_credit"),"The isolated AE start still supplied a battery.");
        TopologySetup(); StartNoDuplicate();
        drive=Spawn(Product+".disk_drive"); At(drive,x+3,y);
        terminal=Spawn(Product+".terminal"); At(terminal,x+5,y);
        usabilityDrive=Spawn(Product+".disk_drive"); At(usabilityDrive,x+3,y+2);
        input=Spawn(Product+".item_interface"); At(input,x+5,y+2);
        small=Spawn(Product+".disk_small"); Add(small); Adapter.Move(small,Adapter.Slots("DiskStorage","Bays",drive)[0]);
        usabilityDisk=Spawn(Product+".disk_small"); Add(usabilityDisk); Adapter.Move(usabilityDisk,Adapter.Slots("DiskStorage","Bays",usabilityDrive)[0]);
        usabilityDrawer=Spawn(Product+".drawer"); At(usabilityDrawer,x+8,y+2);
        usabilityCable=Spawn(Product+".pipeline"); At(usabilityCable,x+8,y+4);
        foreach(var part in new[]{drive,usabilityDrive,input,terminal}) Check(Status(part)=="Online","Usability fixture is offline: "+part.identifier);
        var scenario=StartingScenario.Get(ExpectedScenario);
        Check(!scenario.GetDescription().Contains("battery") && !scenario.GetDescription().Contains("电池"),"New start still advertises a battery.");
        Check(scenario.GetStartWith().Any(s=>s.Contains("无线") || s.Contains("wireless")),"Native starting-supply list omits the wireless pair.");
        details="Fresh current engineer start delivered two wireless connectors, no AE battery, and a saved immutable grant plan; repeated start/upgrade callbacks gave no duplicate. Real two-drive network is online; new menu description and supply list were rendered.";
    }
    private void UsabilityWindowPosition()
    {
        Adapter.Call(Adapter.Session,"Open",terminal);
        Check(UsabilityWindow.rectTransform.localScale.x==1f,"Fresh tablet zoom is not 100%.");
        UsabilityWindow.rectTransform.anchoredPosition=new Vector2(31,-29); UsabilityWindow.FixBounds();
        usabilityPosition=UsabilityWindow.rectTransform.anchoredPosition;
        bool nativeCloseError=false;
        void ObserveClose(string message,string trace,LogType type)
        { if(message.Contains("CloseUI called with unknown identifier: deepspaceae.terminal")) nativeCloseError=true; }
        Application.logMessageReceived+=ObserveClose;
        try { Adapter.Call(Adapter.Session,"Close"); }
        finally { Application.logMessageReceived-=ObserveClose; }
        Check(!nativeCloseError,"Terminal close dispatched its custom ID to the native-only UI manager.");
        Adapter.Call(Adapter.Session,"Open",terminal);
        Check(Vector2.Distance(UsabilityWindow.rectTransform.anchoredPosition,usabilityPosition)<0.1f,"Closing and reopening recentered the terminal.");
        StartCoroutine(Screenshot("01-terminal-100"));
        details="The real terminal retained its moved native window position after close/reopen; its initial UI size is 100%.";
    }
    private void UsabilityZoom()
    {
        foreach(float scale in new[]{0.5f,0.75f,1f,0.5f})
        {
            Press((MachineryButton)Adapter.Field(UsabilityWindowState,"sizeButton")!);
            Check(Math.Abs(UsabilityWindow.rectTransform.localScale.x-scale)<0.001f,"Native tablet zoom cycle differs.");
            Check(((MachineryButton)Adapter.Field(UsabilityWindowState,"sizeButton")!).text==Mathf.RoundToInt(scale*100)+"%","Size button does not show the current size.");
        }
        UsabilityWindow.rectTransform.anchoredPosition=new Vector2(99999,-99999); UsabilityWindow.FixBounds();
        var bottomRight=UsabilityWindow.rectTransform.TransformPoint(new Vector3(UsabilityWindow.widthPixels,-UsabilityWindow.heightPixels,0));
        Check(bottomRight.x<=Screen.width+1 && bottomRight.y>=-1,"Zoomed terminal extends outside the screen after bounds correction.");
        UsabilityWindow.rectTransform.anchoredPosition=usabilityPosition; UsabilityWindow.FixBounds();
        Adapter.Call(Adapter.Session,"Close"); Adapter.Call(Adapter.Session,"Open",terminal);
        Check(UsabilityWindow.rectTransform.localScale.x==0.5f && Vector2.Distance(UsabilityWindow.rectTransform.anchoredPosition,usabilityPosition)<0.1f,"Window size/position did not survive recreation.");
        StartCoroutine(Screenshot("02-terminal-50"));
        details="Actual size-header callbacks cycled 100 -> 50 -> 75 -> 100; scaled screen-edge bounds and size/position retention after recreation passed.";
    }
    private void EditPriority(GameItem part,int increments)
    {
        Adapter.Call(Adapter.Session,"OpenPart",part);
        var button=(MachineryButton)Adapter.Field(Adapter.Session,"priorityButton")!;
        Check(button.text.Contains(Priority(part).ToString()),"Current priority is absent before opening the editor.");
        Press(button);
        var editor=(PixelWindow)Adapter.Field(Adapter.Session,"editorWindow")!;
        for(int i=0;i<Math.Abs(increments);i++) Press(FindButton(editor,increments>0?"+10":"-10"));
        Check(button.text.Contains(Priority(part).ToString()),"Priority label did not refresh while editing.");
        Adapter.Call(Adapter.Session,"CloseEditor");
    }
    private void UsabilityDrivePriority()
    {
        EditPriority(drive,1); EditPriority(usabilityDrive,2);
        var a=Spawn("toilet_paper"); Add(a);
        Check((int)Adapter.Call(Adapter.Session,"DepositInterface",input,a,a.unitCount)!>0 && ReferenceEquals(a.parentInventory,Adapter.Data(usabilityDisk)),"Higher-priority drive was not chosen for a real deposit.");
        EditPriority(usabilityDrive,-3);
        var b=Spawn("toothpaste"); Add(b);
        Check((int)Adapter.Call(Adapter.Session,"DepositInterface",input,b,b.unitCount)!>0 && ReferenceEquals(b.parentInventory,Adapter.Data(small)),"Changing drive priority did not change the next real route.");
        Adapter.Call(Adapter.Session,"OpenPart",drive);
        StartCoroutine(Screenshot("03-drive-priority"));
        details="Native drive editor and always-visible label tested at 10, 20 and -10. Real interface deposits switched between two real disk inventories when drive priority changed.";
    }
    private void UsabilityBusPriorities()
    {
        foreach(string kind in new[]{"item_storage_bus","import_bus","export_bus","formation_bus","annihilation_bus"})
        {
            var bus=Mount(usabilityCable,kind,kind=="item_storage_bus"?0:3);
            Adapter.Static("PartStorage","Number",bus,"io_enabled",0);
            EditPriority(bus,1); Check(Priority(bus)==10,"Bus priority control did not update: "+kind);
            if(kind=="item_storage_bus") usabilityBus=bus; else Unmount(bus);
        }
        Adapter.Call(Adapter.Session,"OpenPart",usabilityBus);
        StartCoroutine(Screenshot("04-bus-priority"));
        details="Storage, import, export, formation and annihilation buses all show their priority without opening the editor and update that visible label through real editor button callbacks.";
    }
    private void UsabilityDrawer()
    {
        var data=(GameInventory)Adapter.Static("DrawerStorage","Data",usabilityDrawer)!;
        usabilityPaper=Spawn("toilet_paper"); Add(usabilityPaper); Adapter.Move(usabilityPaper,data);
        usabilityOther=Spawn("toothpaste"); Add(usabilityOther);
        Adapter.Call(Adapter.Session,"OpenPart",usabilityDrawer);
        var panel=(PixelWindow)Adapter.Field(Adapter.Session,"partWindow")!;
        Press(FindButton(panel,ModHelper.GetLocalized(Product,"ui_drawer_lock")));
        Check((bool)Adapter.Static("DrawerStorage","IsLocked",usabilityDrawer)!,"Drawer lock button did not lock.");
        Check(data.TryFindOneValidInventorySlot(usabilityOther).numTransfer==0,"Locked drawer accepted another type.");
        Adapter.Move(usabilityPaper,root);
        Check(data.childItems.Count==0 && data.TryFindOneValidInventorySlot(usabilityOther).numTransfer==0
            && data.TryFindOneValidInventorySlot(usabilityPaper).numTransfer>0,"Empty locked drawer forgot its type.");
        Adapter.Static("DrawerStorage","ToggleLock",usabilityDrawer); Adapter.Move(usabilityOther,data);
        Adapter.Static("DrawerStorage","ToggleLock",usabilityDrawer); Adapter.Static("DrawerStorage","ToggleLock",usabilityDrawer);
        Check(data.TryFindOneValidInventorySlot(usabilityPaper).numTransfer==0
            && !(bool)Adapter.Call(data,"CanReplace",usabilityOther,usabilityPaper)!,"Unlocking a nonempty drawer allowed a type swap before emptying.");
        Adapter.Move(usabilityOther,root); Adapter.Move(usabilityPaper,data);
        Adapter.Static("DrawerStorage","ToggleLock",usabilityDrawer); Adapter.Move(usabilityPaper,root);
        Adapter.Call(Adapter.Session,"OpenPart",usabilityDrawer);
        Check((string)Adapter.Static("DrawerStorage","LockedId",usabilityDrawer)! == usabilityPaper.identifier,"Final empty-drawer lock identity differs.");
        StartCoroutine(Screenshot("05-empty-locked-drawer"));
        details="Actual lock button, insertion, withdrawal and swap paths passed: an empty locked drawer retains its type; unlocking while full still rejects other types; unlocking plus emptying permits a new type. Empty locked state is ready for save/reload.";
    }
    private void UsabilityUpgrade()
    {
        var keys=new[]{"engineer_start_v1","engineer_skip_intro_v1","gift_cursor","gift_inflight","gift_plan_v2"};
        var backup=keys.ToDictionary(k=>k,k=>ModHelper.GetModData(Product,k,null));
        var upgrade=ModHelper.GetNetworkUpgrade(Upgrade); int oldState=upgrade.state;
        var stash=Spawn("storage_bay_large"); Add(stash);
        var before=new HashSet<int>(MainItems().Select(i=>i.GetUniqueID()));
        try
        {
            foreach(var key in keys) ModHelper.RemoveModData(Product,key);
            upgrade.state=0; upgrade.Unlock();
            for(int attempt=0;attempt<5;attempt++)
            {
                foreach(var item in root.childItems.ToArray().Where(i=>!before.Contains(i.GetUniqueID())))
                {
                    var slot=stash.TryFindOneValidInventorySlot(item); if(slot!=null && slot.numTransfer>0) slot.TryAcceptOnce(item.unitCount);
                }
                Adapter.Call(AcquisitionInstance(),"Deliver");
            }
            var added=MainItems().Where(i=>!before.Contains(i.GetUniqueID())).ToArray();
            Check(added.Length==19 && added.Count(i=>i.identifier==Product+".wireless_connector")==2
                && !added.Any(i=>i.identifier=="energy_credit"),"Native upgrade did not deliver exactly the current battery-free kit and wireless pair.");
            usabilityGiftIds=added.Select(i=>i.GetUniqueID()).ToArray();
            upgrade.Unlock(); Adapter.Call(AcquisitionInstance(),"Deliver");
            Check(MainItems().Count(i=>!before.Contains(i.GetUniqueID()))==19,"Repeated upgrade duplicated the grant.");
            ModHelper.RemoveModData(Product,"gift_plan_v2"); ModHelper.SetModDataInt(Product,"gift_cursor",17);
            Adapter.Call(AcquisitionInstance(),"Deliver");
            Check(MainItems().Count(i=>!before.Contains(i.GetUniqueID()))==19
                && ModHelper.GetModData(Product,"gift_plan_v2","missing")=="","A completed legacy upgrade replayed after the loadout changed.");
        }
        finally
        {
            upgrade.state=oldState;
            foreach(var pair in backup) if(pair.Value==null) ModHelper.RemoveModData(Product,pair.Key); else ModHelper.SetModData(Product,pair.Key,pair.Value);
        }
        details="Native AE Engineer upgrade callback delivered 19 real items including exactly two wireless connectors and no battery, resumed partial placement, and did not duplicate on a repeated unlock or a completed legacy grant migration. Test entitlement state restored.";
    }
    private void UsabilityTrader()
    {
        var table=EmporiumEntry.Instance.frontInvinvElement;
        Check(table.childItems.Count==0,"Supplier fixture requires the fresh empty counter.");
        int width=table.inventoryShape.width,height=table.inventoryShape.height;
        var oldClient=PlayerStore.Instance.currentClientInstance;
        var keys=new[]{"steve_stock","steve_served_day","steve_appointment_day"};
        var backup=keys.ToDictionary(k=>k,k=>ModHelper.GetModData(Product,k,null));
        try
        {
            foreach(var test in new[]{(8,4,4),(6,4,2),(2,4,0),(8,3,2)})
            {
                table.SetShape(test.Item1,2);
                ModHelper.SetModData(Product,"steve_stock",string.Join("\n",Enumerable.Repeat(Product+".wireless_connector",test.Item2)));
                ModHelper.SetModDataInt(Product,"steve_served_day",-1);
                var client=(StoreClient)Adapter.Call(AcquisitionInstance(),"CreateSteve",StoreStation.GetDayCounter())!;
                PlayerStore.Instance.currentClientInstance=StoreClientInstance.CreateClientInstance(client);
                client.mainDialogue.ExecuteEndAction();
                Check(table.childItems.Count==test.Item3 && table.childItems.All(i=>i.identifier==Product+".wireless_connector" && !GeneralHelper.IsItemOwned(i)),"Supplier offered a partial wireless pair or wrong ownership.");
                foreach(var item in table.childItems.ToArray()) item.Destroy();
            }
        }
        finally
        {
            foreach(var item in table.childItems.ToArray()) item.Destroy();
            table.SetShape(width,height); PlayerStore.Instance.currentClientInstance=oldClient;
            foreach(var pair in backup) if(pair.Value==null) ModHelper.RemoveModData(Product,pair.Key); else ModHelper.SetModData(Product,pair.Key,pair.Value);
        }
        details="The villager supplier's real introduction callback offered 4 or 2 unowned connectors, and zero when only one fits. A cramped counter never retained half a pair; a pre-update odd appointment was normalized. Original counter dimensions and appointment state restored.";
    }
    private void UsabilitySave()
    {
        Adapter.Call(Adapter.Session,"Open",terminal);
        UsabilityWindow.rectTransform.anchoredPosition=new Vector2(42,-38); UsabilityWindow.FixBounds(); usabilityPosition=UsabilityWindow.rectTransform.anchoredPosition;
        usabilityIds=new[]{core,terminal,drive,small,usabilityDrive,usabilityDisk,input,usabilityDrawer,usabilityBus,usabilityPaper,usabilityOther}.Select(i=>i.GetUniqueID()).ToArray();
        PlayerStore.Instance.SaveGame();
        Check(Vector2.Distance(PlayerStore.Instance.contentWindowPositions[terminal.GetUniqueID()],usabilityPosition)<0.1f,"Saving an open moved terminal did not capture its latest position.");
        interfaceReloadWaiting=true; interfaceReloadReady=false; deadline=Time.realtimeSinceStartup+90;
        report.status="reloading_same_fresh_slot";
        NewGameData.Instance.saveSlot=report.saveSlot; NewGameData.Instance.saveFile=null!; NewGameData.Instance.isLoadFromMainMenu=true;
        SceneManager.LoadScene("InventoryScene",LoadSceneMode.Single);
        details="Saved the still-open terminal at its latest position plus 50% zoom, positive/negative drive priorities, visible bus priority, empty locked drawer and the delivered upgrade's real item identities; reloading only this fresh test slot.";
    }
    private void UsabilityReload()
    {
        var all=MainItems(); foreach(int id in usabilityIds.Concat(usabilityGiftIds)) Check(all.Count(i=>i.GetUniqueID()==id)==1,"Saved usability item missing/duplicated: "+id);
        terminal=all.Single(i=>i.GetUniqueID()==usabilityIds[1]); drive=all.Single(i=>i.GetUniqueID()==usabilityIds[2]);
        usabilityDrive=all.Single(i=>i.GetUniqueID()==usabilityIds[4]); usabilityDrawer=all.Single(i=>i.GetUniqueID()==usabilityIds[7]);
        usabilityBus=all.Single(i=>i.GetUniqueID()==usabilityIds[8]); usabilityPaper=all.Single(i=>i.GetUniqueID()==usabilityIds[9]); usabilityOther=all.Single(i=>i.GetUniqueID()==usabilityIds[10]);
        Check(Priority(drive)==10 && Priority(usabilityDrive)==-10 && Priority(usabilityBus)==10,"Priorities did not survive native save/reload.");
        var data=(GameInventory)Adapter.Static("DrawerStorage","Data",usabilityDrawer)!;
        Check((bool)Adapter.Static("DrawerStorage","IsLocked",usabilityDrawer)! && data.childItems.Count==0
            && data.TryFindOneValidInventorySlot(usabilityOther).numTransfer==0 && data.TryFindOneValidInventorySlot(usabilityPaper).numTransfer>0,"Empty drawer lock did not survive native save/reload.");
        Adapter.Call(Adapter.Session,"Open",terminal);
        Check(UsabilityWindow.rectTransform.localScale.x==0.5f && Vector2.Distance(UsabilityWindow.rectTransform.anchoredPosition,usabilityPosition)<0.1f,"Native save/reload lost terminal position or size.");
        Adapter.Call(Adapter.Session,"OpenPart",drive);
        Check(((MachineryButton)Adapter.Field(Adapter.Session,"priorityButton")!).text.Contains("10"),"Reloaded drive label hides priority.");
        Check(ModHelper.GetModDataInt(Product,"gift_cursor",-1)==34,"Completed starting grant replayed after reload.");
        StartCoroutine(Screenshot("06-reloaded-layout")); report.saveReloadTested=true;
        details="Native reload preserved all 30 fixture/upgrade identities, 50% terminal size and latest position, drive/bus priorities and the empty drawer's type lock; the completed starting grant did not replay.";
    }
}
