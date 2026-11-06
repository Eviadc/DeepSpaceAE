using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using DeepSpaceAE.Core;
using DeepSpaceAE.Core.Network;
using UnityEngine;

namespace DeepSpaceAE.GameAdapter;

internal sealed partial class GameSession
{
    internal const string WindowId = "deepspaceae.terminal";
    private sealed class Token
    {
        internal Guid Id { get; } = Guid.NewGuid();
    }
    private ConditionalWeakTable<GameItem, Token> identities = new();
    private sealed class Observation
    {
        private int count;
        private string? name, sprite;
        private string liquid="";
        private readonly HashSet<string> enabled = new(StringComparer.Ordinal);
        internal Observation(GameItem item) => Capture(item);
        private void Capture(GameItem item)
        {
            count = item.unitCount; name = item.name; sprite = item.spritePath; enabled.Clear();
            liquid=LiquidStorage.IsContainer(item)?LiquidStorage.Read(item).Stamp:"";
            foreach (var pair in item.modifiedState.dict) if (pair.Value.valueEnabled) enabled.Add(pair.Key);
        }
        internal bool Changed(GameItem item)
        {
            bool different = count != item.unitCount || name != item.name || sprite != item.spritePath
                || liquid!=(LiquidStorage.IsContainer(item)?LiquidStorage.Read(item).Stamp:"");
            int keys = 0;
            foreach (var pair in item.modifiedState.dict)
                if (pair.Value.valueEnabled) { keys++; if (!enabled.Contains(pair.Key)) different = true; }
            if (!different && keys == enabled.Count) return false;
            Capture(item); return true;
        }
    }
    private ConditionalWeakTable<GameItem, Observation> observations = new();
    private readonly ModLog log;
    internal bool Loading { get; private set; } = true;
    private bool writing, refreshQueued, terminalDirty;
    private int terminalGeneration;
    private CatalogSort sort = CatalogSort.Name;
    private string search = "", tag = "", message = "";
    private GameItem? openTerminal;
    private readonly HashSet<GameInventory> observed = new();
    private sealed class Rejected : Exception { internal Rejected(string text) : base(text) { } }
    internal CableVisuals Visuals { get; }
    internal GameSession(ModLog log) { this.log = log; Visuals = new CableVisuals(log, this); }
    internal static GameGridInventory? MainInventory() => EmporiumEntry.Instance?.invElement;
    // Persistent native identity makes channel allocation and equal-priority routes
    // deterministic across save/load. Directory GUIDs remain view-only session keys.
    internal string Id(GameItem item) => item.GetUniqueID().ToString("X8",CultureInfo.InvariantCulture);
    internal Guid GuidFor(GameItem item) => identities.GetValue(item, _ => new Token()).Id;
    internal static bool IsPart(GameItem item) => item.identifier != null && ModEntry.PartKinds.ContainsKey(item.identifier);
    internal void Reset()
    {
        PartStorage.ResetPowerSettlement();
        ResetInterfaces();
        NightAutomation=false; automationGeneration++;
        NativeLiquidSelection.EndFor(openTerminal); NativeLiquidSelection.Clear(); liquidMode=false;
        Loading = true; Close(); NativeDrag.Gesture = null; NativeDrag.OriginItem = null; NativeDrag.OriginSource = null; Visuals.Detach(); identities = new(); observations = new(); writing = false;
        search = tag = message = "";
    }
    private void Unobserve()
    {
        foreach (var inv in observed) { inv.onSlotAddItemEndFunc -= Changed; inv.onSlotRemoveItemEndFunc -= Changed; inv.onSlotChangeAmountItemFunc -= Changed; }
        observed.Clear(); openTerminal = null; terminalDirty = false; refreshQueued = false; terminalGeneration++;
    }
    internal void Close()
    {
        NativeLiquidSelection.EndFor(openTerminal);
        Unobserve(); CloseNative();
        if (CustomUIManager.Instance == null) return;
        CustomUIManager.Instance.CloseWindow(WindowId); CustomUIManager.Instance.CloseWindow("deepspaceae.part");
    }
    private void Changed(GameItem item, GameInventory inventory, SlotMarker slot)
    {
        terminalDirty = openTerminal != null;
        ScheduleTerminalRefresh(); RefreshPartLater();
    }
    private void ScheduleTerminalRefresh()
    {
        if (refreshQueued || !terminalDirty || openTerminal == null) return;
        refreshQueued = true;
        var current = openTerminal;
        int expected = terminalGeneration;
        LetterboxManager.RunNextUpdate(() => {
            if (expected != terminalGeneration) return;
            refreshQueued = false;
            if (!ReferenceEquals(openTerminal, current) || !terminalDirty) return;
            if (writing || ItemMouseDragHandler.current?.IsDraggingItem == true || ItemMultiSelectHandler.current?.IsGroupDragging == true) return; // retain dirty until release
            terminalDirty = false; Open(current);
        });
    }
    internal void DragEnded() { terminalGrid?.RefreshVisible(); ScheduleTerminalRefresh(); RefreshPartLater(); QueueInterfaces(); }
    internal void ItemChanged(GameItem item)
    {
        if (item.parentInventory != null && observed.Contains(item.parentInventory)
            && observations.GetValue(item, i => new Observation(i)).Changed(item)) Changed(item, item.parentInventory, null!);
    }
    internal void TerminalItemValidated(GameItem item)
    {
        if(displayedView==null || item.parentInventory==null || !observed.Contains(item.parentInventory)) return;
        var id=GuidFor(item);
        if(!displayedView.StackKeys.TryGetValue(id,out var previous)) return;
        var current=TerminalStackKey.Read(item,storageGrouping:true);
        if(previous==current) return;
        displayedView.StackKeys[id]=current;
        Changed(item,item.parentInventory,null!);
    }
    internal void StructureChanged(GameItem item)
    {
        var host = PartStorage.BusHost(item);
        if (host?.parentInventory != null && observed.Contains(host.parentInventory)) Changed(host, host.parentInventory, null!);
        else if (item.parentInventory != null && observed.Contains(item.parentInventory)) Changed(item, item.parentInventory, null!);
    }
    internal static List<GameInventory> Contents(GameItem owner)
    {
        if (DrawerStorage.IsDrawer(owner)) return new List<GameInventory> { DrawerStorage.Data(owner) };
        var result = new List<GameInventory>();
        if (owner.child == null) return result;
        var visited = new HashSet<GraphNodeStorage>(); var queue = new Queue<GraphNodeStorage>(); queue.Enqueue(owner.child);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue(); if (!visited.Add(node)) continue;
            if (node is GameInventory inventory) { if (ReferenceEquals(inventory.GetParentItem(), owner)) result.Add(inventory); continue; }
            if (node is GameItem) continue;
            foreach (var child in node.children) if (child != null) queue.Enqueue(child);
        }
        return result;
    }
    internal sealed class Board
    {
        internal InventoryLayout Layout = null!;
        internal NetworkTopology Topology = null!;
        internal NetworkEvaluation Evaluation = null!;
        internal Dictionary<string, GameItem> ById = new(StringComparer.Ordinal);
        internal HashSet<string> Powered = new(StringComparer.Ordinal);
        internal Dictionary<string,GameItem[]> Receivers=new(StringComparer.Ordinal);
    }
    internal Board ReadBoard(bool reserve = false)
    {
        var root = MainInventory() ?? throw new Rejected(UiText.Get("error_enter_game"));
        var board = new Board { Layout = new InventoryLayout(root) };
        foreach (var part in board.Layout.Parts) board.ById.Add(Id(part), part);
        var nodes = board.Layout.Parts.Select(i => new NetworkNode(Id(i), ModEntry.PartKinds[i.identifier],
            i.identifier == "deepspaceae.item_storage_bus" || DiskStorage.IsDrive(i) ? Id(i) : null));
        board.Topology = PhysicalNetworkProjection.Build(nodes, board.Layout.Edges(Id), new ChannelSettings());
        board.Evaluation = new NetworkEvaluation(board.Topology);
        if(!ModSettings.EnergyEnabled)
        {
            if(!Loading) board.Powered.UnionWith(board.Evaluation.NightCosts.Keys);
            return board;
        }
        foreach(var group in board.Layout.Parts.Where(EnergyReceiver.IsReceiver)
            .Where(r=>board.Evaluation.Devices[Id(r)].Status==DeviceStatus.Online && board.Evaluation.Devices[Id(r)].CoreId!=null)
            .GroupBy(r=>board.Evaluation.Devices[Id(r)].CoreId!)) board.Receivers.Add(group.Key,group.ToArray());
        var power=new PowerSnapshot();
        foreach (var pair in board.Evaluation.NightCosts)
        {
            PartStorage.PoolLinks(board.ById[pair.Key],board.Receivers.TryGetValue(pair.Key,out var receivers)?receivers:Array.Empty<GameItem>());
            if (PartStorage.Powered(board.ById[pair.Key], pair.Value, reserve,power)) board.Powered.Add(pair.Key);
        }
        return board;
    }
    internal int CoreCost(GameItem core)
    {
        if(!ModSettings.EnergyEnabled) return 0;
        if (!ReferenceEquals(core.parentInventory, MainInventory())) return 2;
        var board = ReadBoard();
        return board.Evaluation.NightCosts.TryGetValue(Id(core), out var cost) ? cost : 2;
    }
    internal void DayStarted()
    {
        Loading = false;
        if (PlayerStore.Instance != null)
            foreach (var item in PlayerStore.Instance.FindAllItem(false))
            {
                if(!ModSettings.EnergyEnabled) PartStorage.SuspendPower(item);
                if (IsPart(item))
                {
                    var definition = ModEntry.Parts.First(p => p.Id == item.identifier);
                    item.name = UiText.Get(definition.NameKey);
                    item.shortDescription = IoBus.IsBus(item) ? UiText.Get(IoBus.IsIo(item)?"item_io_desc":"item_bus_desc") : definition.Part == NetworkPart.Pipeline ? UiText.Get("item_cable_desc") : UiText.Get("item_part_desc");
                    item.flavorText = UiText.Get("item_flavor");
                }
                if (IsPart(item) && !ReferenceEquals(item.parentInventory, MainInventory())) Visuals.Inactive(item);
                if (item.identifier == "deepspaceae.core" || EnergyReceiver.IsReceiver(item)) PartStorage.RestoreLocks(item);
            }
        Visuals.Invalidate();
    }
    internal void PowerModeChanged()
    {
        if(Loading || !PlayerStore.HasInstance || MainInventory()==null) return;
        PartStorage.ResetPowerSettlement();
        foreach(var item in PlayerStore.Instance.FindAllItem(false))
        {
            if(!ModSettings.EnergyEnabled) PartStorage.SuspendPower(item);
            else if(item.identifier=="deepspaceae.core" || EnergyReceiver.IsReceiver(item)) PartStorage.RestoreLocks(item);
        }
        var service=StoreService.GetCurrentConverterService();
        if(service!=null) CurrentConverterHelper.UpdateServiceCost(service);
        if(openTerminal!=null) { terminalDirty=true; ScheduleTerminalRefresh(); }
        RefreshPartLater(); Visuals.Invalidate();
    }
    internal static IEnumerable<GameItem> AllCores() => PlayerStore.Instance == null ? Array.Empty<GameItem>()
        : PlayerStore.Instance.FindAllItem(false).Where(i => i.identifier == "deepspaceae.core").Distinct();
    internal void Night()
    {
        Close();
        if(!ModSettings.EnergyEnabled) { Visuals.Invalidate(); return; }
        Board? board = null;
        try { if (MainInventory() != null) board = ReadBoard(true); }
        catch (Exception e) { log.Error(Diagnostics.ConsoleText(e.ToString())); }
        var power=new PowerSnapshot();
        foreach (var core in AllCores())
            try
            {
                int cost = board != null && board.Evaluation.NightCosts.TryGetValue(Id(core), out var n) ? n : 0;
                PartStorage.PoolLinks(core,board!=null && board.Receivers.TryGetValue(Id(core),out var receivers)?receivers:Array.Empty<GameItem>());
                PartStorage.Settle(core, cost, log,power);
            }
            catch (Exception e) { log.Error(Diagnostics.ConsoleText(e.ToString())); }
        Visuals.Invalidate();
    }
    internal void NightLate()
    {
        if(!ModSettings.EnergyEnabled) { Visuals.Invalidate(); return; }
        foreach(var core in AllCores()) PartStorage.GridNightComplete(core);
        Visuals.Invalidate();
    }
    private sealed class View
    {
        internal Board Board = null!;
        internal GameGridInventory Root = null!;
        internal GameItem Core = null!;
        internal Dictionary<GameItem, GameItem> Owners = new();
        internal Dictionary<GameItem, StoragePolicy> Policies = new();
        internal Dictionary<GameItem, List<GameInventory>> Inventories = new();
        internal List<GameInventory> Attachments = new();
        internal Dictionary<Guid, GameItem> Live = new();
        internal Dictionary<Guid, GameInventory> Sources = new();
        internal Dictionary<Guid,string?> StackKeys=new();
        internal HashSet<GameInventory> StackDependencies=new();
        internal CatalogSearch Search = null!;
        internal CatalogSearch TagSearch = null!;
        internal CatalogSearch LiquidSearch=null!;
        internal Dictionary<Guid,LiquidSnapshot> Liquids=new();
        internal Dictionary<GameItem,List<StoragePolicy>> LiquidPolicies=new();

    }
    private View Read(GameItem terminal)
    {
        var board = ReadBoard(); var root = MainInventory()!;
        if (DrawerStorage.IsDrawer(terminal))
        {
            if (!ReferenceEquals(terminal.parentInventory, root) || !GeneralHelper.IsItemEffectivelyOwned(terminal)) throw new Rejected(UiText.Get("error_part_main"));
            var local = new View { Board = board, Root = root, Core = terminal };
            var policy = new StoragePolicy(0, StorageAccess.ReadWrite, false, false, false, 0, Array.Empty<string>());
            local.Owners.Add(terminal, terminal); local.Policies.Add(terminal, policy); local.Inventories.Add(terminal, Contents(terminal));
            local.Attachments.Add(DrawerStorage.Upgrades(terminal));
            return ReadCatalog(local, new Dictionary<GameItem, List<StoragePolicy>> { [terminal] = new List<StoragePolicy> { policy } });
        }
        if (!ReferenceEquals(terminal.parentInventory, root) || !GeneralHelper.IsItemEffectivelyOwned(terminal)) throw new Rejected(UiText.Get("error_terminal_main"));
        if (!board.Evaluation.Devices.TryGetValue(Id(terminal), out var connection) || connection.Status != DeviceStatus.Online || connection.CoreId == null)
            throw new Rejected(UiText.Get("error_terminal_network"));
        if (!board.Powered.Contains(connection.CoreId)) throw new Rejected(UiText.Get("error_network_power"));
        var view = ReadStorage(board,connection.CoreId);
        return view;
    }
    private View ReadStorage(Board board,string coreId)
    {
        var view = new View { Root = MainInventory()!, Board = board, Core = board.ById[coreId] };
        var ownerPolicies = new Dictionary<GameItem, List<StoragePolicy>>();
        foreach (var bus in board.Layout.Parts.Where(p => p.identifier == "deepspaceae.item_storage_bus"))
        {
            var bc = board.Evaluation.Devices[Id(bus)];
            if (bc.Status != DeviceStatus.Online || bc.CoreId != coreId) continue;
            var owner = board.Layout.FrontOwner(bus);
            if (owner == null || IsPart(owner) || DiskStorage.IsDisk(owner) || !board.Layout.Owned.Contains(owner)) continue;
            if(LiquidStorage.IsContainer(owner))
            {
                if(!view.LiquidPolicies.TryGetValue(owner,out var liquidPolicies)) view.LiquidPolicies.Add(owner,liquidPolicies=new List<StoragePolicy>());
                liquidPolicies.Add(PartStorage.Policy(bus));
            }
            if (!view.Inventories.TryGetValue(owner, out var invs)) { invs = Contents(owner); view.Inventories.Add(owner, invs); }
            if (invs.Count == 0) continue;
            var policy = PartStorage.Policy(bus); view.Owners.Add(bus, owner); view.Policies.Add(bus, policy);
            if (!ownerPolicies.TryGetValue(owner, out var ps)) ownerPolicies.Add(owner, ps = new List<StoragePolicy>());
            ps.Add(policy);
        }
        foreach(var drive in board.Layout.Parts.Where(DiskStorage.IsDrive))
        {
            if(!board.Evaluation.Devices.TryGetValue(Id(drive),out var dc) || dc.Status!=DeviceStatus.Online || dc.CoreId!=coreId) continue;
            var bays=DiskStorage.Bays(drive); view.Attachments.AddRange(bays); view.Attachments.Add(DiskStorage.Upgrades(drive));
            var invs=bays.Take(DiskStorage.Unlocked(drive)).Where(b=>b.childItem!=null).Select(b=>b.childItem).Where(DiskStorage.IsDisk).Distinct()
                .Select(d=>DiskStorage.Data(d)).Cast<GameInventory>().ToList();
            view.Inventories.Add(drive,invs); view.Owners.Add(drive,drive);
            var policy=new StoragePolicy(0,StorageAccess.ReadWrite,false,false,false,0,Array.Empty<string>());
            view.Policies.Add(drive,policy); ownerPolicies.Add(drive,new List<StoragePolicy>{policy});
        }
        return ReadCatalog(view, ownerPolicies);
    }
    private View ReadCatalog(View view, Dictionary<GameItem, List<StoragePolicy>> ownerPolicies)
    {
        var records = new List<CatalogRecord>(); var tagRecords = new List<CatalogRecord>();
        foreach (var pair in ownerPolicies)
        {
            var filter = new ExtractionFilter(pair.Value);
            foreach (var inventory in view.Inventories[pair.Key]) foreach (var item in inventory.childItems)
            {
                var itemTags = PartStorage.Tags(item);
                if (!filter.Allows(item.identifier, itemTags)) continue;
                var id = GuidFor(item);
                if(!view.Live.TryAdd(id,item)) continue;
                view.Sources.Add(id, item.parentInventory ?? inventory);
                var publicTags = item.GetGameItemType();
                records.Add(new CatalogRecord(id, item.identifier, item.name ?? "?", publicTags, item.unitCount, publicTags.Select(NativeWidgets.TagName)));
                tagRecords.Add(new CatalogRecord(id, item.identifier, item.name ?? "?", itemTags, item.unitCount, itemTags.Select(NativeWidgets.TagName)));
            }
        }
        view.Search = new CatalogSearch(records);
        view.TagSearch = new CatalogSearch(tagRecords);
        var liquidRows=new List<CatalogRecord>();
        foreach(var pair in view.LiquidPolicies)
        {
            var owner=pair.Key; var tags=PartStorage.Tags(owner);
            bool readable=pair.Value.Any(p=>p.CanExtract(owner.identifier,tags));
            if(!readable && !pair.Value.Any(p=>p.CanInsert(owner.identifier,tags))) continue;
            var id=GuidFor(owner); var snapshot=LiquidStorage.Read(owner); snapshot.Readable=readable;
            view.Liquids[id]=snapshot; view.Live[id]=owner; view.Sources[id]=owner.parentInventory;
            liquidRows.Add(new CatalogRecord(id,"liquid:"+(readable?snapshot.Composition.Signature:id.ToString()),
                (readable?snapshot.Name:UiText.Get("ui_liquid_insert_only"))+" · "+owner.name,
                new[]{"deepspaceae.liquid"},readable?(int)Math.Min(int.MaxValue,snapshot.Composition.Volume):0));
        }
        view.LiquidSearch=new CatalogSearch(liquidRows,aggregateQuantity:false);
        return view;
    }
    private void Error(Exception e)
    {
        log.Warning(Diagnostics.ConsoleText(e.ToString()));
        ShowNotice(e is Rejected ? Safe(e.Message) : UiText.Get("error_operation"));
    }
    internal void OpenPart(GameItem part)
    {
        if (writing) return;
        try
        {
            if (part.identifier == "deepspaceae.terminal") { Open(part); return; }
            if (IoBus.IsBus(part))
            {
                if (PartStorage.CanConfigure(part)) ShowBus(part); else ShowNotice(UiText.Get("ui_install_bus_first"));
                return;
            }
            if (!GeneralHelper.IsItemEffectivelyOwned(part)) throw new Rejected(UiText.Get("error_not_owned"));
            if (DrawerStorage.IsDrawer(part)) ShowDrawer(part);
            else if(InterfaceStorage.IsInterface(part)) ShowInterface(part);
            else if (DiskStorage.IsDrive(part)) ShowDrive(part);
            else if(EnergyReceiver.IsReceiver(part)) ShowReceiver(part);
            else if (part.identifier == "deepspaceae.core") ShowCore(part);
            // Cables have no independent UI; double-click is routed by hit face.
        }
        catch (Exception e) { Error(e); }
    }
    internal void Open(GameItem terminal)
    {
        if (writing) return;
        try
        {
            var view = Read(terminal);
            if (!PowerView(view)) throw new Rejected(UiText.Get("error_reserve"));
            Show(terminal, view);
        }
        catch (Exception e) { CloseTerminal(); CustomUIManager.Instance?.CloseWindow(WindowId); Error(e); }
    }
    private void ConfigureNumber(GameItem bus, string key, int value)
    {
        if (!PartStorage.CanConfigure(bus)) return;
        AutomationStructureChanged();
        PartStorage.Number(bus, key, value); Visuals.Invalidate(); StructureChanged(bus);
    }
    private void SetFilter(GameItem bus, int slot, string value)
    {
        if (!PartStorage.CanConfigure(bus)) return;
        AutomationStructureChanged();
        var filters = PartStorage.Filters(bus); filters[slot] = value.Replace("\n", "").Replace("\r", "").Trim();
        bool tags=PartStorage.Policy(bus).Tags;
        if(filters[slot].Length==0 && !tags && IoBus.IsIo(bus)) PartStorage.String(bus,"filter_state_"+slot,"");
        PartStorage.String(bus, tags ? "tag_filters" : "filters", string.Join("\n", filters)); Visuals.Invalidate(); StructureChanged(bus);
    }
    private GameItem? WrenchItem(GameItem target)
    {
        if (target == null) return null;
        if (target.identifier == "deepspaceae.pipeline") return CableBusAt(target, UnityEngine.Input.mousePosition, true);
        if (!IoBus.IsBus(target)) return null;
        // Native hit providers may return the installed item itself. Its empty
        // upgrade inventory does not mean that the bus cannot be detached.
        if (target.parentInventory?.GetParentItem()?.identifier == "deepspaceae.pipeline") return target;
        return PartStorage.Inventory(target).childItems.LastOrDefault();
    }
    internal bool CanUseWrench(GameItem target) => WrenchItem(target) != null;
    // One diagnostic at a user gesture, never on hover and never an authorization
    // fallback. Keep native MayTarget/CanTarget decisions visible in Player.log.
    internal void TraceWrenchGesture(GameItem tool, GameItem? target, string gesture)
    {
        if (tool.identifier != PartStorage.Wrench) return;
        try
        {
            string hit = target?.identifier ?? "none";
            var selected = target == null ? null : WrenchItem(target);
            log.Log("Wrench " + gesture + ": target=" + hit + "; nativeMay=" + (target != null && tool.MayTarget(target))
                + "; nativeCan=" + (target != null && tool.CanTarget(target)) + "; selected=" + (selected?.identifier ?? "none")
                + "; toolOwned=" + GeneralHelper.IsItemEffectivelyOwned(tool) + "; targetOwned=" + (target != null && GeneralHelper.IsItemEffectivelyOwned(target))
                + "; disabled=" + tool.forceDisableUse + "; callback=" + (tool.onThisTargetItemFunc != null));
        }
        catch (Exception e) { log.Warning("Wrench " + gesture + " diagnostic: " + Diagnostics.ConsoleText(e.Message)); }
    }
    internal void UseWrench(GameItem tool, GameItem target)
    {
        log.Log("Wrench callback: target=" + target.identifier + "; writing=" + writing);
        if (writing || !GeneralHelper.IsItemEffectivelyOwned(tool) || !GeneralHelper.IsItemEffectivelyOwned(target)) return;
        var item = WrenchItem(target); var owner = item?.parentInventory?.GetParentItem(); var destination = MainInventory();
        if (item == null || owner == null || destination == null) return;
        PartStorage.WithUpgradeRemoval(owner, () => ConfigureMove(owner, item, destination));
    }
    private void ConfigureMove(GameItem part, GameItem item, GameInventory destination)
    {
        if (writing) return;
        writing = true;
        try
        {
            if (!GeneralHelper.IsItemEffectivelyOwned(part) || !GeneralHelper.IsItemEffectivelyOwned(item)) throw new Rejected(UiText.Get("error_not_owned"));
            var source = item.parentInventory ?? throw new Rejected(UiText.Get("error_position_changed"));
            int moved = NativeMove(item, source, Placement(item, new[] { destination }));
            log.Log("Wrench move: item=" + item.identifier + "; accepted=" + moved);
            if (moved == 0)
                throw new Rejected(UiText.Get("error_native_remove"));
        }
        catch (Exception e) { Error(e); }
        finally { writing = false; Visuals.Invalidate(); }
        OpenPart(part);
    }
    private SlotMarker Placement(GameItem item, IEnumerable<GameInventory> destinations)
    {
        int count = item.unitCount;
        if (!item.MayRemove() || count <= 0) throw new Rejected(UiText.Get("error_native_remove"));
        foreach (var inv in destinations)
        {
            if (ReferenceEquals(item.parentInventory, inv)) continue;
            var slot = inv.TryFindOneValidInventorySlot(item);
            if (slot != null && slot.numTransfer > 0) return slot;
        }
        throw new Rejected(UiText.Get("error_no_slot"));
    }
    private int NativeMove(GameItem item, GameInventory source, SlotMarker slot) => slot.TryAcceptOnce(item.unitCount);
    private static string Safe(string? value) => (value ?? "?").Replace("<", "〈").Replace(">", "〉");
}
