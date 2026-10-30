using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DeepSpaceAE.Core;
using DeepSpaceAE.Core.Network;

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
    private readonly ModLog log;
    private bool channels = true;
    private bool writesEnabled;
    private bool writeFault;
    private bool writing;
    private int page;
    private int depositPage;
    private int busPage;
    private GameItem? depositTarget;
    private sealed class MoveRejectedException : Exception { internal MoveRejectedException(string message) : base(message) { } }
    private string message = "只读模式；请用独立测试存档。";

    internal CableVisuals Visuals { get; }
    internal GameSession(ModLog log) { this.log = log; Visuals = new CableVisuals(log); }
    // 100-REV3 attaches this inventory to invWindow and wires its MainInvArea callbacks.
    // PlayerStore.GetStoreInventory() returns a different, unattached inventory.
    internal static GameGridInventory? MainInventory() => EmporiumEntry.Instance?.invElement;
    internal string Id(GameItem item) => identities.GetValue(item, _ => new Token()).Text;
    internal Guid GuidFor(GameItem item) => identities.GetValue(item, _ => new Token()).Id;
    internal static bool IsPart(GameItem item) => item.identifier != null && ModEntry.PartKinds.ContainsKey(item.identifier);

    internal void Reset()
    {
        Close();
        Visuals.Detach();
        identities = new ConditionalWeakTable<GameItem, Token>();
        writesEnabled = false;
        writeFault = false;
        writing = false;
        page = 0;
        depositPage = 0;
        busPage = 0;
        depositTarget = null;
        message = "已载入游戏；网络目录已重置，写入许可仅在本次会话有效。";
    }

    internal void Close()
    {
        if (CustomUIManager.Instance != null)
        {
            CustomUIManager.Instance.CloseWindow(WindowId);
            CustomUIManager.Instance.CloseWindow("deepspaceae.part");
        }
    }

    internal static List<GameInventory> Contents(GameItem owner)
    {
        if (owner.child == null) return new List<GameInventory>();
        var result = new List<GameInventory>();
        var visited = new HashSet<GraphNodeStorage>();
        var queue = new Queue<GraphNodeStorage>();
        queue.Enqueue(owner.child);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!visited.Add(node)) continue;
            if (visited.Count > 2048) throw new InvalidOperationException("Container structure exceeds the observation limit.");
            if (node is GameInventory inventory)
            {
                if (ReferenceEquals(inventory.GetParentItem(), owner)) result.Add(inventory);
                continue; // Direct inventories only: never recurse into stored items/nested containers.
            }
            if (node is GameItem) continue;
            foreach (var child in node.children) if (child != null) queue.Enqueue(child);
        }
        return result;
    }

    private sealed class View
    {
        internal GameGridInventory Root = null!;
        internal List<GameItem> Parts = null!;
        internal Dictionary<GameItem, GameItem> Owners = new();
        internal Dictionary<Guid, GameItem> Live = new();
        internal Dictionary<Guid, GameInventory> Sources = new();
        internal TerminalCatalog Catalog = null!;
        internal InventoryLayout Layout = null!;
        internal ItemLocation[] Rows = null!;
        internal HashSet<GameItem> ContainerOwners = new();
        internal Dictionary<GameItem, List<GameInventory>> Inventories = new();
        internal GameItem[] Buses = null!;
        internal GameItem[] ActiveBuses = null!;
        internal HashSet<GameItem> ActiveBusSet = new();
        internal GameItem[] Carried = null!;
        internal string PartSummary = "";
    }

    private View Read(GameItem terminal)
    {
        var root = MainInventory() ?? throw new InvalidOperationException("Load a test save first.");
        if (terminal.identifier != "deepspaceae.terminal" || !ReferenceEquals(terminal.parentInventory, root))
            throw new InvalidOperationException("The AE terminal must be directly in MAIN, outside bags and boxes.");
        var layout = new InventoryLayout(root);
        var view = new View { Root = root, Layout = layout, Parts = layout.Parts };
        foreach (var bus in view.Parts.Where(i => i.identifier == "deepspaceae.item_storage_bus"))
        {
            var facingOwner = layout.FrontOwner(bus);
            if (facingOwner != null && !IsPart(facingOwner) && layout.Owned.Contains(facingOwner))
            {
                if (!view.Inventories.TryGetValue(facingOwner, out var contents))
                { contents = Contents(facingOwner); view.Inventories.Add(facingOwner, contents); }
                if (contents.Count > 0) view.Owners.Add(bus, facingOwner);
            }
        }
        var nodes = view.Parts.Select(item => new NetworkNode(Id(item), ModEntry.PartKinds[item.identifier],
            item.identifier == "deepspaceae.item_storage_bus" ? (view.Owners.TryGetValue(item, out var owner) ? Id(owner) : "unbound:" + Id(item)) : null)).ToArray();
        var topology = PhysicalNetworkProjection.Build(nodes, layout.Edges(Id), new ChannelSettings(channels));
        var evaluation = new NetworkEvaluation(topology);
        var terminalConnection = evaluation.Devices[Id(terminal)];
        var snapshots = new List<ContainerSnapshot>();
        view.ContainerOwners = new HashSet<GameItem>(view.Owners.Values);
        var accessibleOwners = new HashSet<GameItem>();
        if (terminalConnection.Status == DeviceStatus.Online)
            foreach (var pair in view.Owners)
            {
                var connection = evaluation.Devices[Id(pair.Key)];
                if (connection.Status == DeviceStatus.Online && connection.CoreId == terminalConnection.CoreId) accessibleOwners.Add(pair.Value);
            }
        foreach (var owner in accessibleOwners)
        {
            var observed = new List<ItemInstance>();
            foreach (var inventory in view.Inventories[owner])
                foreach (var item in inventory.childItems)
                {
                    if (!ReferenceEquals(item.parentInventory, inventory)) throw new InvalidOperationException("Inconsistent container observation; reading stopped.");
                    var id = GuidFor(item);
                    view.Live.Add(id, item); // Duplicate observations fail closed.
                    view.Sources.Add(id, inventory);
                    observed.Add(new ItemInstance(id, item.identifier ?? "unknown", $"units={item.unitCount}; runtime={item.uniqueId}"));
                }
            snapshots.Add(new ContainerSnapshot(Id(owner), observed));
        }
        view.Catalog = new TerminalCatalog(topology, Id(terminal), snapshots, evaluation);
        view.Rows = view.Catalog.Index.ByInstance.Values.ToArray(); // Native observation order; no full O(M log M) sort per page.
        view.Buses = view.Parts.Where(item => item.identifier == "deepspaceae.item_storage_bus").ToArray();
        view.ActiveBuses = view.Owners.Keys.Where(bus => Online(view, bus)).ToArray();
        view.ActiveBusSet = new HashSet<GameItem>(view.ActiveBuses);
        view.Carried = view.Layout.Items.Where(item => !IsPart(item) && !view.ContainerOwners.Contains(item)).ToArray();
        view.PartSummary = Diagnostics.PartCounts(root);
        return view;
    }

    internal void OpenPart(GameItem part)
    {
        if (writing) return;
        if (part.identifier == "deepspaceae.terminal") { Open(part); return; }
        try
        {
            Visuals.Attach();
            var root = MainInventory();
            if (root == null || !ReferenceEquals(part.parentInventory, root))
                throw new InvalidOperationException("Place the AE part directly in MAIN before opening it.");
            var kind = ModEntry.PartKinds[part.identifier];
            var builder = CustomUIManager.Window("deepspaceae.part", "DeepSpaceAE · " + Safe(part.name), CustomUIManager.LAYER_PAPER)
                .SetSize(550, 330).SetDraggable(true).SetCloseOnEscape(true)
                .AddLabel("双击打开组件；单击和拖动不会打开窗口。")
                .AddLabel(kind == NetworkPart.Core ? "核心占格 3×3，提供 32 频道。" : kind == NetworkPart.Pipeline
                    ? "线缆按实际接触自动切换连接贴图；每段提供 8 频道。"
                    : "总线箭头面连接真实容器，背面连接核心或线缆。拖拽时使用游戏旋转/翻转操作调整方向。")
                .AddButton("打开存储终端", () => Open(part)).AddButton("只读诊断", Diagnose);
            builder.AddButton("关闭", () => CustomUIManager.Instance.CloseWindow("deepspaceae.part")).Show();
        }
        catch (Exception error)
        {
            log.Error(Diagnostics.ConsoleText(error.ToString()));
            Diagnostics.Show("DeepSpaceAE - Part open failed", new[] { error.Message });
        }
    }

    internal void Open(GameItem selected)
    {
        if (writing) return;
        log.Log("Open terminal requested: " + Diagnostics.ConsoleText(selected.identifier ?? "unknown"));
        try
        {
            if (CustomUIManager.Instance == null) { log.Warning("Game UI is not ready."); return; }
            var root = MainInventory();
            if (root == null) throw new InvalidOperationException("Load a test save first.");
            var terminal = selected.identifier == "deepspaceae.terminal" ? selected
                : root.childItems.FirstOrDefault(i => i.identifier == "deepspaceae.terminal");
            if (terminal == null) throw new InvalidOperationException("Place an AE terminal directly in MAIN first.");
            Show(terminal);
        }
        catch (Exception error)
        {
            // An open failure is not the current status of a later successful window.
            // In particular, do not leave a stale 'not in MAIN' warning in message.
            log.Error(Diagnostics.ConsoleText(error.ToString()));
            Diagnostics.Show("DeepSpaceAE - Terminal open failed", new[] { error.Message,
                "See F8 for the full exception, or use F9 > Special > AE: Diagnose." });
        }
    }

    private void Show(GameItem terminal, View? cached = null)
    {
        var view = cached ?? Read(terminal);
        var builder = CustomUIManager.Window(WindowId, "DeepSpaceAE · 存储终端 0.1.0", CustomUIManager.LAYER_PAPER)
            .SetSize(660, 700).SetDraggable(true).SetCloseOnEscape(true)
            .AddLabel($"网络：{view.Catalog.Connection.Status} | 频道 {(channels ? "开：核心32 / 管线8" : "关闭限制")}")
            .AddLabel("主库存部件：" + view.PartSummary)
            .AddLabel("实际写入许可：" + (writeFault ? "已锁定" : writesEnabled ? "开启" : "关闭（只读）"))
            .AddWarning(message)
            .AddToggle("启用频道限制", channels, enabled => { channels = enabled; Open(terminal); })
            .AddToggle("实验取放（仅测试档；整实例、不堆叠）", writesEnabled && !writeFault, enabled => {
                writesEnabled = enabled && !writeFault;
                message = writeFault ? "本次会话发生未知搬运结果，写入已锁定，请停止测试并查看 F8。" : (writesEnabled ? "实验取放已开启。每次移动重新核验连接。" : "已切换只读。");
                Open(terminal);
            }).AddButton("刷新网络与容器", () => Open(terminal))
            .AddButton("只读诊断", Diagnose).BeginScroll(400);
        if (view.Catalog.Connection.Status == DeviceStatus.ConflictingCores)
            builder.AddWarning("核心冲突：让本终端的连通网络只保留一个核心。关闭频道限制不能解除核心冲突。");
        busPage = Math.Max(0, Math.Min(busPage, Math.Max(0, (view.Buses.Length - 1) / 8)));
        builder.AddHeader($"总线：{view.Buses.Length} · 第 {busPage + 1} 页");
        if (busPage > 0) builder.AddButton("总线上一页", () => { busPage--; Show(terminal, view); });
        if ((busPage + 1) * 8 < view.Buses.Length) builder.AddButton("总线下一页", () => { busPage++; Show(terminal, view); });
        for (int busIndex = busPage * 8; busIndex < Math.Min(view.Buses.Length, (busPage + 1) * 8); busIndex++)
        {
            var part = view.Buses[busIndex];
            var status = view.Catalog.Network.Devices[Id(part)].Status;
            var selectedOwner = view.Owners.TryGetValue(part, out var current) ? current.name : "未接入：箭头面需接触真实容器，背面接线";
            builder.AddHeader($"总线 {Short(part)} · 设备 {status} · {(Online(view, part) ? "本终端可访问" : "本终端不可访问")} · {Safe(selectedOwner)}");
        }
        var rows = view.Rows;
        page = Math.Max(0, Math.Min(page, Math.Max(0, (rows.Length - 1) / 30)));
        builder.AddHeader($"接入物品：{rows.Length} 个实例 · 第 {page + 1} 页");
        if (page > 0) builder.AddButton("物品上一页", () => { page--; Show(terminal, view); });
        if ((page + 1) * 30 < rows.Length) builder.AddButton("物品下一页", () => { page++; Show(terminal, view); });
        for (int rowIndex = page * 30; rowIndex < Math.Min(rows.Length, (page + 1) * 30); rowIndex++)
        {
            var row = rows[rowIndex];
            var item = view.Live[row.Item.InstanceId];
            var captured = item;
            var expectedSource = view.Sources[row.Item.InstanceId];
            builder.AddLabel($"{Safe(item.name)} ×{item.unitCount} · {Short(item)} · 状态不合并");
            if (writesEnabled && !writeFault) builder.AddButton("取出整实例", () => Move(terminal, captured, null, expectedSource, null));
        }
        if (writesEnabled && !writeFault)
        {
            builder.AddHeader("从主库存存入（先选目标总线，再选物品）");
            if (depositTarget == null || !view.ActiveBusSet.Contains(depositTarget))
                depositTarget = view.ActiveBuses.Length > 0 ? view.ActiveBuses[0] : null;
            for (int busIndex = busPage * 8; busIndex < Math.Min(view.Buses.Length, (busPage + 1) * 8); busIndex++)
            {
                var capturedBus = view.Buses[busIndex];
                if (!view.ActiveBusSet.Contains(capturedBus)) continue;
                builder.AddButton("目标总线 " + Short(capturedBus) + " → " + Safe(view.Owners[capturedBus].name), () => { depositTarget = capturedBus; Show(terminal, view); });
            }
            if (depositTarget != null)
            {
                var capturedBus = depositTarget;
                var expectedOwner = view.Owners[capturedBus];
                builder.AddLabel("已选总线 " + Short(capturedBus));
                var carried = view.Carried;
                depositPage = Math.Max(0, Math.Min(depositPage, Math.Max(0, (carried.Length - 1) / 20)));
                if (depositPage > 0) builder.AddButton("主库存上一页", () => { depositPage--; Show(terminal, view); });
                if ((depositPage + 1) * 20 < carried.Length) builder.AddButton("主库存下一页", () => { depositPage++; Show(terminal, view); });
                for (int itemIndex = depositPage * 20; itemIndex < Math.Min(carried.Length, (depositPage + 1) * 20); itemIndex++)
                {
                    var captured = carried[itemIndex];
                    builder.AddButton("存入 " + Safe(captured.name) + $" ×{captured.unitCount} ({Short(captured)})", () => Move(terminal, captured, capturedBus, view.Root, expectedOwner));
                }
            }
        }
        builder.End().AddButton("关闭", Close).Show();
        log.Log($"Terminal shown: status={view.Catalog.Connection.Status}; visible={rows.Length}; writes={writesEnabled && !writeFault}. "
            + view.PartSummary);
    }

    internal void Diagnose()
    {
        var lines = new List<string> { "Read-only observation; no items moved or spawned.",
            $"Writes enabled={writesEnabled}; fault={writeFault}. IDs are session-only." };
        try
        {
            var root = MainInventory();
            if (root == null) throw new InvalidOperationException("Load a test save first.");
            lines.Add("MAIN = EmporiumEntry.invElement: " + Diagnostics.PartCounts(root));
            var legacy = PlayerStore.Instance?.GetStoreInventory(); // Observation only; never a movement target.
            lines.Add("Legacy store same object as MAIN: " + ReferenceEquals(root, legacy));
            if (legacy != null) lines.Add("Legacy store (ignored by network): " + Diagnostics.PartCounts(legacy));
            var layout = new InventoryLayout(root);
            var facingBuses = new Dictionary<GameItem, List<string>>();
            foreach (var bus in layout.Parts.Where(item => item.identifier == "deepspaceae.item_storage_bus"))
            {
                var owner = layout.FrontOwner(bus);
                if (owner == null) continue;
                if (!facingBuses.TryGetValue(owner, out var list)) { list = new List<string>(); facingBuses.Add(owner, list); }
                list.Add(Short(bus));
            }
            foreach (var part in layout.Parts.Take(100))
            {
                var shape = part.modifiedShape;
                lines.Add($"{Short(part)} {part.identifier}; cell=({shape.minX},{shape.minY}); orientation={shape.orientation}; flip={shape.flipped}; adjacent=[{string.Join(",", layout.Neighbors(part).Select(Short))}]");
            }
            if (layout.Parts.Count > 100) lines.Add("Part detail truncated at 100; MAIN counts above are complete.");
            var others = layout.Items.Where(item => !IsPart(item)).ToArray();
            foreach (var owner in others.Take(60))
                lines.Add($"{Short(owner)} {owner.identifier}; owned={layout.Owned.Contains(owner)}; direct inventories={Contents(owner).Count}; facing buses=[{(facingBuses.TryGetValue(owner, out var facing) ? string.Join(",", facing) : "")}]");
            if (others.Length > 60) lines.Add("Non-AE detail truncated at 60.");
        }
        catch (Exception error) { lines.Add("Observation stopped: " + error.Message); log.Error(Diagnostics.ConsoleText(error.ToString())); }
        log.Log(Diagnostics.ConsoleText(string.Join("\n", lines)));
        Diagnostics.Show("DeepSpaceAE - Read-only diagnostics", lines);
    }

    private bool Online(View view, GameItem bus)
    {
        var connection = view.Catalog.Network.Devices[Id(bus)];
        return view.Catalog.Connection.Status == DeviceStatus.Online && connection.Status == DeviceStatus.Online
            && connection.CoreId == view.Catalog.Connection.CoreId;
    }

    private void Move(GameItem terminal, GameItem item, GameItem? bus, GameInventory expectedSource, GameItem? expectedOwner)
    {
        if (writing) return;
        writing = true;
        bool enteredNative = false;
        try
        {
            if (!writesEnabled || writeFault) throw new MoveRejectedException("Experimental writes are disabled or locked.");
            var view = Read(terminal);
            if (view.Catalog.Connection.Status != DeviceStatus.Online) throw new MoveRejectedException("The terminal is offline.");
            var source = item.parentInventory;
            if (!ReferenceEquals(source, expectedSource)) throw new MoveRejectedException("Source inventory changed since the displayed observation; refresh first.");
            if (source == null || !source.childItems.Any(i => ReferenceEquals(i, item)) || !GeneralHelper.IsItemEffectivelyOwned(item) || IsPart(item))
                throw new MoveRejectedException("Source instance changed, is not owned, or cannot be moved.");
            int count = item.unitCount;
            List<GameInventory> destinations;
            if (bus == null)
            {
                if (!view.Catalog.Index.ByInstance.ContainsKey(GuidFor(item))) throw new MoveRejectedException("The item is no longer visible through an accessible bus.");
                destinations = new List<GameInventory> { view.Root };
            }
            else
            {
                if (!ReferenceEquals(source, view.Root) || !view.Owners.TryGetValue(bus, out var owner) || !Online(view, bus))
                    throw new MoveRejectedException("Bus disconnected or source is not directly in MAIN.");
                if (!ReferenceEquals(owner, expectedOwner)) throw new MoveRejectedException("Bus target changed since the displayed observation; refresh first.");
                destinations = Contents(owner);
            }
            if (!item.MayRemove() || count <= 0 || item.MaxNumRemove() < count || item.unitCount != count)
                throw new MoveRejectedException("Native removal rules reject moving the whole instance.");
            SlotMarker? placement = null;
            foreach (var destination in destinations)
            {
                if (ReferenceEquals(source, destination) || destination.IsInsertLocked() || GraphUtils.FindParentEquals(destination, item)) continue;
                var candidate = destination.TryFindOneValidInventorySlot(item);
                if (candidate != null && ReferenceEquals(candidate.item, item) && ReferenceEquals(candidate.inventory, destination)
                    && candidate.targetItem == null && candidate.itemGridShape != null && candidate.numTransfer >= count)
                { placement = candidate; break; }
            }
            if (placement == null) throw new MoveRejectedException("No whole-instance slot: full, rejected, stacking, or nested placement. Nothing moved.");
            var finalView = Read(terminal);
            bool stillVisible = bus == null ? finalView.Catalog.Index.ByInstance.ContainsKey(GuidFor(item))
                : finalView.Owners.TryGetValue(bus, out var finalOwner) && Online(finalView, bus)
                    && ReferenceEquals(finalOwner, expectedOwner)
                    && Contents(finalOwner).Any(inv => ReferenceEquals(inv, placement.inventory));
            if (finalView.Catalog.Connection.Status != DeviceStatus.Online || !ReferenceEquals(finalView.Root, view.Root)
                || !stillVisible || !ReferenceEquals(item.parentInventory, source) || !source.childItems.Any(i => ReferenceEquals(i, item))
                || item.unitCount != count || placement.targetItem != null || placement.numTransfer < count)
                throw new InvalidOperationException("Connection or source ownership changed during preflight; writes locked.");
            enteredNative = true;
            int moved = placement.TryAcceptOnce(count);
            if (moved != count || item.unitCount != count || !ReferenceEquals(item.parentInventory, placement.inventory)
                || !placement.inventory.childItems.Any(i => ReferenceEquals(i, item)) || source.childItems.Any(i => ReferenceEquals(i, item)))
                throw new InvalidOperationException("Move result unknown or incomplete; writes locked. No automatic retry or rollback.");
            message = $"已移动整实例 {Short(item)} ×{count}。";
            log.Log($"Whole instance moved: id={Short(item)}; count={count}.");
        }
        catch (Exception error)
        {
            if (enteredNative || error is not MoveRejectedException) { writeFault = true; writesEnabled = false; }
            message = error.Message;
            log.Warning(Diagnostics.ConsoleText(error.ToString()));
        }
        finally { writing = false; }
        Open(terminal);
    }

    private string Short(GameItem item) => Id(item).Substring(0, 6);
    private static string Safe(string value) => (value ?? "?").Replace("<", "〈").Replace(">", "〉");
}
