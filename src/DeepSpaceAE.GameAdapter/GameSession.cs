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
    private bool writeFault, writing, refreshQueued;
    private int page;
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
        Loading = true; Close(); uncertainItems.Clear(); NativeDrag.Gesture = null; NativeDrag.OriginItem = null; NativeDrag.OriginSource = null; Visuals.Detach(); identities = new(); observations = new(); writeFault = writing = false;
        page = 0; search = tag = message = "";
    }
    private void Unobserve()
    {
        foreach (var inv in observed) { inv.onSlotAddItemEndFunc -= Changed; inv.onSlotRemoveItemEndFunc -= Changed; inv.onSlotChangeAmountItemFunc -= Changed; }
        observed.Clear(); openTerminal = null;
    }
    internal void Close()
    {
        Unobserve(); CloseNative();
        if (CustomUIManager.Instance == null) return;
        CustomUIManager.Instance.CloseWindow(WindowId); CustomUIManager.Instance.CloseWindow("deepspaceae.part");
    }
    private void Changed(GameItem item, GameInventory inventory, SlotMarker slot)
    {
        if (refreshQueued || openTerminal == null) { RefreshPartLater(); return; }
        refreshQueued = true;
        var current = openTerminal;
        LetterboxManager.RunNextUpdate(() => { refreshQueued = false; if (ReferenceEquals(openTerminal, current) && !writing && ItemMouseDragHandler.current?.IsDraggingItem != true) Open(current); RefreshPartLater(); });
    }
    internal void ItemChanged(GameItem item)
    {
        if (item.parentInventory != null && observed.Contains(item.parentInventory)
            && observations.GetValue(item, i => new Observation(i)).Changed(item)) Changed(item, item.parentInventory, null!);
    }
    internal void StructureChanged(GameItem item)
    {
        if (item.parentInventory != null && observed.Contains(item.parentInventory)) Changed(item, item.parentInventory, null!);
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
        var root = MainInventory() ?? throw new Rejected("请先进入游戏。");
        var board = new Board { Layout = new InventoryLayout(root) };
        foreach (var part in board.Layout.Parts) board.ById.Add(Id(part), part);
        var nodes = board.Layout.Parts.Select(i => new NetworkNode(Id(i), ModEntry.PartKinds[i.identifier],
            i.identifier == "deepspaceae.item_storage_bus" ? Id(i) : null));
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
                    item.name = definition.Name;
                    item.shortDescription = definition.Part == NetworkPart.ItemStorageBus ? "箭头面接容器，背面接线。双击设置优先级与筛选。" : "放置于主物品栏接入网络。双击打开。";
                    item.flavorText = "深空中的应用能源。";
                }
                if (IsPart(item) && !ReferenceEquals(item.parentInventory, MainInventory())) Visuals.Inactive(item);
                if (item.identifier == "deepspaceae.core") PartStorage.RestoreLocks(item);
            }
        Visuals.Invalidate();
    }
    private static IEnumerable<GameItem> AllCores() => PlayerStore.Instance == null ? Array.Empty<GameItem>()
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
    private sealed class View
    {
        internal Board Board = null!;
        internal GameGridInventory Root = null!;
        internal GameItem Core = null!;
        internal Dictionary<GameItem, GameItem> Owners = new();
        internal Dictionary<GameItem, StoragePolicy> Policies = new();
        internal Dictionary<GameItem, List<GameInventory>> Inventories = new();
        internal Dictionary<Guid, GameItem> Live = new();
        internal Dictionary<Guid, GameInventory> Sources = new();
        internal CatalogSearch Search = null!;

    }
    private View Read(GameItem terminal)
    {
        var board = ReadBoard(); var root = MainInventory()!;
        if (!ReferenceEquals(terminal.parentInventory, root) || !GeneralHelper.IsItemEffectivelyOwned(terminal)) throw new Rejected("终端未激活：请直接放入主物品栏。");
        if (!board.Evaluation.Devices.TryGetValue(Id(terminal), out var connection) || connection.Status != DeviceStatus.Online || connection.CoreId == null)
            throw new Rejected("终端未激活：检查核心、线缆和频道。每个网络只能有一个核心。");
        if (!board.Powered.Contains(connection.CoreId)) throw new Rejected("网络未供电：在核心中安装足够供整晚使用的电池。");
        var view = new View { Root = root, Board = board, Core = board.ById[connection.CoreId] };
        var ownerPolicies = new Dictionary<GameItem, List<StoragePolicy>>();
        foreach (var bus in board.Layout.Parts.Where(p => p.identifier == "deepspaceae.item_storage_bus"))
        {
            var bc = board.Evaluation.Devices[Id(bus)];
            if (bc.Status != DeviceStatus.Online || bc.CoreId != connection.CoreId) continue;
            var owner = board.Layout.FrontOwner(bus);
            if (owner == null || IsPart(owner) || !board.Layout.Owned.Contains(owner)) continue;
            if (!view.Inventories.TryGetValue(owner, out var invs)) { invs = Contents(owner); view.Inventories.Add(owner, invs); }
            if (invs.Count == 0) continue;
            var policy = PartStorage.Policy(bus); view.Owners.Add(bus, owner); view.Policies.Add(bus, policy);
            if (!ownerPolicies.TryGetValue(owner, out var ps)) ownerPolicies.Add(owner, ps = new List<StoragePolicy>());
            ps.Add(policy);
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
        ShowNotice(e is Rejected ? Safe(e.Message) : "操作未完成，请保留游戏日志并检查网络状态。");
    }
    internal void OpenPart(GameItem part)
    {
        if (writing) return;
        try
        {
            if (part.identifier == "deepspaceae.terminal") { Open(part); return; }
            if (!ReferenceEquals(part.parentInventory, MainInventory()) || !GeneralHelper.IsItemEffectivelyOwned(part)) throw new Rejected("组件未激活：请直接放入主物品栏。");
            if (part.identifier == "deepspaceae.core") ShowCore(part);
            else if (part.identifier == "deepspaceae.item_storage_bus") ShowBus(part);
            else ShowCable(part);
        }
        catch (Exception e) { Error(e); }
    }
    internal void Open(GameItem terminal)
    {
        if (writing) return;
        try
        {
            var view = Read(terminal);
            if (!PartStorage.Powered(view.Core, view.Board.Evaluation.NightCosts[Id(view.Core)], true)) throw new Rejected("电池无法预留整晚用电。");
            Show(terminal, view);
        }
        catch (Exception e) { CloseTerminal(); CustomUIManager.Instance?.CloseWindow(WindowId); Error(e); }
    }
    private void ConfigureNumber(GameItem bus, string key, int value)
    {
        if (!ReferenceEquals(bus.parentInventory, MainInventory()) || !GeneralHelper.IsItemEffectivelyOwned(bus)) return;
        PartStorage.Number(bus, key, value); Visuals.Invalidate(); StructureChanged(bus);
    }
    private void SetFilter(GameItem bus, int slot, string value)
    {
        if (!ReferenceEquals(bus.parentInventory, MainInventory()) || !GeneralHelper.IsItemEffectivelyOwned(bus)) return;
        var filters = PartStorage.Filters(bus); filters[slot] = value.Replace("\n", "").Replace("\r", "").Trim();
        PartStorage.String(bus, PartStorage.Policy(bus).Tags ? "tag_filters" : "filters", string.Join("\n", filters)); Visuals.Invalidate(); StructureChanged(bus);
    }
    internal void UseWrench(GameItem tool, GameItem bus)
    {
        if (writing || !GeneralHelper.IsItemEffectivelyOwned(tool) || !GeneralHelper.IsItemEffectivelyOwned(bus) || bus.identifier != "deepspaceae.item_storage_bus") return;
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
            if (writeFault) throw new Rejected("操作已锁定。");
            if (!GeneralHelper.IsItemEffectivelyOwned(part) || !GeneralHelper.IsItemEffectivelyOwned(item)) throw new Rejected("物品不属于你。");
            var source = item.parentInventory ?? throw new Rejected("物品位置已变更。");
            if (ReferenceEquals(destination, PartStorage.Inventory(part)) && !ReferenceEquals(source, MainInventory())) throw new Rejected("请从主物品栏安装。");
            NativeMove(item, source, Placement(item, new[] { destination }));
        }
        catch (Exception e) { Error(e); }
        finally { writing = false; Visuals.Invalidate(); }
        OpenPart(part);
    }
    private SlotMarker Placement(GameItem item, IEnumerable<GameInventory> destinations)
    {
        int count = item.unitCount;
        if (!item.MayRemove() || count <= 0 || item.MaxNumRemove() < count) throw new Rejected("物品被锁定或原版规则禁止移动。");
        foreach (var inv in destinations)
        {
            if (ReferenceEquals(item.parentInventory, inv) || inv.IsInsertLocked() || GraphUtils.FindParentEquals(inv, item)) continue;
            var slot = inv.TryFindOneValidInventorySlot(item);
            if (slot != null && ReferenceEquals(slot.item, item) && ReferenceEquals(slot.inventory, inv) && slot.targetItem == null && slot.itemGridShape != null && slot.numTransfer >= count) return slot;
        }
        throw new Rejected("目标没有可接收整个物品的空位。");
    }
    private void NativeMove(GameItem item, GameInventory source, SlotMarker slot)
    {
        int count = item.unitCount;
        if (!ReferenceEquals(item.parentInventory, source) || !source.childItems.Any(i => ReferenceEquals(i, item)) || !item.MayRemove() || item.MaxNumRemove() < count)
            throw new Rejected("物品位置或锁定状态已变更，请刷新。");
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
