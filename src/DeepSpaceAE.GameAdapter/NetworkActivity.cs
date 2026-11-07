using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DeepSpaceAE.Core.Network;
using HarmonyLib;

namespace DeepSpaceAE.GameAdapter;

[HarmonyPatch(typeof(PlayerStore),nameof(PlayerStore.SaveGame))]
internal static class NetworkActivitySavePatch
{
    [HarmonyPrefix]
    private static void Prefix()=>ModEntry.ActiveSession?.SaveNetworkActivity();
}

internal sealed partial class GameSession
{
    private sealed class ActivityInventory
    {
        internal readonly Dictionary<GameItem,(string Key,int Count)> Items=new();
        internal readonly Dictionary<string,long> Totals=new(StringComparer.Ordinal);
        internal readonly HashSet<GameInventory> Dependencies=new();
        internal readonly Dictionary<GameInventory,GameItem> DependencyOwners=new();
    }
    private readonly Dictionary<string,StackActivity> activities=new(StringComparer.Ordinal);
    private readonly Dictionary<string,HashSet<GameInventory>> activityNetworks=new(StringComparer.Ordinal);
    private readonly Dictionary<string,HashSet<GameItem>> activityLiquidNetworks=new(StringComparer.Ordinal);
    private readonly Dictionary<GameInventory,ActivityInventory> activityInventories=new();
    private readonly Dictionary<GameItem,LiquidSnapshot> activityLiquids=new();
    private readonly HashSet<GameInventory> activityObserved=new(),activityDirty=new();
    private readonly HashSet<string> activityTouched=new(StringComparer.Ordinal);
    private int activityTopology=-1,activityGeneration;
    private bool activityQueued,activityFlushing;

