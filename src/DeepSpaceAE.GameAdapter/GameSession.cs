using System;
using System.Collections.Generic;
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
        internal string Text { get; }
        internal Token() => Text = Id.ToString("N");
    }
    private ConditionalWeakTable<GameItem, Token> identities = new();
    private sealed class Observation
    {
        private int count;
        private string? name, sprite;
        private readonly HashSet<string> enabled = new(StringComparer.Ordinal);
        internal Observation(GameItem item) => Capture(item);
        private void Capture(GameItem item)
        {
            count = item.unitCount; name = item.name; sprite = item.spritePath; enabled.Clear();
            foreach (var pair in item.modifiedState.dict) if (pair.Value.valueEnabled) enabled.Add(pair.Key);
        }
        internal bool Changed(GameItem item)
        {
            bool different = count != item.unitCount || name != item.name || sprite != item.spritePath;
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
    private bool writeFault, writing, refreshQueued, terminalDirty;
    private int terminalGeneration;
    private CatalogSort sort = CatalogSort.Name;
    private string search = "", tag = "", message = "";
    private GameItem? openTerminal;
    private readonly HashSet<GameInventory> observed = new();
    private sealed class Rejected : Exception { internal Rejected(string text) : base(text) { } }
    internal CableVisuals Visuals { get; }
    internal GameSession(ModLog log) { this.log = log; Visuals = new CableVisuals(log, this); }
    internal static GameGridInventory? MainInventory() => EmporiumEntry.Instance?.invElement;
    internal string Id(GameItem item) => identities.GetValue(item, _ => new Token()).Text;
    internal Guid GuidFor(GameItem item) => identities.GetValue(item, _ => new Token()).Id;
    internal static bool IsPart(GameItem item) => item.identifier != null && ModEntry.PartKinds.ContainsKey(item.identifier);
    internal void Reset()
    {
        PartStorage.ResetPowerSettlement();
        Loading = true; Close(); uncertainItems.Clear(); NativeDrag.Gesture = null; NativeDrag.OriginItem = null; NativeDrag.OriginSource = null; Visuals.Detach(); identities = new(); observations = new(); writeFault = writing = false;
        search = tag = message = "";
    }
    private void Unobserve()
    {
        foreach (var inv in observed) { inv.onSlotAddItemEndFunc -= Changed; inv.onSlotRemoveItemEndFunc -= Changed; inv.onSlotChangeAmountItemFunc -= Changed; }
        observed.Clear(); openTerminal = null; terminalDirty = false; refreshQueued = false; terminalGeneration++;
    }
    internal void Close()
    {
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
    internal void DragEnded() { terminalGrid?.RefreshVisible(); ScheduleTerminalRefresh(); RefreshPartLater(); }
    internal void ItemChanged(GameItem item)
    {
        if (item.parentInventory != null && observed.Contains(item.parentInventory)
            && observations.GetValue(item, i => new Observation(i)).Changed(item)) Changed(item, item.parentInventory, null!);
    }
    internal void StructureChanged(GameItem item)
    {
        var host = PartStorage.BusHost(item);
        if (host?.parentInventory != null && observed.Contains(host.parentInventory)) Changed(host, host.parentInventory, null!);
        else if (item.parentInventory != null && observed.Contains(item.parentInventory)) Changed(item, item.parentInventory, null!);
    }
    internal static List<GameInventory> Contents(GameItem owner)
    {
        var result = new List<GameInventory>();
        if (owner.child == null) return result;
        var visited = new HashSet<GraphNodeStorage>(); var queue = new Queue<GraphNodeStorage>(); queue.Enqueue(owner.child);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue(); if (!visited.Add(node)) continue;
            if (visited.Count > 2048) throw new InvalidOperationException("Container structure exceeds the observation limit.");
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
        foreach (var pair in board.Evaluation.NightCosts)
            if (PartStorage.Powered(board.ById[pair.Key], pair.Value, reserve)) board.Powered.Add(pair.Key);
        return board;
    }
    internal int CoreCost(GameItem core)
    {
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
                if (IsPart(item))
                {
                    var definition = ModEntry.Parts.First(p => p.Id == item.identifier);
                    item.name = UiText.Get(definition.NameKey);
                    item.shortDescription = definition.Part == NetworkPart.ItemStorageBus ? UiText.Get("item_bus_desc") : definition.Part == NetworkPart.Pipeline ? UiText.Get("item_cable_desc") : UiText.Get("item_part_desc");
                    item.flavorText = UiText.Get("item_flavor");
                }
                if (IsPart(item) && !ReferenceEquals(item.parentInventory, MainInventory())) Visuals.Inactive(item);
                if (item.identifier == "deepspaceae.core") PartStorage.RestoreLocks(item);
            }
        Visuals.Invalidate();
    }
    internal static IEnumerable<GameItem> AllCores() => PlayerStore.Instance == null ? Array.Empty<GameItem>()
        : PlayerStore.Instance.FindAllItem(false).Where(i => i.identifier == "deepspaceae.core");
    internal void Night()
    {
        Close();
        Board? board = null;
        try { if (MainInventory() != null) board = ReadBoard(true); }
        catch (Exception e) { log.Error(Diagnostics.ConsoleText(e.ToString())); }
        foreach (var core in AllCores())
            try
            {
                int cost = board != null && board.Evaluation.NightCosts.TryGetValue(Id(core), out var n) ? n : 0;
                PartStorage.Settle(core, cost, log);
            }
            catch (Exception e) { log.Error(Diagnostics.ConsoleText(e.ToString())); }
        Visuals.Invalidate();
    }
    internal void NightLate()
    {
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
        internal CatalogSearch Search = null!;

    }
    private View Read(GameItem terminal)
    {
        var board = ReadBoard(); var root = MainInventory()!;
        if (!ReferenceEquals(terminal.parentInventory, root) || !GeneralHelper.IsItemEffectivelyOwned(terminal)) throw new Rejected(UiText.Get("error_terminal_main"));
        if (!board.Evaluation.Devices.TryGetValue(Id(terminal), out var connection) || connection.Status != DeviceStatus.Online || connection.CoreId == null)
            throw new Rejected(UiText.Get("error_terminal_network"));
        if (!board.Powered.Contains(connection.CoreId)) throw new Rejected(UiText.Get("error_network_power"));
        var view = new View { Root = root, Board = board, Core = board.ById[connection.CoreId] };
        var ownerPolicies = new Dictionary<GameItem, List<StoragePolicy>>();
        foreach (var bus in board.Layout.Parts.Where(p => p.identifier == "deepspaceae.item_storage_bus"))
        {
            var bc = board.Evaluation.Devices[Id(bus)];
            if (bc.Status != DeviceStatus.Online || bc.CoreId != connection.CoreId) continue;
            var owner = board.Layout.FrontOwner(bus);
            if (owner == null || IsPart(owner) || DiskStorage.IsDisk(owner) || !board.Layout.Owned.Contains(owner)) continue;
            if (!view.Inventories.TryGetValue(owner, out var invs)) { invs = Contents(owner); view.Inventories.Add(owner, invs); }
            if (invs.Count == 0) continue;
            var policy = PartStorage.Policy(bus); view.Owners.Add(bus, owner); view.Policies.Add(bus, policy);
            if (!ownerPolicies.TryGetValue(owner, out var ps)) ownerPolicies.Add(owner, ps = new List<StoragePolicy>());
            ps.Add(policy);
        }
        foreach(var drive in board.Layout.Parts.Where(DiskStorage.IsDrive))
        {
            if(!board.Evaluation.Devices.TryGetValue(Id(drive),out var dc) || dc.Status!=DeviceStatus.Online || dc.CoreId!=connection.CoreId) continue;
            var bays=DiskStorage.Bays(drive); view.Attachments.AddRange(bays);
            var invs=bays.Where(b=>b.childItem!=null).Select(b=>b.childItem).Where(DiskStorage.IsDisk)
                .Select(d=>DiskStorage.Data(d)).Cast<GameInventory>().ToList();
            view.Inventories.Add(drive,invs); view.Owners.Add(drive,drive);
            var policy=new StoragePolicy(0,StorageAccess.ReadWrite,false,false,false,0,Array.Empty<string>());
            view.Policies.Add(drive,policy); ownerPolicies.Add(drive,new List<StoragePolicy>{policy});
        }
        var records = new List<CatalogRecord>();
        foreach (var pair in ownerPolicies)
        {
            var filter = new ExtractionFilter(pair.Value);
            foreach (var inventory in view.Inventories[pair.Key]) foreach (var item in inventory.childItems)
            {
                if (!ReferenceEquals(item.parentInventory, inventory)) throw new InvalidOperationException("Inconsistent container observation.");
                var itemTags = PartStorage.Tags(item);
                if (!filter.Allows(item.identifier, itemTags)) continue;
                var id = GuidFor(item); view.Live.Add(id, item); view.Sources.Add(id, inventory);
                var publicTags = item.GetGameItemType();
                records.Add(new CatalogRecord(id, item.identifier, item.name ?? "?", publicTags, item.unitCount, publicTags.Select(NativeWidgets.TagName)));
            }
        }
        view.Search = new CatalogSearch(records);
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
            if (part.identifier == "deepspaceae.item_storage_bus")
            {
                if (PartStorage.CanConfigure(part)) ShowBus(part); else ShowNotice(UiText.Get("ui_install_bus_first"));
                return;
            }
            if (!ReferenceEquals(part.parentInventory, MainInventory()) || !GeneralHelper.IsItemEffectivelyOwned(part)) throw new Rejected(UiText.Get("error_part_main"));
            if (DiskStorage.IsDrive(part)) ShowDrive(part);
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
            if (!PartStorage.Powered(view.Core, view.Board.Evaluation.NightCosts[Id(view.Core)], true)) throw new Rejected(UiText.Get("error_reserve"));
            Show(terminal, view);
        }
        catch (Exception e) { CloseTerminal(); CustomUIManager.Instance?.CloseWindow(WindowId); Error(e); }
    }
    private void ConfigureNumber(GameItem bus, string key, int value)
    {
        if (!PartStorage.CanConfigure(bus)) return;
        PartStorage.Number(bus, key, value); Visuals.Invalidate(); StructureChanged(bus);
    }
    private void SetFilter(GameItem bus, int slot, string value)
    {
        if (!PartStorage.CanConfigure(bus)) return;
        var filters = PartStorage.Filters(bus); filters[slot] = value.Replace("\n", "").Replace("\r", "").Trim();
        PartStorage.String(bus, PartStorage.Policy(bus).Tags ? "tag_filters" : "filters", string.Join("\n", filters)); Visuals.Invalidate(); StructureChanged(bus);
    }
    internal void UseWrench(GameItem tool, GameItem bus)
    {
        if (writing || !GeneralHelper.IsItemEffectivelyOwned(tool) || !GeneralHelper.IsItemEffectivelyOwned(bus)) return;
        if (bus.identifier == "deepspaceae.pipeline")
        {
            var selected = CableBusAt(bus, UnityEngine.Input.mousePosition);
            if (selected != null) PartStorage.WithUpgradeRemoval(bus, () => ConfigureMove(bus, selected, MainInventory()!));
            return;
        }
        if (bus.identifier != "deepspaceae.item_storage_bus") return;
        var card = PartStorage.Inventory(bus).childItems.LastOrDefault();
        if (card == null) return;
        PartStorage.WithUpgradeRemoval(bus, () => ConfigureMove(bus, card, MainInventory()!));
    }
    private void ConfigureMove(GameItem part, GameItem item, GameInventory destination)
    {
        if (writing) return;
        writing = true;
        try
        {
            if (writeFault) throw new Rejected(UiText.Get("error_locked"));
            if (!GeneralHelper.IsItemEffectivelyOwned(part) || !GeneralHelper.IsItemEffectivelyOwned(item)) throw new Rejected(UiText.Get("error_not_owned"));
            var source = item.parentInventory ?? throw new Rejected(UiText.Get("error_position_changed"));
            if (ReferenceEquals(destination, PartStorage.Inventory(part)) && !ReferenceEquals(source, MainInventory())) throw new Rejected(UiText.Get("error_install_main"));
            NativeMove(item, source, Placement(item, new[] { destination }));
        }
        catch (Exception e) { Error(e); }
        finally { writing = false; Visuals.Invalidate(); }
        OpenPart(part);
    }
    private SlotMarker Placement(GameItem item, IEnumerable<GameInventory> destinations)
    {
        int count = item.unitCount;
        if (!item.MayRemove() || count <= 0 || item.MaxNumRemove() < count) throw new Rejected(UiText.Get("error_native_remove"));
        foreach (var inv in destinations)
        {
            if (ReferenceEquals(item.parentInventory, inv) || inv.IsInsertLocked() || GraphUtils.FindParentEquals(inv, item)) continue;
            var slot = inv.TryFindOneValidInventorySlot(item);
            if (slot != null && ReferenceEquals(slot.item, item) && ReferenceEquals(slot.inventory, inv) && slot.targetItem == null && slot.itemGridShape != null && slot.numTransfer >= count) return slot;
        }
        throw new Rejected(UiText.Get("error_no_slot"));
    }
    private void NativeMove(GameItem item, GameInventory source, SlotMarker slot)
    {
        int count = item.unitCount;
        if (!ReferenceEquals(item.parentInventory, source) || !source.childItems.Any(i => ReferenceEquals(i, item)) || !item.MayRemove() || item.MaxNumRemove() < count)
            throw new Rejected(UiText.Get("error_source_changed"));
        try
        {
            int moved = slot.TryAcceptOnce(count);
            if (moved != count || item.unitCount != count || !ReferenceEquals(item.parentInventory, slot.inventory)
                || !slot.inventory.childItems.Any(i => ReferenceEquals(i, item)) || source.childItems.Any(i => ReferenceEquals(i, item)))
                throw new InvalidOperationException("Native movement result unknown; operations locked. No retry or rollback.");
        }
        catch { writeFault = true; if (item.parentInventory == null) uncertainItems.Add(item); throw; }
    }
    private static string Safe(string? value) => (value ?? "?").Replace("<", "〈").Replace(">", "〉");
}
