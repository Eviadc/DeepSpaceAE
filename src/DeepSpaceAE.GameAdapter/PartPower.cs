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
    internal static bool CanRemovePower(GameItem core)=>UsesPool(core)?Battery(core)==null || CanRemoveSource(Battery(core)!):!GridUnpaid(core) && Lease(core).CanRemove(Day);
    internal static bool CanInstall(GameItem core,GameItem source)
    {
        if(!ReferenceEquals(core.parentInventory,GameSession.MainInventory()) || !GeneralHelper.IsItemEffectivelyOwned(core) || !CanRemovePower(core)) return false;
        int cost=ModEntry.ActiveSession?.CoreCost(core) ?? 2;
        if(UsesPool(core)) return IsPowerItem(source) && CanRemoveSource(source);
        var lease=Lease(core); // trial does not mutate the saved reservation
        return lease.Reserve(Day,Math.Max(2,cost),source.uniqueId,n=>CanSupply(source,n));
    }
    internal static GameItem? Battery(GameItem core)=>Inventory(core).childItems.SingleOrDefault();
    internal static NightReservation Lease(GameItem core)=>new(Int(core,"power_day",-1),Int(core,"power_budget"),Int(core,"power_battery"),(NightPhase)Int(core,"power_phase"));
    private static void Save(GameItem core,NightReservation lease)
    {
        Number(core,"power_day",lease.Day); Number(core,"power_budget",lease.Required);
        Number(core,"power_battery",checked((int)lease.Battery)); Number(core,"power_phase",(int)lease.Phase);
    }
    private static string Stamp(NightReservation lease)=>$"{lease.Day}/{lease.Required}/{lease.Battery}/{lease.Phase}";
    private static void Usage(GameItem core,int amount)
    {
        if(!core.state.dict.TryGetValue("machinery_drawn_power",out var value) || value.GetInt()!=amount)
            core.ModifyTag("machinery_drawn_power",t=>t.Enable().SetInt(amount),true);
    }
    internal static bool Powered(GameItem core,int cost,bool reserve=false)
    {
        if(ModEntry.ActiveSession?.Loading!=false) return false;
        if(UsesPool(core)) return PoolPowered(core,cost);
        if(ModEntry.ActiveSession.NightAutomation)
        {
            var paid=Lease(core); var battery=Battery(core);
            return battery!=null && battery.unitCount==1 && GeneralHelper.IsItemEffectivelyOwned(battery) && !GridUnpaid(core)
                && (!IsGrid(battery) || CanSupply(battery,cost))
                && AutomationRules.PaidNightPower(Day,paid.Day,paid.Phase==NightPhase.Settled,paid.Battery,battery.uniqueId,paid.Required,cost);
        }
        var lease=Lease(core); string before=Stamp(lease); lease.ObservePeak(Day,Math.Max(2,cost));
        var source=Battery(core); bool powered=false;
        if(source!=null && !GridUnpaid(core))
        {
            // A switched-off grid source stays locked and retains its accrued bill.
            if(!IsGrid(source) || CanSupply(source,lease.Required))
                powered=lease.Reserve(Day,Math.Max(2,cost),source.uniqueId,n=>CanSupply(source,n));
        }
        if(before!=Stamp(lease)) { Save(core,lease); ModEntry.ActiveSession?.NativeStorageChanged(core); }
        if(source!=null && IsGrid(source)) Usage(core,lease.Required);
        RestoreLocks(core); return powered;
    }
    internal static void RestoreLocks(GameItem core)
    { if(EnergyReceiver.IsReceiver(core) || UsesPool(core)) PoolLocks(core); else if(CanRemovePower(core)) Inventory(core).UnlockInv(); else Inventory(core).LockInv(); }
    internal static void Settle(GameItem core,int cost,ModLog log)
    {
        if(UsesPool(core)) { PoolSettle(core,cost,log); return; }
        var source=Battery(core); if(source==null || !GeneralHelper.IsItemEffectivelyOwned(core) || !GeneralHelper.IsItemEffectivelyOwned(source)) return;
        var lease=Lease(core);
        if(IsGrid(source))
        {
            // Native service accounting performs the actual cash deduction later.
            // Do not call Supply here: isNightCycle is still false at this hook.
            // A saved in-progress debit without an observed unpaid outcome is
            // unknown. Never replay it merely because another night begins.
            if(lease.Phase==NightPhase.DebitStarted && !GridUnpaid(core)) return;
            if(lease.Phase!=NightPhase.DebitStarted)
            {
                int peak=lease.Day==Day?lease.Required:0;
                if(!lease.Reserve(Day,Math.Max(2,Math.Max(cost,peak)),source.uniqueId,_=>true)) { Save(core,lease); return; }
                if(!lease.BeginDebit(Day,source.uniqueId)) return;
            }
            gridDebits.Add(core);
            Save(core,lease); Usage(core,lease.Required); Number(core,"grid_pending_day",Day); Inventory(core).LockInv();
            var service=StoreService.GetCurrentConverterService(); if(service!=null) CurrentConverterHelper.UpdateServiceCost(service);
            return;
        }
        if(lease.Phase==NightPhase.Settled && lease.Day>=Day || lease.Phase==NightPhase.Fault || lease.Phase==NightPhase.DebitStarted) return;
        int required=Math.Max(2,Math.Max(cost,lease.Day==Day?lease.Required:0));
        if(!lease.Reserve(Day,required,source.uniqueId,n=>CanSupply(source,n))) { Save(core,lease); RestoreLocks(core); return; }
        if(!lease.BeginDebit(Day,source.uniqueId)) return;
        Save(core,lease); Inventory(core).LockInv();
        try
        {
            int before=PowerHelper.GetAvailableEnergyFromItem(source,false); bool infinite=HypertechBatteryHelper.IsHypertechBattery(source);
            bool success=CanSupply(source,lease.Required) && PowerHelper.DrawPowerSource(source,lease.Required);
            int after=PowerHelper.GetAvailableEnergyFromItem(source,false);
            lease.Complete(success && (infinite?before==after:before-after==lease.Required)); Save(core,lease);
            if(lease.Phase==NightPhase.Fault) log.Error("AE battery debit unknown; power slot locked.");
        }
        catch { lease.Complete(false); Save(core,lease); throw; }
    }
    internal static void GridNightComplete(GameItem core)
    {
        var source=UsesPool(core)?PoolGrid(core):Battery(core);
        if(gridDebits.Contains(core) && source!=null && IsGrid(source) && Int(core,"grid_pending_day",-1)==Day && Int(core,"grid_paid_day",-1)!=Day)
        { Number(core,"grid_unpaid",1); RestoreLocks(core); if(UsesPool(core)) RestorePoolLocks(); }
        gridDebits.Remove(core);
    }
    internal static Dictionary<GameItem,GameItem> GridCores()=>GameSession.AllCores().Where(c=>GeneralHelper.IsItemEffectivelyOwned(c))
        .Select(c=>(Core:c,Source:UsesPool(c)?PoolGrid(c):Battery(c))).Where(p=>p.Source!=null && IsGrid(p.Source)).ToDictionary(p=>p.Source!,p=>p.Core);
    internal static void UpdateGridService(StoreService service)
    {
        var cores=GridCores();
        if(cores.Count==0 && !EnergyReceiver.Hosts().Any(h=>EnergyReceiver.IsReceiver(h) && EnergyReceiver.Slots(h).Any(s=>s.childItem!=null && IsGrid(s.childItem)))) return;
        float total=0; bool owned=false;
        foreach(var source in PlayerStore.Instance.FindAllItem())
        {
            if(!IsGrid(source)) continue;
            if(cores.TryGetValue(source,out var core))
            {
                var lease=Lease(core); owned=true;
                if(lease.Phase!=NightPhase.Fault && (lease.Phase!=NightPhase.DebitStarted || GridUnpaid(core) || gridDebits.Contains(core)))
                    total+=lease.Required*CurrentConverterHelper.GetRate(source);
            }
            else if(source.parentInventory?.GetParentItem() is GameItem hostOwner && (EnergyReceiver.IsReceiver(hostOwner) || hostOwner.identifier=="deepspaceae.core" && UsesPool(hostOwner))) { }
            else if(!source.IsTag("ITEM_BEHIND_LOCK_TAG") && CurrentConverterHelper.IsOn(source))
            { var host=CurrentConverterHelper.GetHostMachine(source); if(host!=null) total+=CurrentConverterHelper.GetHostUsage(host)*CurrentConverterHelper.GetRate(source); }
        }
        if(owned && !service.introduced) { service.introduced=true; service.isUsing=true; }
        service.unlocked=service.introduced;
        service.cost=CurrentConverterHelper.IsBlackedOut()?0:Mathf.CeilToInt(total);
    }
    internal static void GridServicePaid(int amount)
    {
        var pending=GridCores().Where(p=>gridDebits.Contains(p.Value) && Int(p.Value,"grid_pending_day",-1)==Day).ToArray();
        float ownBill=pending.Sum(p=>Lease(p.Value).Required*CurrentConverterHelper.GetRate(p.Key));
        if(amount<Mathf.CeilToInt(ownBill) && !(amount==0 && CurrentConverterHelper.IsBlackedOut())) return;
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