    // A digest keeps the network save compact. Display equality still uses the
    // full canonical key; these digests only address sorting metadata.
    private static string ActivityKey(GameItem item,string? state)
    {
        string prefix="";
        if(state==null)
        {
            prefix="instance:"+item.GetUniqueID()+":";
            state=TerminalStackKey.Read(item,storageGrouping:true,includeContents:true);
            if(state==null) return prefix;
        }
        using var sha=SHA256.Create();
        return prefix+Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(state)));
    }
    private static string LiquidActivityKey(GameItem item,LiquidSnapshot snapshot)
        =>"liquid:"+item.GetUniqueID()+":"+ActivityKey(item,snapshot.Stamp);
    private StackActivity ActivityFor(string core)
    {
        if(activities.TryGetValue(core,out var activity)) return activity;
        try { activity=StackActivity.Decode(ModHelper.GetModData(ModEntry.ModId,"activity_"+core,"")); }
        catch(FormatException e) { log.Warning(e.Message); activity=new StackActivity(); }
        activities.Add(core,activity); return activity;
    }
    internal void ResetActivity()
    {
        foreach(var inv in activityObserved) UnsubscribeActivity(inv);
        activityObserved.Clear(); activityNetworks.Clear(); activityInventories.Clear(); activityDirty.Clear(); activityTouched.Clear();
        activityLiquidNetworks.Clear(); activityLiquids.Clear(); activities.Clear();
        activityTopology=-1; activityGeneration++; activityQueued=false;
    }
    private void UnsubscribeActivity(GameInventory inv)
    { inv.onSlotAddItemEndFunc-=ActivityChanged; inv.onSlotRemoveItemEndFunc-=ActivityChanged; inv.onSlotChangeAmountItemFunc-=ActivityChanged; }
    private void ObserveActivityInventories()
    {
        var needed=new HashSet<GameInventory>(activityInventories.Keys);
        foreach(var snapshot in activityInventories.Values) needed.UnionWith(snapshot.Dependencies);
        foreach(var inv in activityObserved.Where(i=>!needed.Contains(i)).ToArray())
        { UnsubscribeActivity(inv); activityObserved.Remove(inv); }
        foreach(var inv in needed) if(activityObserved.Add(inv))
        { inv.onSlotAddItemEndFunc+=ActivityChanged; inv.onSlotRemoveItemEndFunc+=ActivityChanged; inv.onSlotChangeAmountItemFunc+=ActivityChanged; }
    }
    internal void RefreshActivityNetworks(Board board)
    {
        if(Loading || activityTopology==automationGeneration) return;
        activityTopology=automationGeneration;
        activityNetworks.Clear(); activityLiquidNetworks.Clear();
        var needed=new HashSet<GameInventory>(); var liquids=new HashSet<GameItem>();
        foreach(var core in board.Evaluation.NightCosts.Keys)
        {
            var view=ReadStorage(board,core,catalog:false);
            var inventories=new HashSet<GameInventory>(view.Inventories.Values.SelectMany(v=>v));
            activityNetworks.Add(core,inventories); needed.UnionWith(inventories);
            var sources=new HashSet<GameItem>(view.LiquidPolicies.Keys);
            activityLiquidNetworks.Add(core,sources); liquids.UnionWith(sources);
        }
        foreach(var inv in activityInventories.Keys.Where(i=>!needed.Contains(i)).ToArray()) activityInventories.Remove(inv);
        foreach(var inv in needed) if(!activityInventories.ContainsKey(inv))
        { activityInventories.Add(inv,new ActivityInventory()); activityDirty.Add(inv); }
        foreach(var item in activityLiquids.Keys.Where(i=>!liquids.Contains(i)).ToArray()) activityLiquids.Remove(item);
        foreach(var item in liquids) activityLiquids[item]=LiquidStorage.Read(item);
        ObserveActivityInventories();
        FlushActivity();
    }
    private void ActivityChanged(GameItem item,GameInventory inventory,SlotMarker slot)
    {
        if(activityInventories.ContainsKey(inventory)) activityDirty.Add(inventory);
        foreach(var pair in activityInventories)
            if(pair.Value.DependencyOwners.TryGetValue(inventory,out var owner))
            {
                activityDirty.Add(pair.Key);
                activityTouched.Add(ActivityKey(owner,TerminalStackKey.Read(owner,storageGrouping:true)));
            }
        QueueActivity();
    }
    internal void ActivityItemValidated(GameItem item)
    {
        if(Loading || activityFlushing) return;
        var inv=item.parentInventory;
        if(inv!=null && activityInventories.TryGetValue(inv,out var snapshot) && !activityDirty.Contains(inv))
        {
            var state=TerminalStackKey.Read(item,storageGrouping:true);
            if(!snapshot.Items.TryGetValue(item,out var old) || old.Key!=ActivityKey(item,state) || old.Count!=item.unitCount)
            { activityDirty.Add(inv); QueueActivity(); }
        }
        if(activityLiquids.TryGetValue(item,out var liquid))
        {
            var current=LiquidStorage.Read(item);
            if(current.Stamp!=liquid.Stamp) { activityLiquids[item]=current; QueueActivity(); }
        }
    }
    private void QueueActivity()
    {
        if(Loading || activityQueued || activityFlushing) return;
        activityQueued=true; int expected=activityGeneration;
        LetterboxManager.RunNextUpdate(()=>{
            if(expected!=activityGeneration) return;
            activityQueued=false;
            if(writing || ItemMouseDragHandler.current?.IsDraggingItem==true || ItemMultiSelectHandler.current?.IsGroupDragging==true) return;
            try
            {
                if(activityTopology!=automationGeneration) RefreshActivityNetworks(ReadBoard());
                else FlushActivity();
            }
            catch(Exception e) { log.Warning("AE network activity: "+Diagnostics.ConsoleText(e.ToString())); }
        });
    }
    private void FlushActivity()
    {
        if(Loading || activityFlushing) return;
        activityFlushing=true;
        try
        {
            foreach(var inv in activityDirty.ToArray())
            {
                if(!activityInventories.TryGetValue(inv,out var snapshot)) continue;
                snapshot.Items.Clear(); snapshot.Totals.Clear(); snapshot.Dependencies.Clear(); snapshot.DependencyOwners.Clear();
                foreach(var item in inv.childItems)
                {
                    var dependencies=new HashSet<GameInventory>();
                    var state=TerminalStackKey.Read(item,dependencies,storageGrouping:true);
                    snapshot.Dependencies.UnionWith(dependencies);
                    foreach(var dependency in dependencies) snapshot.DependencyOwners[dependency]=item;
                    string key=ActivityKey(item,state);
                    snapshot.Items[item]=(key,item.unitCount);
                    snapshot.Totals.TryGetValue(key,out long count); snapshot.Totals[key]=count+item.unitCount;
                }
            }
            activityDirty.Clear(); ObserveActivityInventories();
            bool modified=false;
            foreach(var network in activityNetworks)
            {
                var totals=new Dictionary<string,long>(StringComparer.Ordinal);
                foreach(var inv in network.Value)
                    foreach(var pair in activityInventories[inv].Totals)
                    { totals.TryGetValue(pair.Key,out long count); totals[pair.Key]=count+pair.Value; }
                foreach(var item in activityLiquidNetworks[network.Key])
                {
                    var liquid=activityLiquids[item];
                    totals[LiquidActivityKey(item,liquid)]=liquid.Composition.Volume;
                }
                var activity=ActivityFor(network.Key);
                if(!activity.Observe(totals,DateTime.UtcNow.Ticks,activityTouched)) continue;
                ModHelper.SetModData(ModEntry.ModId,"activity_"+network.Key,activity.Encode()); modified=true;
            }
            activityTouched.Clear();
            if(modified && openTerminal!=null) { terminalDirty=true; ScheduleTerminalRefresh(); }
        }
        finally { activityFlushing=false; }
    }
    internal void SaveNetworkActivity()
    {
        if(Loading || MainInventory()==null) return;
        try { RefreshActivityNetworks(ReadBoard()); FlushActivity(); }
        catch(Exception e) { log.Warning("AE network activity save: "+Diagnostics.ConsoleText(e.ToString())); }
    }
    private long StackModified(View view,GameItem item,string? state)
        =>ActivityFor(Id(view.Core)).Modified(ActivityKey(item,state));
}
