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
    private sealed class Token { internal Guid Id { get; } = Guid.NewGuid(); }
    private ConditionalWeakTable<GameItem, Token> identities = new();
    private readonly Dictionary<GameItem, GameItem> bindings = new();
    private readonly ModLog log;
    private bool channels = true;
    private bool writesEnabled;
    private bool writeFault;
    private bool writing;
    private int page;
    private int depositPage;
    private GameItem? depositTarget;
    private sealed class MoveRejectedException : Exception { internal MoveRejectedException(string message) : base(message) { } }
    private string message = "只读模式；请用独立测试存档。";

    internal GameSession(ModLog log) => this.log = log;
    internal string Id(GameItem item) => identities.GetValue(item, _ => new Token()).Id.ToString("N");
    internal Guid GuidFor(GameItem item) => identities.GetValue(item, _ => new Token()).Id;
    internal static bool IsPart(GameItem item) => ModEntry.Parts.Any(p => p.Id == item.identifier);

    internal void Reset()
    {
        Close();
        bindings.Clear();
        identities = new ConditionalWeakTable<GameItem, Token>();
        writesEnabled = false;
        writeFault = false;
        writing = false;
        page = 0;
        depositPage = 0;
        depositTarget = null;
        message = "已载入游戏；总线选择和写入许可仅在本次会话有效。";
    }

    internal void Close()
    {
        if (CustomUIManager.Instance != null) CustomUIManager.Instance.CloseWindow(WindowId);
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
            if (visited.Count > 2048) throw new InvalidOperationException("容器结构超出测试版读取上限。");
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
        internal Dictionary<GameItem, List<GameItem>> Candidates = new();
        internal Dictionary<GameItem, GameItem> Owners = new();
        internal Dictionary<Guid, GameItem> Live = new();
        internal TerminalCatalog Catalog = null!;
    }

    private View Read(GameItem terminal)
    {
        var root = PlayerStore.Instance?.GetStoreInventory() ?? throw new InvalidOperationException("请先载入存档。");
        if (terminal.identifier != "deepspaceae.terminal" || !ReferenceEquals(terminal.parentInventory, root))
            throw new InvalidOperationException("终端必须直接放在主物品栏。请激活主库存中的 AE 终端。");
        var view = new View { Root = root, Parts = root.childItems.Where(i => IsPart(i) && GeneralHelper.IsItemEffectivelyOwned(i)).ToList() };
        foreach (var bus in view.Parts.Where(i => i.identifier == "deepspaceae.item_storage_bus"))
        {
            var candidates = root.childItems.Where(owner => !IsPart(owner) && GeneralHelper.IsItemEffectivelyOwned(owner)
                && bus.IsAdjacent(owner) && Contents(owner).Count > 0).ToList();
            view.Candidates.Add(bus, candidates);
            if (bindings.TryGetValue(bus, out var selected) && candidates.Contains(selected)) view.Owners.Add(bus, selected);
            else if (candidates.Count == 1) view.Owners.Add(bus, candidates[0]);
        }
        var nodes = view.Parts.Select(item => new NetworkNode(Id(item), ModEntry.Parts.First(p => p.Id == item.identifier).Part,
            item.identifier == "deepspaceae.item_storage_bus" ? (view.Owners.TryGetValue(item, out var owner) ? Id(owner) : "unbound:" + Id(item)) : null)).ToArray();
        var edges = new List<NetworkLink>();
        for (int a = 0; a < view.Parts.Count; a++)
            for (int b = a + 1; b < view.Parts.Count; b++)
                if (view.Parts[a].IsAdjacent(view.Parts[b])) edges.Add(new NetworkLink(Id(view.Parts[a]), Id(view.Parts[b])));
        var topology = PhysicalNetworkProjection.Build(nodes, edges, new ChannelSettings(channels));
        var snapshots = new List<ContainerSnapshot>();
        foreach (var owner in view.Owners.Values.Distinct())
        {
            var observed = new List<ItemInstance>();
            foreach (var inventory in Contents(owner))
                foreach (var item in inventory.childItems)
                {
                    if (!ReferenceEquals(item.parentInventory, inventory)) throw new InvalidOperationException("容器观测不一致；停止读取。");
                    var id = GuidFor(item);
                    view.Live.Add(id, item); // Duplicate observations fail closed.
                    observed.Add(new ItemInstance(id, item.identifier ?? "unknown", $"units={item.unitCount}; runtime={item.uniqueId}"));
                }
            snapshots.Add(new ContainerSnapshot(Id(owner), observed));
        }
        view.Catalog = new TerminalCatalog(topology, Id(terminal), snapshots);
        return view;
    }

    internal void Open(GameItem selected)
    {
        if (writing) return;
        try
        {
            if (CustomUIManager.Instance == null) { log.Warning("Game UI is not ready."); return; }
            var root = PlayerStore.Instance?.GetStoreInventory();
            if (root == null) throw new InvalidOperationException("请先载入存档。");
            var terminal = selected.identifier == "deepspaceae.terminal" ? selected
                : root.childItems.FirstOrDefault(i => i.identifier == "deepspaceae.terminal");
            if (terminal == null) throw new InvalidOperationException("请先在主库存放置一个 AE 终端。");
            Show(terminal);
        }
        catch (Exception error) { message = error.Message; log.Error(error.ToString()); }
    }

    private void Show(GameItem terminal)
    {
        var view = Read(terminal);
        var builder = CustomUIManager.Window(WindowId, "DeepSpaceAE · 存储终端 0.1.0", CustomUIManager.LAYER_PAPER)
            .SetSize(660, 700).SetDraggable(true).SetCloseOnEscape(true)
            .AddLabel($"网络：{view.Catalog.Connection.Status} | 频道 {(channels ? "开：核心32 / 管线8" : "关闭限制")}")
            .AddWarning(message)
            .AddToggle("启用频道限制", channels, enabled => { channels = enabled; Open(terminal); })
            .AddToggle("实验取放（仅测试档；整实例、不堆叠）", writesEnabled && !writeFault, enabled => {
                writesEnabled = enabled && !writeFault;
                message = writeFault ? "本次会话发生未知搬运结果，写入已锁定，请停止测试并查看 F8。" : (writesEnabled ? "实验取放已开启。每次移动重新核验连接。" : "已切换只读。");
                Open(terminal);
            }).AddButton("刷新网络与容器", () => Open(terminal)).BeginScroll(470);
        foreach (var part in view.Parts)
        {
            if (part.identifier != "deepspaceae.item_storage_bus") continue;
            var status = view.Catalog.Network.Devices[Id(part)].Status;
            var selectedOwner = view.Owners.TryGetValue(part, out var current) ? current.name : "未接入：需侧边接触真实容器";
            builder.AddHeader($"总线 {Short(part)} · {status} · {Safe(selectedOwner)}");
            foreach (var candidate in view.Candidates[part])
            {
                var capturedBus = part;
                var capturedOwner = candidate;
                builder.AddButton("接入：" + Safe(candidate.name), () => { bindings[capturedBus] = capturedOwner; Open(terminal); });
            }
        }
        var rows = view.Catalog.Index.ByInstance.Values.OrderBy(row => row.ContainerId, StringComparer.Ordinal).ThenBy(row => row.Item.InstanceId).ToArray();
        page = Math.Max(0, Math.Min(page, Math.Max(0, (rows.Length - 1) / 30)));
        builder.AddHeader($"接入物品：{rows.Length} 个实例 · 第 {page + 1} 页");
        if (page > 0) builder.AddButton("物品上一页", () => { page--; Open(terminal); });
        if ((page + 1) * 30 < rows.Length) builder.AddButton("物品下一页", () => { page++; Open(terminal); });
        foreach (var row in rows.Skip(page * 30).Take(30))
        {
            var item = view.Live[row.Item.InstanceId];
            var captured = item;
            builder.AddLabel($"{Safe(item.name)} ×{item.unitCount} · {Short(item)} · 状态不合并");
            if (writesEnabled && !writeFault) builder.AddButton("取出整实例", () => Move(terminal, captured, null));
        }
        if (writesEnabled && !writeFault)
        {
            builder.AddHeader("从主库存存入（先选目标总线，再选物品）");
            var activeBuses = view.Owners.Keys.Where(bus => Online(view, bus)).ToArray();
            if (depositTarget == null || !activeBuses.Contains(depositTarget)) depositTarget = activeBuses.FirstOrDefault();
            foreach (var bus in activeBuses)
            {
                var capturedBus = bus;
                builder.AddButton("目标总线 " + Short(bus) + " → " + Safe(view.Owners[bus].name), () => { depositTarget = capturedBus; Open(terminal); });
            }
            if (depositTarget != null)
            {
                var capturedBus = depositTarget;
                builder.AddLabel("已选总线 " + Short(capturedBus));
                var carried = view.Root.childItems.Where(i => !IsPart(i) && !view.Owners.Values.Contains(i)).ToArray();
                depositPage = Math.Max(0, Math.Min(depositPage, Math.Max(0, (carried.Length - 1) / 20)));
                if (depositPage > 0) builder.AddButton("主库存上一页", () => { depositPage--; Open(terminal); });
                if ((depositPage + 1) * 20 < carried.Length) builder.AddButton("主库存下一页", () => { depositPage++; Open(terminal); });
                foreach (var item in carried.Skip(depositPage * 20).Take(20))
                {
                    var captured = item;
                    builder.AddButton("存入 " + Safe(item.name) + $" ×{item.unitCount} ({Short(item)})", () => Move(terminal, captured, capturedBus));
                }
            }
        }
        builder.End().AddButton("关闭", Close).Show();
    }

    private bool Online(View view, GameItem bus)
    {
        var connection = view.Catalog.Network.Devices[Id(bus)];
        return view.Catalog.Connection.Status == DeviceStatus.Online && connection.Status == DeviceStatus.Online
            && connection.CoreId == view.Catalog.Connection.CoreId;
    }

    private void Move(GameItem terminal, GameItem item, GameItem? bus)
    {
        if (writing) return;
        writing = true;
        bool enteredNative = false;
        try
        {
            if (!writesEnabled || writeFault) throw new MoveRejectedException("写入未开启或已锁定。");
            var view = Read(terminal);
            if (view.Catalog.Connection.Status != DeviceStatus.Online) throw new MoveRejectedException("终端离线。");
            var source = item.parentInventory;
            if (source == null || !source.childItems.Any(i => ReferenceEquals(i, item)) || !GeneralHelper.IsItemEffectivelyOwned(item) || IsPart(item))
                throw new MoveRejectedException("源实例已变化、未拥有或不可移动。");
            int count = item.unitCount;
            List<GameInventory> destinations;
            if (bus == null)
            {
                if (!view.Catalog.Index.ByInstance.ContainsKey(GuidFor(item))) throw new MoveRejectedException("物品不再位于可访问的总线中。");
                destinations = new List<GameInventory> { view.Root };
            }
            else
            {
                if (!ReferenceEquals(source, view.Root) || !view.Owners.TryGetValue(bus, out var owner) || !Online(view, bus))
                    throw new MoveRejectedException("总线已断开或源不在主库存。");
                destinations = Contents(owner);
            }
            if (!item.MayRemove() || count <= 0 || item.MaxNumRemove() < count || item.unitCount != count)
                throw new MoveRejectedException("原生移除规则拒绝整实例移动。");
            SlotMarker? placement = null;
            foreach (var destination in destinations)
            {
                if (ReferenceEquals(source, destination) || destination.IsInsertLocked() || GraphUtils.FindParentEquals(destination, item)) continue;
                var candidate = destination.TryFindOneValidInventorySlot(item);
                if (candidate != null && ReferenceEquals(candidate.item, item) && ReferenceEquals(candidate.inventory, destination)
                    && candidate.targetItem == null && candidate.itemGridShape != null && candidate.numTransfer >= count)
                { placement = candidate; break; }
            }
            if (placement == null) throw new MoveRejectedException("无可用整实例位置：容量不足、拒收、目标堆叠或自动转入嵌套容器。未移动。");
            var finalView = Read(terminal);
            bool stillVisible = bus == null ? finalView.Catalog.Index.ByInstance.ContainsKey(GuidFor(item))
                : finalView.Owners.TryGetValue(bus, out var finalOwner) && Online(finalView, bus)
                    && Contents(finalOwner).Any(inv => ReferenceEquals(inv, placement.inventory));
            if (finalView.Catalog.Connection.Status != DeviceStatus.Online || !ReferenceEquals(finalView.Root, view.Root)
                || !stillVisible || !ReferenceEquals(item.parentInventory, source) || !source.childItems.Any(i => ReferenceEquals(i, item))
                || item.unitCount != count || placement.targetItem != null || placement.numTransfer < count)
                throw new InvalidOperationException("预检期间连接或源归属发生变化；写入已锁定。");
            enteredNative = true;
            int moved = placement.TryAcceptOnce(count);
            if (moved != count || item.unitCount != count || !ReferenceEquals(item.parentInventory, placement.inventory)
                || !placement.inventory.childItems.Any(i => ReferenceEquals(i, item)) || source.childItems.Any(i => ReferenceEquals(i, item)))
                throw new InvalidOperationException("搬运结果未知或不完整；停止写入，不自动重试或回滚。");
            message = $"已移动整实例 {Short(item)} ×{count}。";
            log.Log(message);
        }
        catch (Exception error)
        {
            if (enteredNative || error is not MoveRejectedException) { writeFault = true; writesEnabled = false; }
            message = error.Message;
            log.Warning(error.ToString());
        }
        finally { writing = false; }
        Open(terminal);
    }

    private string Short(GameItem item) => Id(item).Substring(0, 6);
    private static string Safe(string value) => (value ?? "?").Replace("<", "〈").Replace(">", "〉");
}
