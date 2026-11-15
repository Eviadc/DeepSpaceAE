using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;
using HarmonyLib;
namespace DeepSpaceAE.GameAdapter;

[HarmonyPatch(typeof(HopperHelper),nameof(HopperHelper.RunWindow))]
internal static class AutomationWindowPatch
{
    [HarmonyPostfix]
    private static void Postfix(int __0)=>ModEntry.ActiveSession?.RunAutomation(__0);
}
internal sealed partial class GameSession
{
    private int automationGeneration;
    internal bool NightAutomation { get; private set; }
    internal void AutomationStructureChanged() { automationGeneration++; QueueActivity(); }
    internal void RunAutomation(int window,GameItem? only=null)
    {
        if(Loading || writing || MainInventory()==null) return;
        if(ItemMouseDragHandler.current?.IsDraggingItem==true || ItemMultiSelectHandler.current?.IsGroupDragging==true) return;
        writing=true; NightAutomation=window!=0; int operations=0;
        int busLimit=only==null?8:int.MaxValue, batchLimit=only==null?64:int.MaxValue;
        var moved=new HashSet<GameItem>();
        try
        {
            using var placement=new AutomationPlacement();
            if(MapUIManager.Instance!=null && MapUIManager.Instance.IsOutside) return;
            var board=ReadBoard(); int generation=automationGeneration;
            var views=new Dictionary<string,View>(StringComparer.Ordinal);
            var buses=board.Layout.Parts.Where(IoBus.IsAutomation).Where(b=>only==null?
                PartStorage.Int(b,"io_enabled",1)!=0 && HopperHelper.WindowOf(IoBus.Order(b))==window:ReferenceEquals(b,only))
                .OrderBy(IoBus.Order).ThenByDescending(b=>PartStorage.Policy(b).Priority).ThenBy(b=>b.GetUniqueID()).ToArray();
            foreach(var bus in buses)
            {
                if(operations>=batchLimit || generation!=automationGeneration) break;
                if(!board.Evaluation.Devices.TryGetValue(Id(bus),out var connection) || connection.Status!=DeviceStatus.Online
                    || connection.CoreId==null || !board.Powered.Contains(connection.CoreId)) { Result(bus,0,"ui_io_offline"); continue; }
                if(!views.TryGetValue(connection.CoreId,out var view)) views.Add(connection.CoreId,view=ReadStorage(board,connection.CoreId));
                if(IoBus.IsPlane(bus))
                { RunPlane(bus,board,view,moved,ref operations,busLimit,batchLimit); continue; }
                var machine=board.Layout.FrontOwner(bus);
                if(machine==null || IsPart(machine) || !GeneralHelper.IsItemEffectivelyOwned(machine)) { Result(bus,0,"ui_io_no_slots"); continue; }
                bool import=bus.identifier==IoBus.Import;
                if(PartModes.IsFluid(bus))
                { RunFluidBus(bus,machine,view,moved,ref operations,busLimit,batchLimit); continue; }
                var slots=IoBus.TargetSlots(machine,import);
                if(slots.Count==0) { Result(bus,0,"ui_io_no_slots"); continue; }
                var candidates=import?slots.SelectMany(s=>s.childItems).ToArray():view.Search.Find("")
                    .Select(r=>view.Live[r.Instance]).Where(i=>!ReferenceEquals(i.parentInventory?.GetParentItem(),machine)).ToArray();
                int units=0,attempts=0;
                foreach(var item in candidates)
                {
                    if(attempts>=busLimit || operations>=batchLimit) break;
                    if(generation!=automationGeneration)
                    {
                        if(only==null) break;
                        board=ReadBoard(); generation=automationGeneration;
                        if(!board.Evaluation.Devices.TryGetValue(Id(bus),out var fresh) || fresh.Status!=DeviceStatus.Online
                            || fresh.CoreId==null || !board.Powered.Contains(fresh.CoreId) || !ReferenceEquals(board.Layout.FrontOwner(bus),machine)) break;
                        view=ReadStorage(board,fresh.CoreId); slots=IoBus.TargetSlots(machine,import);
                    }
                    var source=item.parentInventory; int remaining=item.unitCount;
                    if(source==null || moved.Contains(item) || IsPart(item) || HopperHelper.IsHopper(item) || item.unitCount<=0
                        || !GeneralHelper.IsItemEffectivelyOwned(item) || !item.MayRemove() || item.MaxNumRemove()<=0
                        || source.IsRemoveLocked() || source.IsTag(HopperHelper.NO_HOPPER_INVENTORY_TAG)
                        || !IoBus.Matches(bus,item)) continue;
                    if(!import && !NetworkExtracts(view,item,source,machine)) continue;
                    if(import && !ReferenceEquals(source.GetParentItem(),machine)) continue;
                    var destinations=import?NetworkDestinations(view,item,machine):slots;
                    foreach(var destination in destinations)
                    {
                        if(!import && !ReferenceEquals(destination.GetParentItem(),machine)) continue;
                        if(ReferenceEquals(destination,source) || destination.IsInsertLocked() || destination.IsTag(HopperHelper.NO_HOPPER_INVENTORY_TAG)
                            || GraphUtils.FindParentEquals(destination,item)) continue;
                        while(ReferenceEquals(item.parentInventory,source) && item.unitCount>0 && remaining>0 && attempts<busLimit && operations<batchLimit)
                        {
                            if(generation!=automationGeneration) break;
                            var slot=AutomationPlacement.Find(destination,item);
                            if(slot==null || slot.numTransfer<=0) break;
                            int amount=Math.Min(remaining,Math.Min(item.unitCount,Math.Min(slot.numTransfer,item.MaxNumRemove())));
                            if(amount<=0) break;
                            attempts++; operations++;
                            int accepted=slot.TryAcceptOnce(amount);
                            if(accepted<=0) break;
                            units+=accepted; remaining-=accepted;
                            if(slot.item!=null && !ReferenceEquals(slot.item,item)) moved.Add(slot.item);
                            if(slot.targetItem!=null) moved.Add(slot.targetItem);
                        }
                        if(!ReferenceEquals(item.parentInventory,source)) { moved.Add(item); break; }
                    }
                }
                Result(bus,units,units>0?"ui_io_moved":"ui_io_idle");
            }
        }
        catch(Exception e) { log.Error("AE automation stopped: "+Diagnostics.ConsoleText(e.ToString())); if(only!=null) Result(only,0,"ui_io_error"); }
        finally
        {
            NightAutomation=false; writing=false;
            terminalDirty=openTerminal!=null; ScheduleTerminalRefresh(); RefreshPartLater(); Visuals.Invalidate();
        }
    }
    private static void Result(GameItem bus,int moved,string status)
    { PartStorage.Number(bus,"io_moved",moved); PartStorage.String(bus,"io_status",status); }
    private bool NetworkExtracts(View view,GameItem item,GameInventory source,GameItem machine)
    {
        foreach(var pair in view.Owners)
        {
            if(ReferenceEquals(pair.Value,machine) || !GeneralHelper.IsItemEffectivelyOwned(pair.Value)) continue;
            var policy=IoBus.IsBus(pair.Key)?PartStorage.Policy(pair.Key):view.Policies[pair.Key];
            if(view.Inventories[pair.Value].Contains(source) && policy.CanExtract(item.identifier,PartStorage.Tags(item))) return true;
        }
        return false;
    }
    private List<GameInventory> NetworkDestinations(View view,GameItem item,GameItem machine)
    {
        var devices=view.Policies.Keys.ToDictionary(Id,StringComparer.Ordinal);
        var policies=devices.ToDictionary(p=>p.Key,p=>IoBus.IsBus(p.Value)?PartStorage.Policy(p.Value):view.Policies[p.Value],StringComparer.Ordinal);
        var result=new List<GameInventory>(); var seen=new HashSet<GameInventory>();
        foreach(var id in StorageRouting.InsertionOrder(policies,item.identifier,PartStorage.Tags(item)))
        {
            var device=devices[id]; var owner=view.Owners[device];
            if(ReferenceEquals(owner,machine) || !GeneralHelper.IsItemEffectivelyOwned(owner)) continue;
            foreach(var inv in view.Inventories[owner]) if(seen.Add(inv)) result.Add(inv);
        }
        return result;
    }
}
