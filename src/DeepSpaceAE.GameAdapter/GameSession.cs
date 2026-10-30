using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DeepSpaceAE.Core;
using DeepSpaceAE.Core.Network;
using UnityEngine;

namespace DeepSpaceAE.GameAdapter;

internal sealed class GameSession
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
    private int page, carryPage, filterPage;
    private CatalogSort sort;
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
        Loading = true; Close(); Visuals.Detach(); identities = new(); observations = new(); writeFault = writing = false;
        page = carryPage = filterPage = 0; search = tag = message = "";
    }
    private void Unobserve()
    {
        foreach (var inv in observed) { inv.onSlotAddItemEndFunc -= Changed; inv.onSlotRemoveItemEndFunc -= Changed; inv.onSlotChangeAmountItemFunc -= Changed; }
        observed.Clear(); openTerminal = null;
    }
    internal void Close()
    {
        Unobserve();
        if (CustomUIManager.Instance == null) return;
        CustomUIManager.Instance.CloseWindow(WindowId); CustomUIManager.Instance.CloseWindow("deepspaceae.part");
    }
    private void Changed(GameItem item, GameInventory inventory, SlotMarker slot)
    {
        if (refreshQueued || openTerminal == null) return;
        refreshQueued = true;
        var current = openTerminal;
        LetterboxManager.RunNextUpdate(() => { refreshQueued = false; if (ReferenceEquals(openTerminal, current) && !writing) Open(current); });
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
        if (!ReferenceEquals(core.parentInventory, MainInventory())) return 8;
        var board = ReadBoard();
        return board.Evaluation.NightCosts.TryGetValue(Id(core), out var cost) ? cost : 8;
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
        internal GameItem[] Carried = null!;
        internal CatalogSearch Search = null!;
        internal Dictionary<GameItem, Sprite?> Sprites = new();
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
                records.Add(new CatalogRecord(id, item.identifier, item.name ?? "?", itemTags, item.unitCount));
            }
        }
        view.Search = new CatalogSearch(records);
        view.Carried = board.Layout.Items.Where(i => !IsPart(i) && !view.Inventories.ContainsKey(i) && GeneralHelper.IsItemEffectivelyOwned(i)).ToArray();
        return view;
    }
    private void Error(Exception e)
    {
        log.Warning(Diagnostics.ConsoleText(e.ToString()));
        CustomUIManager.Window("deepspaceae.part", "DeepSpaceAE", CustomUIManager.LAYER_PAPER)
            .SetSize(520, 230).SetDraggable(true).SetCloseOnEscape(true).AddLabel(e is Rejected ? Safe(e.Message) : "操作未完成，请保留游戏日志并检查网络状态。").Show();
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
        catch (Exception e) { Unobserve(); CustomUIManager.Instance?.CloseWindow(WindowId); Error(e); }
    }
    private void Show(GameItem terminal, View view)
    {
        Unobserve();
        var b = CustomUIManager.Window(WindowId, "DeepSpaceAE · 存储终端", CustomUIManager.LAYER_PAPER)
            .SetSize(780, 760).SetDraggable(true).SetCloseOnEscape(true).OnClose(Unobserve);
        openTerminal = terminal;
        foreach (var inv in view.Inventories.Values.SelectMany(i => i).Append(view.Root).Distinct())
        { foreach (var item in inv.childItems) observations.GetValue(item, i => new Observation(i)); observed.Add(inv); inv.onSlotAddItemEndFunc += Changed; inv.onSlotRemoveItemEndFunc += Changed; inv.onSlotChangeAmountItemFunc += Changed; }
        Action render = () => { };
        b.BeginGrid(2, 340, 32).AddInput("搜索名称、ID；#标签", search, value => { search = value; page = 0; render(); }, onTyping: value => { search = value; page = 0; render(); });
        var tags = new[] { "全部标签" }.Concat(view.Search.Tags).ToArray();
        int selected = Array.IndexOf(tags, tag); if (selected < 0) tag = "";
        b.AddDropdown(tags, Math.Max(0, selected), index => { tag = index == 0 ? "" : tags[index]; page = 0; render(); }).End();
        b.BeginGrid(2, 340, 32).AddDropdown(new[] { "原始顺序", "按名称", "按数量" }, (int)sort,
            i => { sort = (CatalogSort)i; page = 0; render(); }).AddButton("刷新", () => Open(terminal)).End();
        b.BeginScroll(550); var body = b.CurrentContainer; b.End();
        render = () => {
            CustomUIBuilder.ClearContainer(body);
            var grid = new CustomUIBuilder(CustomUIManager.Instance, b.Handle, body);
            if (!string.IsNullOrEmpty(message)) grid.AddLabel(Safe(message));
            if (writeFault) grid.AddWarning("物品移动结果异常，操作已锁定。请保存日志并停止取放。");
            var rows = view.Search.Find(search, string.IsNullOrEmpty(tag) ? null : tag, sort);
            page = Math.Max(0, Math.Min(page, Math.Max(0, (rows.Count - 1) / 32)));
            grid.AddLabel($"网络库存 · {rows.Count} 个实例 · 第 {page + 1} 页").BeginGrid(8, 84, 68);
            foreach (var row in rows.Skip(page * 32).Take(32))
            {
                var item = view.Live[row.Instance]; var source = view.Sources[row.Instance];
                ItemCell(grid, item, SpriteFor(view, item), () => Transfer(terminal, item, source, false));
            }
            grid.End().BeginGrid(2, 340, 32).AddButton("上一页", () => { page--; render(); }).AddButton("下一页", () => { if ((page + 1) * 32 < rows.Count) page++; render(); }).End();
            carryPage = Math.Max(0, Math.Min(carryPage, Math.Max(0, (view.Carried.Length - 1) / 16)));
            grid.AddLabel("主物品栏 · 点击存入网络（按总线优先级）").BeginGrid(8, 84, 68);
            foreach (var item in view.Carried.Skip(carryPage * 16).Take(16)) { var captured = item; ItemCell(grid, captured, SpriteFor(view, captured), () => Transfer(terminal, captured, view.Root, true)); }
            grid.End().BeginGrid(2, 340, 32).AddButton("主栏上一页", () => { carryPage--; render(); }).AddButton("主栏下一页", () => { if ((carryPage + 1) * 16 < view.Carried.Length) carryPage++; render(); }).End();
            CustomUIBuilder.RebuildLayout(body);
        };
        render(); b.Show();
    }
    private static Sprite? SpriteFor(View view, GameItem item)
    {
        if (!view.Sprites.TryGetValue(item, out var sprite))
        { sprite = (item as GameItemElement)?.ResolveSpriteByName(item.spritePath); view.Sprites.Add(item, sprite); }
        return sprite;
    }
    private static void ItemCell(CustomUIBuilder b, GameItem item, Sprite? sprite, Action action)
    {
        b.BeginGrid(1, 80, 30);
        string detail = Safe(item.name) + "\n" + Safe(item.identifier) + " ×" + item.unitCount;
        if (item.IsTag(PowerHelper.POWER_SOURCE_ITEM_TAG)) detail += "\n电量 " + PowerHelper.GetAvailableEnergyFromItem(item, false) + " kJ";
        if (sprite != null) b.AddIconButton(sprite, 32, action).WithTooltip(detail);
        b.AddButton(Safe(item.name) + " ×" + item.unitCount, action).WithTooltip(detail).End();
    }
    private void ShowCore(GameItem core)
    {
        var board = ReadBoard(); var lease = PartStorage.Lease(core); var battery = PartStorage.Battery(core);
        int cost = board.Evaluation.NightCosts.TryGetValue(Id(core), out var n) ? n : 8;
        var members = board.Evaluation.ComponentCore.Where(p => p.Value == Id(core)).Select(p => board.ById[p.Key]).ToArray();
        var b = CustomUIManager.Window("deepspaceae.part", "DeepSpaceAE · 网络控制器", CustomUIManager.LAYER_PAPER)
            .SetSize(650, 620).SetDraggable(true).SetCloseOnEscape(true)
            .AddHeader(board.Powered.Contains(Id(core)) ? "网络在线" : "网络未激活")
            .AddLabel($"每晚用电 {cost} kJ · 本日预留 {((lease.Phase == NightPhase.Reserved) ? lease.Required : 0)} kJ")
            .AddLabel("频道 " + (board.Evaluation.ChannelsUsed.TryGetValue(Id(core), out var used) ? used : 0) + " / 32；线缆上限 8")
            .AddLabel(battery == null ? "电池槽：空" : "电池：" + Safe(battery.name) + (lease.Phase == NightPhase.Fault || lease.Phase == NightPhase.DebitStarted ? " · 上次结算未确认，已锁定" : lease.CanRemove(PartStorage.Day) ? " · 可更换" : " · 已锁定，夜间结算后次日可更换"));
        foreach (NetworkPart part in Enum.GetValues(typeof(NetworkPart)))
        { int count = members.Count(i => ModEntry.PartKinds[i.identifier] == part); b.AddLabel(PartName(part) + $"：{count} × {NetworkEvaluation.NightCost(part)} kJ"); }
        b.BeginScroll(280);
        if (battery != null) b.AddLabel(HypertechBatteryHelper.IsHypertechBattery(battery) ? "超科技电池：沿用原生输出上限" : "剩余电量：" + PowerHelper.GetAvailableEnergyFromItem(battery, false) + " kJ");
        if (battery == null) b.AddLabel("仅显示足够支持整晚运行的电池。");
        if (battery == null)
            foreach (var candidate in MainInventory()!.childItems.Where(i => PartStorage.CanInstall(core, i)))
            { var captured = candidate; b.AddButton("安装 " + Safe(captured.name), () => ConfigureMove(core, captured, PartStorage.Inventory(core))); }
        else if (lease.CanRemove(PartStorage.Day)) b.AddButton("取出电池", () => ConfigureMove(core, battery, MainInventory()!));
        b.End().Show();
    }
    private static string PartName(NetworkPart p) => p == NetworkPart.Core ? "核心" : p == NetworkPart.Pipeline ? "线缆" : p == NetworkPart.Terminal ? "终端" : "存储总线";
    private void ShowCable(GameItem cable)
    {
        var board = ReadBoard();
        bool online = board.Evaluation.ComponentCore.TryGetValue(Id(cable), out var core) && board.Powered.Contains(core);
        CustomUIManager.Window("deepspaceae.part", "AE 线缆", CustomUIManager.LAYER_PAPER).SetSize(400, 220).SetDraggable(true).SetCloseOnEscape(true)
            .AddLabel(online ? "在线" : "未激活").AddLabel("已用频道：" + board.Evaluation.ChannelsUsed[Id(cable)] + " / 8").AddLabel("每晚耗电：1 kJ").Show();
    }
    private void ShowBus(GameItem bus)
    {
        var policy = PartStorage.Policy(bus); var filters = PartStorage.Filters(bus); int slot = filterPage;
        slot = Math.Max(0, Math.Min(slot, policy.Slots - 1)); filterPage = slot;
        var b = CustomUIManager.Window("deepspaceae.part", "DeepSpaceAE · 存储总线", CustomUIManager.LAYER_PAPER)
            .SetSize(700, 780).SetDraggable(true).SetCloseOnEscape(true).BeginScroll(680)
            .AddInput("优先级（整数，越大越优先）", policy.Priority.ToString(), value => { if (int.TryParse(value, out var priority)) ConfigureNumber(bus, "priority", priority); })
            .AddDropdown(new[] { "存入与取出", "仅取出", "仅存入" }, (int)policy.Access, i => { ConfigureNumber(bus, "access", i); OpenPart(bus); })
            .AddToggle("排除匹配项", policy.Exclude, value => { ConfigureNumber(bus, "exclude", value ? 1 : 0); OpenPart(bus); })
            .AddToggle("筛选也作用于取出", policy.FilterExtraction, value => { ConfigureNumber(bus, "extract_filter", value ? 1 : 0); OpenPart(bus); })
            .AddLabel($"{(policy.Tags ? "标签" : "物品")}筛选 · {policy.Slots} 槽 · 空筛选接收全部 · 第 {slot + 1} 槽")
            .AddInput(policy.Tags ? "输入真实标签，或从下方选择" : "输入物品 ID，或点击主栏物品复制类型", filters[slot], value => SetFilter(bus, slot, value))
            .BeginGrid(2, 340, 32).AddButton("前一槽", () => { filterPage = Math.Max(0, slot - 1); OpenPart(bus); })
            .AddButton("清除此槽", () => { SetFilter(bus, slot, ""); OpenPart(bus); })
            .AddButton("后一槽", () => { filterPage = Math.Min(policy.Slots - 1, slot + 1); OpenPart(bus); }).End()
            .BeginGrid(9, 65, 32);
        for (int i = 0; i < policy.Slots; i++)
        {
            int index = i;
            b.AddButton((i == slot ? "▶" : "") + (i + 1) + ":" + Safe(filters[i]), () => { filterPage = index; OpenPart(bus); });
        }
        b.End().BeginScroll(180);
        if (policy.Tags)
        {
            var options = MainInventory()!.childItems.SelectMany(PartStorage.Tags).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray();
            b.AddDropdown(new[] { "从主栏物品标签选择" }.Concat(options).ToArray(), 0, i => { if (i > 0) { SetFilter(bus, slot, options[i - 1]); OpenPart(bus); } });
        }
        else foreach (var item in MainInventory()!.childItems.Where(i => !IsPart(i)))
        { var captured = item; b.AddButton(Safe(item.name), () => { SetFilter(bus, slot, captured.identifier); OpenPart(bus); }); }
        b.End().AddHeader("升级卡 " + PartStorage.Inventory(bus).childItems.Count + " / 5");
        foreach (var card in PartStorage.Inventory(bus).childItems) b.AddLabel(Safe(card.name));
        foreach (var card in MainInventory()!.childItems.Where(i => i.identifier == PartStorage.Capacity || i.identifier == PartStorage.Tag))
        { var captured = card; b.AddButton("安装 " + Safe(card.name), () => ConfigureMove(bus, captured, PartStorage.Inventory(bus))); }
        b.AddLabel("使用 AE 扳手拆卸升级卡。").End().Show();
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
        catch { writeFault = true; throw; }
    }
    private void Transfer(GameItem terminal, GameItem item, GameInventory expectedSource, bool deposit)
    {
        if (writing) return; writing = true;
        try
        {
            if (writeFault) throw new Rejected("物品移动已锁定。");
            var view = Read(terminal);
            if (!ReferenceEquals(item.parentInventory, expectedSource) || !GeneralHelper.IsItemEffectivelyOwned(item)) throw new Rejected("物品位置已变更，请刷新。");
            GameItem? targetBus = null, expectedOwner = null;
            SlotMarker placement;
            if (deposit)
            {
                if (!ReferenceEquals(expectedSource, view.Root) || !view.Carried.Contains(item)) throw new Rejected("该物品无法存入。");
                var tags = PartStorage.Tags(item); SlotMarker? found = null;
                var policies = view.Policies.ToDictionary(p => Id(p.Key), p => p.Value, StringComparer.Ordinal);
                foreach (var busId in StorageRouting.InsertionOrder(policies, item.identifier, tags))
                {
                    var candidateBus = view.Board.ById[busId];
                    try { found = Placement(item, view.Inventories[view.Owners[candidateBus]]); }
                    catch (Rejected) { continue; } // Preflight only: no native mutation occurred.
                    targetBus = candidateBus; expectedOwner = view.Owners[candidateBus]; break;
                }
                placement = found ?? throw new Rejected("网络中没有符合筛选且有空位的容器。");
            }
            else
            {
                if (!view.Live.ContainsKey(GuidFor(item))) throw new Rejected("物品已不在可访问的网络中。");
                placement = Placement(item, new[] { view.Root });
            }
            var final = Read(terminal);
            if (!ReferenceEquals(view.Core, final.Core) || !ReferenceEquals(view.Root, final.Root)) throw new Rejected("网络已变更，请刷新。");
            if (targetBus != null)
            {
                if (!final.Owners.TryGetValue(targetBus, out var owner) || !ReferenceEquals(owner, expectedOwner)
                    || !final.Policies[targetBus].CanInsert(item.identifier, PartStorage.Tags(item)) || !final.Inventories[owner].Contains(placement.inventory))
                    throw new Rejected("总线目标或筛选已变更，请刷新。");
            }
            else if (!final.Live.ContainsKey(GuidFor(item))) throw new Rejected("物品已不可访问，请刷新。");
            if (!PartStorage.Powered(final.Core, final.Board.Evaluation.NightCosts[Id(final.Core)], true)) throw new Rejected("电池不足以支持整晚运行。");
            NativeMove(item, expectedSource, placement); message = "已" + (deposit ? "存入 " : "取出 ") + Safe(item.name) + " ×" + item.unitCount;
        }
        catch (Exception e) { message = e is Rejected ? e.Message : "操作发生异常，取放已锁定。"; log.Warning(Diagnostics.ConsoleText(e.ToString())); }
        finally { writing = false; Visuals.Invalidate(); }
        Open(terminal);
    }
    private static string Safe(string? value) => (value ?? "?").Replace("<", "〈").Replace(">", "〉");
}
