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
    internal void AutomationStructureChanged()=>automationGeneration++;
    internal void RunAutomation(int window,GameItem? only=null)
    {
        if(Loading || writing || writeFault || MainInventory()==null) return;
        if(ItemMouseDragHandler.current?.IsDraggingItem==true || ItemMultiSelectHandler.current?.IsGroupDragging==true) return;
        writing=true; NightAutomation=window!=0; int operations=0;
        var moved=new HashSet<GameItem>();
        try
        {
            if(MapUIManager.Instance!=null && MapUIManager.Instance.IsOutside) return;
            var board=ReadBoard(); int generation=automationGeneration;
            var views=new Dictionary<string,View>(StringComparer.Ordinal);
            var buses=board.Layout.Parts.Where(IoBus.IsIo).Where(b=>only==null?
                PartStorage.Int(b,"io_enabled",1)!=0 && HopperHelper.WindowOf(IoBus.Order(b))==window:ReferenceEquals(b,only))
                .OrderBy(IoBus.Order).ThenByDescending(b=>PartStorage.Policy(b).Priority).ThenBy(b=>b.GetUniqueID()).ToArray();
            foreach(var bus in buses)
            {
                if(operations>=64 || generation!=automationGeneration || writeFault) break;
                if(!board.Evaluation.Devices.TryGetValue(Id(bus),out var connection) || connection.Status!=DeviceStatus.Online
                    || connection.CoreId==null || !board.Powered.Contains(connection.CoreId)) { Result(bus,0,"ui_io_offline"); continue; }
                var machine=board.Layout.FrontOwner(bus);
                if(machine==null || IsPart(machine) || !GeneralHelper.IsItemEffectivelyOwned(machine)) { Result(bus,0,"ui_io_no_slots"); continue; }
                bool import=bus.identifier==IoBus.Import;
                var slots=IoBus.MachineSlots(machine,import);
                if(slots.Count==0) { Result(bus,0,"ui_io_no_slots"); continue; }
                if(!views.TryGetValue(connection.CoreId,out var view)) views.Add(connection.CoreId,view=ReadStorage(board,connection.CoreId));
                var candidates=import?slots.SelectMany(s=>s.childItems).ToArray():view.Search.Find("")
                    .Select(r=>view.Live[r.Instance]).Where(i=>!ReferenceEquals(i.parentInventory?.GetParentItem(),machine)).ToArray();
                int units=0,attempts=0;
                foreach(var item in candidates)
                {
                    if(attempts>=8 || operations>=64 || generation!=automationGeneration || writeFault) break;
                    var source=item.parentInventory;
                    if(source==null || moved.Contains(item) || IsPart(item) || HopperHelper.IsHopper(item) || item.unitCount<=0
                        || !GeneralHelper.IsItemEffectivelyOwned(item) || !item.MayRemove() || item.MaxNumRemove()<=0
                        || source.IsRemoveLocked() || source.IsTag(HopperHelper.NO_HOPPER_INVENTORY_TAG)
                        || !PartStorage.Policy(bus).Matches(item.identifier,PartStorage.Tags(item))) continue;
                    if(!import && !NetworkExtracts(view,item,source,machine)) continue;
                    if(import && !ReferenceEquals(source.GetParentItem(),machine)) continue;
                    if(!PartStorage.Powered(view.Core,board.Evaluation.NightCosts[connection.CoreId])) break;
                    var destinations=import?NetworkDestinations(view,item,machine):slots;
                    foreach(var destination in destinations)
                    {
                        if(!import && !ReferenceEquals(destination.GetParentItem(),machine)) continue;
                        if(ReferenceEquals(destination,source) || destination.IsInsertLocked() || destination.IsTag(HopperHelper.NO_HOPPER_INVENTORY_TAG)
                            || GraphUtils.FindParentEquals(destination,item)) continue;
                        var slot=destination.TryFindOneValidInventorySlot(item);
                        if(slot==null || !ReferenceEquals(slot.item,item) || !ReferenceEquals(slot.inventory,destination)
                            || slot.numTransfer<=0 || (slot.targetItem==null && slot.itemGridShape==null)) continue;
                        int amount=Math.Min(item.unitCount,Math.Min(slot.numTransfer,item.MaxNumRemove()));
                        if(amount<=0) continue;
                        slot=NativeTransfer.Revalidate(item,source,slot,amount);
                        if(slot==null || generation!=automationGeneration) continue;
                        attempts++; operations++;
                        units+=AutomationMove(item,source,slot,amount);
                        moved.Add(item); if(slot.item!=null) moved.Add(slot.item); if(slot.targetItem!=null) moved.Add(slot.targetItem);
                        break;
                    }
                }
                Result(bus,units,units>0?"ui_io_moved":"ui_io_idle");
            }
        }
        catch(Exception e) { log.Error("AE automation stopped: "+Diagnostics.ConsoleText(e.ToString())); if(only!=null) Result(only,0,writeFault?"ui_transfer_locked":"ui_io_error"); }
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
        if(!source.childItems.Contains(item)) return false;
        foreach(var pair in view.Owners)
        {
            if(ReferenceEquals(pair.Value,machine) || !GeneralHelper.IsItemEffectivelyOwned(pair.Value)) continue;
            var policy=DiskStorage.IsDrive(pair.Key)?view.Policies[pair.Key]:PartStorage.Policy(pair.Key);
            if(view.Inventories[pair.Value].Contains(source) && policy.CanExtract(item.identifier,PartStorage.Tags(item))) return true;
        }
        return false;
    }
    private List<GameInventory> NetworkDestinations(View view,GameItem item,GameItem machine)
    {
        var devices=view.Policies.Keys.ToDictionary(Id,StringComparer.Ordinal);
        var policies=devices.ToDictionary(p=>p.Key,p=>DiskStorage.IsDrive(p.Value)?view.Policies[p.Value]:PartStorage.Policy(p.Value),StringComparer.Ordinal);
        var result=new List<GameInventory>(); var seen=new HashSet<GameInventory>();
        foreach(var id in StorageRouting.InsertionOrder(policies,item.identifier,PartStorage.Tags(item)))
        {
            var device=devices[id]; var owner=view.Owners[device];
            if(ReferenceEquals(owner,machine) || !GeneralHelper.IsItemEffectivelyOwned(owner)) continue;
            foreach(var inv in view.Inventories[owner]) if(seen.Add(inv)) result.Add(inv);
        }
        return result;
    }
    private int AutomationMove(GameItem item,GameInventory source,SlotMarker slot,int amount)
    {
        int before=item.unitCount;
        int destinationBefore=slot.targetItem?.unitCount ?? 0;
        if(!ReferenceEquals(item.parentInventory,source) || !source.childItems.Contains(item) || item.MaxNumRemove()<amount)
            throw new InvalidOperationException("Automation source changed before native transfer.");
        try
        {
            int accepted=slot.TryAcceptOnce(amount);
            bool destroyed=item is GameItemElement e && e.IsDestroyed();
            var received=slot.targetItem ?? slot.item;
            if(accepted!=amount || !AutomationRules.SourceRemainder(before,accepted,item.unitCount,
                ReferenceEquals(item.parentInventory,source) && source.childItems.Contains(item),ReferenceEquals(item.parentInventory,slot.inventory),destroyed)
                || received==null || !AutomationRules.DestinationReceipt(accepted,destinationBefore,received.unitCount,
                    ReferenceEquals(received.parentInventory,slot.inventory) && slot.inventory.childItems.Contains(received)))
                throw new InvalidOperationException("Native hopper transfer outcome unknown. No retry or compensation.");
            return accepted;
        }
        catch
        {
            writeFault=true;
            foreach(var orphan in new[]{item,slot.item}) if(orphan!=null && orphan.parentInventory==null && !(orphan is GameItemElement e && e.IsDestroyed())) uncertainItems.Add(orphan);
            throw;
        }
    }
}
