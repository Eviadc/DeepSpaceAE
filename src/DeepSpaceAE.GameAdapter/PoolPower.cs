using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DeepSpaceAE.Core.Network;
namespace DeepSpaceAE.GameAdapter;
internal static partial class PartStorage
{
    private sealed class Connected { internal GameItem[] Receivers=Array.Empty<GameItem>(); }
    private static readonly ConditionalWeakTable<GameItem,Connected> connected=new();
    private static readonly Dictionary<int,WeakReference<GameItem>> reservationCores=new();
    internal static bool UsesPool(GameItem core)=>Int(core,"pool_active")!=0;
    internal static string PoolSummary(GameItem core)=>UiText.Get("ui_pool_summary",Plan(core).Count,Current(core).Length);
    internal static string SourceStatus(GameItem source)
    {
        if(CanRemoveSource(source)) return UiText.Get("ui_replaceable");
        var core=ReservedCore(source);
        if(core!=null && GridUnpaid(core)) return UiText.Get("ui_grid_unpaid");
        if(core!=null && (Lease(core).Phase==NightPhase.Fault || Lease(core).Phase==NightPhase.DebitStarted)) return UiText.Get("ui_billing_unknown");
        return UiText.Get("ui_source_reserved",Int(source,"supply_share"));
    }
    internal static bool IsPowerItem(GameItem source)=>source.unitCount==1 && GeneralHelper.IsItemEffectivelyOwned(source)
        && (IsGrid(source) || source.IsTag(PowerHelper.POWER_SOURCE_ITEM_TAG));
    private static int Key(GameItem item)=>item.GetUniqueID();
    private static GameItem? ReservedCore(GameItem source)
    {
        int id=Int(source,"supply_core"); if(id==0) return null;
        if(reservationCores.TryGetValue(id,out var reference) && reference.TryGetTarget(out var held) && !(held is GameItemElement e && e.IsDestroyed())) return held;
        var core=EnergyReceiver.Hosts().FirstOrDefault(h=>h.identifier=="deepspaceae.core" && Key(h)==id);
        if(core!=null) reservationCores[id]=new WeakReference<GameItem>(core); return core;
    }
    internal static bool CanRemoveSource(GameItem source)
    {
        int phase=Int(source,"supply_phase"); if(Int(source,"supply_share")==0) return true;
        var core=ReservedCore(source);
        if(core!=null && (GridUnpaid(core) || Lease(core).Phase==NightPhase.Fault || Lease(core).Phase==NightPhase.DebitStarted)) return false;
        return phase!=(int)NightPhase.Fault && phase!=(int)NightPhase.DebitStarted
            && (Int(source,"supply_day",-1)<Day || phase==(int)NightPhase.Empty || phase==(int)NightPhase.Shortfall);
    }
    private static void Token(GameItem source,GameItem core,int amount,NightPhase phase,int day)
    {
        if(Int(source,"supply_core")==Key(core) && Int(source,"supply_share")==amount && Int(source,"supply_day",-1)==day && Int(source,"supply_phase")== (int)phase) return;
        Number(source,"supply_core",Key(core)); Number(source,"supply_share",amount); Number(source,"supply_day",day); Number(source,"supply_phase",(int)phase);
    }
    private static void ClearToken(GameItem source)
    { Number(source,"supply_share",0); Number(source,"supply_core",0); Number(source,"supply_phase",(int)NightPhase.Empty); }
    private static List<PowerShare> Plan(GameItem core)
    {
        var result=new List<PowerShare>();
        foreach(var line in Text(core,"pool_plan").Split('\n').Where(s=>s.Length>0))
        {
            var fields=line.Split(':');
            if(fields.Length!=2 || !long.TryParse(fields[0],out long id) || id==0 || !int.TryParse(fields[1],out int amount) || amount<=0)
                throw new InvalidOperationException("Invalid saved power plan; no automatic recovery.");
            result.Add(new PowerShare(id,amount));
        }
        if(result.Select(p=>p.Id).Distinct().Count()!=result.Count) throw new InvalidOperationException("Duplicate source in saved power plan.");
        return result;
    }
    private static GameItem[] Current(GameItem core)=>new[]{core}.Concat(connected.GetOrCreateValue(core).Receivers)
        .Where(GeneralHelper.IsItemEffectivelyOwned).SelectMany(EnergyReceiver.Slots).Select(s=>s.childItem).Where(i=>i!=null).ToArray();
    private static int SourceCapacity(GameItem source,int peak,GameItem core)
    {
        if(!IsPowerItem(source)) return 0;
        if(!CanRemoveSource(source) && (Int(source,"supply_core")!=Key(core)
            || Int(source,"supply_phase")!=(int)NightPhase.Reserved)) return 0;
        int low=0,high=peak;
        while(low<high) { int middle=low+(int)(((long)high-low+1)/2); if(CanSupply(source,middle)) low=middle; else high=middle-1; }
        return low;
    }
    internal static void PoolLinks(GameItem core,IEnumerable<GameItem> receivers)
    {
        reservationCores[Key(core)]=new WeakReference<GameItem>(core);
        connected.GetOrCreateValue(core).Receivers=receivers.ToArray();
        if(UsesPool(core) || connected.GetOrCreateValue(core).Receivers.Length==0) return;
        var lease=Lease(core); var source=Battery(core);
        Number(core,"pool_active",1);
        if(lease.Phase==NightPhase.Shortfall)
            String(core,"pool_rejected",lease.Battery.ToString());
        if(source!=null && lease.Required>0 && lease.Phase!=NightPhase.Empty && lease.Phase!=NightPhase.Shortfall)
        {
            Token(source,core,lease.Required,lease.Phase,lease.Day);
            String(core,"pool_plan",Key(source)+":"+lease.Required);
        }
    }
    private static void Global(GameItem core,int day,int budget,NightPhase phase)
    { Save(core,new NightReservation(day,budget,0,phase)); }
    internal static void PoolLocks(GameItem host)
    {
        foreach(var slot in EnergyReceiver.Slots(host))
        { if(slot.childItem==null || CanRemoveSource(slot.childItem)) slot.UnlockInv(); else slot.LockInv(); }
    }
    private static IEnumerable<GameItem> ReservedSources(GameItem core,PowerSnapshot power)=>power.ForCore(core).Concat(Current(core))
        .Where(i=>Int(i,"supply_core")==Key(core)).Distinct();
    private static void RestorePoolLocks(GameItem core,PowerSnapshot power)
    {
        var hosts=new HashSet<GameItem>(new[]{core}.Concat(connected.GetOrCreateValue(core).Receivers));
        foreach(var source in power.ForCore(core))
            if(source.parentInventory?.GetParentItem() is GameItem host && (EnergyReceiver.IsReceiver(host) || UsesPool(host))) hosts.Add(host);
        foreach(var host in hosts) PoolLocks(host);
    }
    private static void ReleaseExcept(GameItem core,ISet<long> keep,PowerSnapshot power)
    {
        foreach(var source in ReservedSources(core,power))
            if(!keep.Contains(Key(source)) && Int(source,"supply_phase")== (int)NightPhase.Reserved) ClearToken(source);
    }
    internal static bool PoolPowered(GameItem core,int cost,PowerSnapshot? snapshot=null)
    {
        var power=snapshot ?? new PowerSnapshot();
        var lease=Lease(core); var sources=Current(core); var plan=Plan(core);
        var byId=sources.ToDictionary(s=>(long)Key(s));
        if(ModEntry.ActiveSession?.NightAutomation==true)
            return !GridUnpaid(core) && lease.Day==Day && lease.Phase==NightPhase.Settled && cost>0 && cost<=lease.Required
                && plan.Count>0 && plan.Sum(p=>(long)p.Amount)==lease.Required
                && plan.All(p=>byId.TryGetValue(p.Id,out var s) && IsPowerItem(s) && Int(s,"supply_core")==Key(core)
                    && Int(s,"supply_share")>=p.Amount && Int(s,"supply_phase")== (int)NightPhase.Settled
                    && Int(s,"supply_day",-1)==Day && (!IsGrid(s) && !HypertechBatteryHelper.IsHypertechBattery(s) || CanSupply(s,p.Amount)));
        if(GridUnpaid(core) || lease.Phase==NightPhase.Fault || lease.Phase==NightPhase.DebitStarted) { RestorePoolLocks(core,power); return false; }
        if(lease.Day==Day && lease.Phase==NightPhase.Settled) return false;
        if(lease.Day<Day)
        {
            foreach(var source in ReservedSources(core,power))
                if(Int(source,"supply_phase")!=(int)NightPhase.DebitStarted && Int(source,"supply_phase")!=(int)NightPhase.Fault) ClearToken(source);
            plan.Clear(); String(core,"pool_plan",""); String(core,"pool_rejected","");
        }
        int peak=Math.Max(Math.Max(2,cost),lease.Day==Day?lease.Required:0);
        var rejected=new HashSet<string>(Text(core,"pool_rejected").Split(','));
        if(lease.Day==Day && lease.Phase==NightPhase.Shortfall && !sources.Any(s=>!rejected.Contains(Key(s).ToString())))
        { Global(core,Day,peak,NightPhase.Shortfall); RestorePoolLocks(core,power); return false; }
        var plannedIds=new HashSet<long>(plan.Select(p=>p.Id));
        var selected=PowerSelection.Select(peak,sources.Select(s=>new PowerCandidate(Key(s),SourceCapacity(s,peak,core),plannedIds.Contains(Key(s)))));
        if(selected==null)
        {
            // A disconnected receiver still owes its reserved share. It cannot
            // power this network, but its battery is not freed by unplugging it.
            var physical=power.Sources;
            if(plan.Count>0 && plan.Sum(p=>(long)p.Amount)>=peak && plan.All(p=>physical.TryGetValue(p.Id,out var source)
                && EnergyReceiver.ContainsSource(source) && Int(source,"supply_core")==Key(core)
                && Int(source,"supply_share")==p.Amount && Int(source,"supply_day",-1)==lease.Day
                && Int(source,"supply_phase")== (int)NightPhase.Reserved && CanSupply(source,p.Amount)))
            { Global(core,Day,peak,NightPhase.Reserved); RestorePoolLocks(core,power); return false; }
            ReleaseExcept(core,new HashSet<long>(),power); String(core,"pool_plan","");
            String(core,"pool_rejected",string.Join(",",sources.Select(s=>Key(s).ToString())));
            Global(core,Day,peak,NightPhase.Shortfall); RestorePoolLocks(core,power); return false;
        }
        var selectedIds=new HashSet<long>(selected.Select(p=>p.Id));
        ReleaseExcept(core,selectedIds,power);
        foreach(var share in selected) Token(byId[share.Id],core,share.Amount,NightPhase.Reserved,Day);
        string next=string.Join("\n",selected.Select(p=>p.Id+":"+p.Amount));
        bool changed=Text(core,"pool_plan")!=next || lease.Required!=peak || lease.Phase!=NightPhase.Reserved || lease.Day!=Day;
        if(changed) { String(core,"pool_plan",next); Global(core,Day,peak,NightPhase.Reserved); ModEntry.ActiveSession?.Visuals.Invalidate(); }
        foreach(var group in sources.Where(IsGrid).GroupBy(source=>source.parentInventory?.GetParentItem()))
        {
            if(group.Key!=null) Usage(group.Key,group.Any(source=>selectedIds.Contains(Key(source)))?peak:0);
        }
        RestorePoolLocks(core,power); return true;
    }
    internal static GameItem? PoolGrid(GameItem core,PowerSnapshot? snapshot=null)
    {
        var physical=(snapshot ?? new PowerSnapshot()).Sources;
        return Plan(core).Select(p=>physical.TryGetValue(p.Id,out var source)?source:null).FirstOrDefault(s=>s!=null && IsGrid(s));
    }
    internal static void PoolSettle(GameItem core,int cost,ModLog log,PowerSnapshot? snapshot=null)
    {
        if(!GeneralHelper.IsItemEffectivelyOwned(core)) return;
        var lease=Lease(core);
        if(!lease.CanSettle(GridUnpaid(core))) return;
        var plan=Plan(core); var power=snapshot ?? new PowerSnapshot(); var sources=power.Sources;
        if(plan.Count==0 || plan.Sum(p=>(long)p.Amount)!=lease.SettlementBudget(Day,cost)) return;
        if(plan.Any(p=>!sources.TryGetValue(p.Id,out var source) || Int(source,"supply_core")!=Key(core)
            || Int(source,"supply_share")!=p.Amount || Int(source,"supply_day",-1)!=lease.Day
            || Int(source,"supply_phase")!=(int)lease.Phase || !EnergyReceiver.ContainsSource(source) || !IsGrid(source) && !CanSupply(source,p.Amount)))
        {
            if(lease.Phase==NightPhase.DebitStarted)
            {
                Global(core,lease.Day,lease.Required,NightPhase.Fault);
                log.Error("Unpaid grid reservation no longer matches its source; no replay or release.");
            }
            else
            {
                ReleaseExcept(core,new HashSet<long>(),power); String(core,"pool_plan","");
                Global(core,Day,lease.Required,NightPhase.Shortfall);
            }
            RestorePoolLocks(core,power); return;
        }
        Global(core,Day,lease.Required,NightPhase.DebitStarted); RestorePoolLocks(core,power);
        foreach(var share in plan)
        {
            var source=sources[share.Id]; Token(source,core,share.Amount,NightPhase.DebitStarted,Day);
            if(IsGrid(source))
            {
                if(plan.Count!=1) throw new InvalidOperationException("Grid source must cover the entire plan.");
                gridDebits.Add(core); Number(core,"grid_pending_day",Day); Usage(source.parentInventory!.GetParentItem(),share.Amount);
                var service=StoreService.GetCurrentConverterService(); if(service!=null) CurrentConverterHelper.UpdateServiceCost(service);
                return;
            }
            try
            {
                if(!EnergyReceiver.ContainsSource(source) || !CanSupply(source,share.Amount))
                    throw new InvalidOperationException("Pooled power source changed before native debit.");
                int before=PowerHelper.GetAvailableEnergyFromItem(source,false); bool infinite=HypertechBatteryHelper.IsHypertechBattery(source);
                bool success=PowerHelper.DrawPowerSource(source,share.Amount);
                int after=PowerHelper.GetAvailableEnergyFromItem(source,false);
                if(!success || (infinite?before!=after:before-after!=share.Amount)) throw new InvalidOperationException("Pooled native battery debit unknown.");
                Token(source,core,share.Amount,NightPhase.Settled,Day);
            }
            catch { Token(source,core,share.Amount,NightPhase.Fault,Day); Global(core,Day,lease.Required,NightPhase.Fault); RestorePoolLocks(core,power); throw; }
        }
        Global(core,Day,lease.Required,NightPhase.Settled); RestorePoolLocks(core,power);
    }
    internal static void PoolGridPaid(GameItem core)
    {
        var source=PoolGrid(core); if(source!=null) Token(source,core,Lease(core).Required,NightPhase.Settled,Day);
        RestorePoolLocks(core,new PowerSnapshot());
    }
}
