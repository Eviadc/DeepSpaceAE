using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;
using HarmonyLib;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

internal static partial class PartStorage
{
    private static readonly HashSet<GameItem> gridDebits=new();
    internal static void ResetPowerSettlement() { gridDebits.Clear(); reservationCores.Clear(); }
    internal static bool IsGrid(GameItem source)=>CurrentConverterHelper.IsCurrentConverter(source);
    internal static bool GridUnpaid(GameItem core)=>Int(core,"grid_unpaid")!=0;
    internal static bool CanSupply(GameItem source,int cost)=>source.unitCount==1 && GeneralHelper.IsItemEffectivelyOwned(source)
        && (IsGrid(source)?CurrentConverterHelper.IsOn(source) && !CurrentConverterHelper.IsBlackedOut()
            :source.IsTag(PowerHelper.POWER_SOURCE_ITEM_TAG) && PowerHelper.CanDrawPowerSource(source,cost));
    internal static bool CanRemovePower(GameItem core)=>!ModSettings.EnergyEnabled || (UsesPool(core)?Battery(core)==null || CanRemoveSource(Battery(core)!):!GridUnpaid(core) && Lease(core).CanRemove(Day));
    internal static bool CanInstall(GameItem core,GameItem source)
    {
        if(!GeneralHelper.IsItemEffectivelyOwned(core) || !CanRemovePower(core)) return false;
        if(!ModSettings.EnergyEnabled || !ReferenceEquals(core.parentInventory,GameSession.MainInventory()))
            return IsPowerItem(source) && CanRemoveSource(source);
        int cost=ModEntry.ActiveSession?.CoreCost(core) ?? 2;
        if(UsesPool(core)) return IsPowerItem(source) && CanRemoveSource(source);
        var lease=Lease(core); // trial does not mutate the saved reservation
        return lease.Reserve(Day,Math.Max(2,cost),source.GetUniqueID(),n=>CanSupply(source,n));
    }
    internal static GameItem? Battery(GameItem core)=>Inventory(core).childItems.SingleOrDefault();
    internal static NightReservation Lease(GameItem core)=>new(Int(core,"power_day",-1),Int(core,"power_budget"),Int(core,"power_battery"),(NightPhase)Int(core,"power_phase"));
    private static void Save(GameItem core,NightReservation lease)
    {
        Number(core,"power_day",lease.Day); Number(core,"power_budget",lease.Required);
        Number(core,"power_battery",checked((int)lease.Battery)); Number(core,"power_phase",(int)lease.Phase);
    }
    private static string Stamp(NightReservation lease)=>$"{lease.Day}/{lease.Required}/{lease.Battery}/{lease.Phase}";
    private static void Usage(GameItem core,int amount,bool skipValidate=true)
    {
        if(!core.state.dict.TryGetValue("machinery_drawn_power",out var value) || value.GetInt()!=amount)
            core.ModifyTag("machinery_drawn_power",t=>t.Enable().SetInt(amount),skipValidate);
    }
    internal static void SuspendPower(GameItem item)
    {
        // Discard only known, uncharged reservations. Unknown/paid journals remain
        // intact so opting back in cannot replay an uncertain debit.
        int phase=Int(item,"supply_phase");
        if(Int(item,"supply_share")!=0 && (phase==(int)NightPhase.Empty || phase==(int)NightPhase.Reserved || phase==(int)NightPhase.Shortfall)) ClearToken(item);
        if(item.identifier!="deepspaceae.core" && !EnergyReceiver.IsReceiver(item)) return;
        Usage(item,0,false);
        if(item.identifier=="deepspaceae.core")
        {
            var lease=Lease(item);
            if(lease.Phase==NightPhase.Reserved || lease.Phase==NightPhase.Shortfall)
            {
                Save(item,new NightReservation(Day,0,0,NightPhase.Empty));
                String(item,"pool_plan",""); String(item,"pool_rejected","");
            }
        }
        RestoreLocks(item);
    }
    internal static bool Powered(GameItem core,int cost,bool reserve=false,PowerSnapshot? power=null)
    {
        if(ModEntry.ActiveSession?.Loading!=false) return false;
        if(!ModSettings.EnergyEnabled) return true;
        if(UsesPool(core)) return PoolPowered(core,cost,power);
        if(ModEntry.ActiveSession.NightAutomation)
        {
            var paid=Lease(core); var battery=Battery(core);
            return battery!=null && battery.unitCount==1 && GeneralHelper.IsItemEffectivelyOwned(battery) && !GridUnpaid(core)
                && (!IsGrid(battery) && !HypertechBatteryHelper.IsHypertechBattery(battery) || CanSupply(battery,paid.Required))
                && AutomationRules.PaidNightPower(Day,paid.Day,paid.Phase==NightPhase.Settled,paid.Battery,battery.GetUniqueID(),paid.Required,cost);
        }
        var lease=Lease(core); string before=Stamp(lease); lease.ObservePeak(Day,Math.Max(2,cost));
        var source=Battery(core); bool powered=false;
        if(source!=null && !GridUnpaid(core))
        {
            // A switched-off grid source stays locked and retains its accrued bill.
            if(!IsGrid(source) || CanSupply(source,lease.Required))
                powered=lease.Reserve(Day,Math.Max(2,cost),source.GetUniqueID(),n=>CanSupply(source,n));
        }
        if(before!=Stamp(lease)) { Save(core,lease); ModEntry.ActiveSession?.NativeStorageChanged(core); }
        if(source!=null && IsGrid(source)) Usage(core,lease.Required);
        RestoreLocks(core); return powered;
    }
    internal static void RestoreLocks(GameItem core)
    { if(!ModSettings.EnergyEnabled) { foreach(var slot in EnergyReceiver.Slots(core)) slot.UnlockInv(); }
      else if(EnergyReceiver.IsReceiver(core) || UsesPool(core)) PoolLocks(core); else if(CanRemovePower(core)) Inventory(core).UnlockInv(); else Inventory(core).LockInv(); }
    internal static void Settle(GameItem core,int cost,ModLog log,PowerSnapshot? power=null)
    {
        if(!ModSettings.EnergyEnabled) return;
        if(UsesPool(core)) { PoolSettle(core,cost,log,power); return; }
        var source=Battery(core); if(source==null || !GeneralHelper.IsItemEffectivelyOwned(core) || !GeneralHelper.IsItemEffectivelyOwned(source)) return;
        var lease=Lease(core);
        if(!lease.CanSettle(GridUnpaid(core))) return;
        if(IsGrid(source))
        {
            // Native service accounting performs the actual cash deduction later.
            // Do not call Supply here: isNightCycle is still false at this hook.
            // A saved in-progress debit without an observed unpaid outcome is
            // unknown. Never replay it merely because another night begins.
            if(lease.Phase==NightPhase.DebitStarted && !GridUnpaid(core)) return;
            if(lease.Phase!=NightPhase.DebitStarted)
            {
                int gridRequired=lease.SettlementBudget(Day,cost);
                if(!lease.Reserve(lease.Day,gridRequired,source.GetUniqueID(),_=>true)) { Save(core,lease); return; }
                if(!lease.BeginDebit(Day,source.GetUniqueID())) return;
            }
            gridDebits.Add(core);
            Save(core,lease); Usage(core,lease.Required); Number(core,"grid_pending_day",Day); Inventory(core).LockInv();
            var service=StoreService.GetCurrentConverterService(); if(service!=null) CurrentConverterHelper.UpdateServiceCost(service);
            return;
        }
        if(lease.Phase==NightPhase.Settled && lease.Day>=Day || lease.Phase==NightPhase.Fault || lease.Phase==NightPhase.DebitStarted) return;
        int required=lease.SettlementBudget(Day,cost);
        if(!lease.Reserve(lease.Day,required,source.GetUniqueID(),n=>CanSupply(source,n))) { Save(core,lease); RestoreLocks(core); return; }
        if(!lease.BeginDebit(Day,source.GetUniqueID())) return;
        Save(core,lease); Inventory(core).LockInv();
        try
        {
            bool success=PowerHelper.DrawPowerSource(source,lease.Required);
            lease.Complete(success); Save(core,lease); RestoreLocks(core);
            if(!success) log.Warning("AE native battery debit declined; network power unavailable.");
        }
        catch { lease.Complete(false); Save(core,lease); RestoreLocks(core); throw; }
    }
    internal static void GridNightComplete(GameItem core)
    {
        if(!ModSettings.EnergyEnabled) { gridDebits.Remove(core); return; }
        var source=UsesPool(core)?PoolGrid(core):Battery(core);
        if(gridDebits.Contains(core) && source!=null && IsGrid(source) && Int(core,"grid_pending_day",-1)==Day && Int(core,"grid_paid_day",-1)!=Day)
        { Number(core,"grid_unpaid",1); RestoreLocks(core); if(UsesPool(core)) RestorePoolLocks(core,new PowerSnapshot()); }
        gridDebits.Remove(core);
    }
    internal static Dictionary<GameItem,GameItem> GridCores()
    {
        var power=new PowerSnapshot();
        return GameSession.AllCores().Where(GeneralHelper.IsItemEffectivelyOwned)
            .Select(c=>(Core:c,Source:UsesPool(c)?PoolGrid(c,power):Battery(c)))
            .Where(p=>p.Source!=null && IsGrid(p.Source)).ToDictionary(p=>p.Source!,p=>p.Core);
    }
    internal static void UpdateGridService(StoreService service)
    {
        var cores=ModSettings.EnergyEnabled?GridCores():new Dictionary<GameItem,GameItem>();
        if(cores.Count==0 && !EnergyReceiver.Hosts().Any(h=>EnergyReceiver.Slots(h).Any(s=>s.childItem!=null && IsGrid(s.childItem)))) return;
        float nativeTotal=0,aeNative=0,aeRequired=0; bool owned=false;
        foreach(var source in PlayerStore.Instance.FindAllItem())
        {
            if(!IsGrid(source)) continue;
            var host=CurrentConverterHelper.GetHostMachine(source);
            float nativeCost=!source.IsTag("ITEM_BEHIND_LOCK_TAG") && CurrentConverterHelper.IsOn(source) && host!=null
                ?CurrentConverterHelper.GetHostUsage(host)*CurrentConverterHelper.GetRate(source):0;
            nativeTotal+=nativeCost;
            bool aeHost=source.parentInventory?.GetParentItem() is GameItem owner
                && (EnergyReceiver.IsReceiver(owner) || owner.identifier=="deepspaceae.core");
            if(aeHost) aeNative+=nativeCost;
            if(cores.TryGetValue(source,out var core))
            {
                var lease=Lease(core); owned=true;
                if(lease.CanSettle(GridUnpaid(core) || gridDebits.Contains(core)))
                    aeRequired+=lease.Required*CurrentConverterHelper.GetRate(source);
            }
        }
        if(owned && !service.introduced) { service.introduced=true; service.isUsing=true; }
        if(owned) service.unlocked=true;
        // Adjust only AE's contribution. Keep native/other-mod service changes.
        if(!CurrentConverterHelper.IsBlackedOut())
            service.cost=Math.Max(0,service.cost+Mathf.CeilToInt(nativeTotal-aeNative+aeRequired)-Mathf.CeilToInt(nativeTotal));
    }
    internal static void GridServicePaid(int amount)
    {
        if(!ModSettings.EnergyEnabled) return;
        var pending=GridCores().Where(p=>gridDebits.Contains(p.Value) && Int(p.Value,"grid_pending_day",-1)==Day).ToArray();
        foreach(var pair in pending)
        {
            var lease=Lease(pair.Value); if(lease.Phase!=NightPhase.DebitStarted) continue;
            lease.Complete(true,Day); Save(pair.Value,lease); Number(pair.Value,"grid_paid_day",Day); Number(pair.Value,"grid_unpaid",0);
            if(UsesPool(pair.Value)) PoolGridPaid(pair.Value);
        }
    }
}
[HarmonyPatch(typeof(CurrentConverterHelper),nameof(CurrentConverterHelper.UpdateServiceCost))]
internal static class AeGridServiceCostPatch
{
    [HarmonyPostfix]
    private static void Postfix(StoreService __0)=>PartStorage.UpdateGridService(__0);
}
[HarmonyPatch(typeof(RunStats),nameof(RunStats.OnServicePaid))]
internal static class AeGridServicePaidPatch
{
    [HarmonyPostfix]
    private static void Postfix(string __0,int __1)
    { if(__0==StoreService.CURRENT_CONVERTER_SERVICE) PartStorage.GridServicePaid(__1); }
}
