using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

internal sealed partial class GameSession
{
    private PixelWindow? terminalWindow, partWindow, editorWindow, noticeWindow;
    private VirtualInventory? terminalGrid, filterGrid;
    private StealPixelElement? borrowedStorage;
    private GameItem? displayedPart;
    private View? displayedView;
    private IReadOnlyList<CatalogRecord>? packedRows;
    private VirtualInventoryLayout? packing;
    private MachineryText? terminalStatus;
    private MachineryButton? pageLabel;
    private NativeWidgets.Field? tagField;
    private string[] displayedTags = Array.Empty<string>();
    private bool partRefreshQueued;
    private readonly Dictionary<string, Sprite> filterSprites = new(StringComparer.Ordinal);

    private void CloseTerminal()
    {
        if (NativeDrag.Gesture != null && ReferenceEquals(NativeDrag.Gesture.Surface.Owner, openTerminal)) NativeDrag.Gesture.Cancelled = true;
        Unobserve(); displayedView = null; terminalGrid = null; packedRows = null; packing = null;
        if (terminalWindow != null) { var w = terminalWindow; terminalWindow = null; w.Hide(); UnityEngine.Object.Destroy(w.handler.gameObject); }
    }
    private void ClosePart()
    {
        if (editorWindow != null) { var w = editorWindow; editorWindow = null; w.Hide(); UnityEngine.Object.Destroy(w.handler.gameObject); }
        borrowedStorage?.UnStealItem(); borrowedStorage = null; displayedPart = null; filterGrid = null;
        if (partWindow != null) { var w = partWindow; partWindow = null; w.Hide(); UnityEngine.Object.Destroy(w.handler.gameObject); }
    }
    internal void NativeWindowHidden(PixelWindow window)
    {
        if (ReferenceEquals(window, terminalWindow)) CloseTerminal();
        else if (ReferenceEquals(window, partWindow)) ClosePart();
        else if (ReferenceEquals(window, editorWindow)) CloseEditor();
        else if (ReferenceEquals(window, noticeWindow)) CloseNotice();
    }
    private void CloseNative() { CloseTerminal(); ClosePart(); CloseNotice(); }
    private void CloseNotice()
    { if (noticeWindow != null) { var w = noticeWindow; noticeWindow = null; w.Hide(); UnityEngine.Object.Destroy(w.handler.gameObject); } }
    private void ShowNotice(string text)
    {
        if (RenderHandler.current == null) return;
        CloseNotice(); noticeWindow = MachineHelper.CreateMachineryWindow(true, MachineHelper.Panel("状态", MachineryStack.Vertical(
            MachineryText.Status("", text), new MachineryButton("关闭", CloseNotice)), "notice", false));
        noticeWindow.closeCallback = CloseNotice; noticeWindow.Show().Center();
    }
    private PixelElement Borrow(GameItem owner)
    {
        var storage = (PixelElement)PartStorage.Inventory(owner);
        if (storage is GameSlotInventory slot) slot.SetMachineryStyle();
        if (storage is GameGridInventory grid) grid.SetMachineryStyle();
        borrowedStorage = new StealPixelElement(false); borrowedStorage.Attach(storage);
        return new MachineryIntake(borrowedStorage, true, false);
    }
    private static DisplayFootprint Footprint(Guid id, GameItem item)
    {
        var s = item.modifiedShape; var cells = new List<GridCell>();
        for (int y = 0; y < s.height; y++) for (int x = 0; x < s.width; x++)
            if (s.GetLocal(x, y) == 1) { var (gx, gy) = s.ForwardTransform(x, y); cells.Add(new GridCell(gx, gy)); }
        int minX = cells.Min(c => c.X), minY = cells.Min(c => c.Y);
        return new DisplayFootprint(id, cells.Select(c => new GridCell(c.X - minX, c.Y - minY)));
    }
    private void Show(GameItem terminal, View view)
    {
        bool create = terminalWindow == null || !ReferenceEquals(openTerminal, terminal);
        if (create) CloseTerminal(); else Unobserve();
        displayedView = view; openTerminal = terminal; packedRows = null; packing = null;
        foreach (var inv in view.Inventories.Values.SelectMany(i => i).Append(view.Root).Distinct())
        {
            foreach (var item in inv.childItems) observations.GetValue(item, i => new Observation(i));
            observed.Add(inv); inv.onSlotAddItemEndFunc += Changed; inv.onSlotRemoveItemEndFunc += Changed; inv.onSlotChangeAmountItemFunc += Changed;
        }
        if (create)
        {
            terminalGrid = new VirtualInventory(terminal, 18, 12);
            terminalGrid.Redraw = RenderTerminal;
            var query = NativeWidgets.Input("搜索物品", search, value => { if (search == value) return; search = value; page = 0; RenderTerminal(); }, 150);
            tagField = NativeWidgets.Dropdown(new[] { "全部类别" }, 0, i => { tag = i == 0 ? "" : displayedTags[i - 1]; page = 0; RenderTerminal(); }, 100);
            var sortField = NativeWidgets.Dropdown(new[] { "原始顺序", "名称", "数量" }, (int)sort, i => { sort = (CatalogSort)i; page = 0; RenderTerminal(); }, 70);
            terminalStatus = MachineryText.Status("网络", "");
            pageLabel = new MachineryButton("1 / 1", null, 52);
            var paging = MachineryStack.Horizontal(new MachineryButton("<", () => { page = Math.Max(0, page - 1); RenderTerminal(); }, 20), pageLabel,
                new MachineryButton(">", () => { if (packing != null && page + 1 < packing.PageCount) page++; RenderTerminal(); }, 20),
                new MachineryButton("刷新", () => Open(terminal), 36));
            var body = MachineryStack.Vertical(MachineryStack.Horizontal(query, tagField, sortField),
                new MachineryIntake(terminalGrid), MachineryStack.Horizontal(terminalStatus, paging));
            terminalWindow = new PixelWindow(true, "AE 存储终端", WindowStyle.Machinery);
            terminalWindow.Attach(body); terminalWindow.closeCallback = CloseTerminal;
        }
        var tags = view.Search.Tags.ToArray();
        if (!displayedTags.SequenceEqual(tags) || create)
        {
            displayedTags = tags;
            int selected = Array.IndexOf(tags, tag) + 1; if (selected == 0) tag = "";
            tagField!.Control.SetOptions(new[] { "全部类别" }.Concat(tags.Select(NativeWidgets.TagName)).ToArray()).SetIndex(selected);
        }
        RenderTerminal();
        if (create) terminalWindow!.Show().Center();
    }
    private void RenderTerminal()
    {
        if (displayedView == null || terminalGrid == null) return;
        var rows = displayedView.Search.Find(search, string.IsNullOrEmpty(tag) ? null : tag, sort);
        if (!ReferenceEquals(rows, packedRows))
        {
            packing = new VirtualInventoryLayout(rows.Select(r => Footprint(r.Instance, displayedView.Live[r.Instance])));
            packedRows = rows;
        }
        page = Math.Max(0, Math.Min(page, packing!.PageCount - 1));
        terminalGrid.Populate(packing, page, displayedView.Live, displayedView.Sources);
        pageLabel!.SetText($"{page + 1} / {packing.PageCount}");
        terminalStatus!.SetText(writeFault ? "取放已锁定" : string.IsNullOrEmpty(message) ? $"{rows.Count} 件" : message);
        terminalWindow?.Validate();
    }
    private void ShowCore(GameItem core)
    {
        ClosePart(); displayedPart = core;
        var board = ReadBoard(); var lease = PartStorage.Lease(core); var battery = PartStorage.Battery(core);
        int cost = board.Evaluation.NightCosts.TryGetValue(Id(core), out var budget) ? budget : 2;
        var stats = new GridPixelElement(4, 2, true).SetSpacing(6, 3);
        var counts = new int[4];
        foreach (var member in board.Evaluation.ComponentCore)
            if (member.Value == Id(core)) counts[(int)ModEntry.PartKinds[board.ById[member.Key].identifier]]++;
        foreach (NetworkPart kind in Enum.GetValues(typeof(NetworkPart)))
        {
            var definition = ModEntry.Parts.First(p => p.Part == kind);
            int column = (int)kind;
            var icon = new MachineryButton("", null, 32, 32).SetDescription(definition.Name + $"\n{counts[column]} 个\n每晚 {NetworkEvaluation.NightCost(kind)} kJ / 个");
            var artwork = NativeWidgets.Icon(NativeWidgets.Sprite(definition.Sprite), 28, 28); artwork.handler.SetParent(icon.handler); artwork.rectTransform.anchoredPosition = new Vector2(2, -2);
            stats.AttachPos(icon, column, 0);
            stats.AttachPos(MachineryText.Status("×", counts[column].ToString()), column, 1);
        }
        int used = board.Evaluation.ChannelsUsed.TryGetValue(Id(core), out var u) ? u : 0;
        var power = MachineryStack.Horizontal(NativeWidgets.Icon(MachineryStyle.IconPower), MachineryText.Status("每晚", cost + " kJ"));
        int remaining = battery == null ? 0 : PowerHelper.GetAvailableEnergyFromItem(battery, false);
        bool infinite = battery != null && HypertechBatteryHelper.IsHypertechBattery(battery);
        int maximum = battery == null || infinite ? 0 : battery.GetTagReadonly(PowerHelper.POWER_SOURCE_ITEM_MAX_ENERGY).GetInt();
        var bar = new MachineryProgressBar(infinite ? 1 : Math.Min(1, (float)remaining / Math.Max(1, maximum)), 120);
        string batteryState = battery == null ? "拖入电池" : lease.Phase == NightPhase.Fault || lease.Phase == NightPhase.DebitStarted ? "结算未确认 · 已锁定" : lease.CanRemove(PartStorage.Day) ? "可更换" : "已锁定 · 次日更换";
        var overview = MachineryStack.Vertical(stats, power, bar,
            MachineryText.Status("储能", infinite ? "超科技电池" : remaining + " / " + maximum + " kJ"),
            MachineryText.Status("本夜预留", (lease.Phase == NightPhase.Reserved ? lease.Required : 0) + " kJ"),
            MachineryText.Status("频道", used + " / 32"));
        partWindow = MachineHelper.CreateMachineryWindow(true,
            new MachineryPanel(MachineryStyle.IconPower, MachineryStack.Vertical(Borrow(core), MachineryText.Status("电池", batteryState)), "power"),
            MachineHelper.Panel("网络控制器", overview, "network", false));
        partWindow.SetTitle("AE 网络控制器"); partWindow.closeCallback = ClosePart; partWindow.Show().Center();
    }
    private void ShowCable(GameItem cable)
    {
        ClosePart(); displayedPart = cable;
        var board = ReadBoard();
        bool online = board.Evaluation.ComponentCore.TryGetValue(Id(cable), out var core) && board.Powered.Contains(core);
        partWindow = MachineHelper.CreateMachineryWindow(true, MachineHelper.Panel("线缆", MachineryStack.Vertical(
            NativeWidgets.Icon(NativeWidgets.Sprite("pipeline")), MachineryText.Status("状态", online ? "在线" : "未激活"),
            MachineryText.Status("频道", board.Evaluation.ChannelsUsed[Id(cable)] + " / 8")), "cable", false));
        partWindow.closeCallback = ClosePart; partWindow.Show().Center();
    }
    private void ShowBus(GameItem bus)
    {
        ClosePart(); displayedPart = bus;
        var policy = PartStorage.Policy(bus);
        filterGrid = new VirtualInventory(bus, 9, policy.Slots / 9, true);
        filterGrid.ConfigureFilter = (index, item) => CaptureGhost(bus, index, item);
        filterGrid.Redraw = RenderFilters;
        var access = new MachineryButton("", null, 60, 18);
        var matching = new MachineryButton("", null, 60, 18);
        var extraction = new MachineryButton("", null, 60, 18);
        void Controls()
        {
            var p = PartStorage.Policy(bus);
            access.SetText(p.Access == StorageAccess.ReadWrite ? "存入 / 取出" : p.Access == StorageAccess.ExtractOnly ? "仅取出" : "仅存入");
            matching.SetText(p.Exclude ? "排除" : "包含"); extraction.SetText(p.FilterExtraction ? "筛选取出" : "全部取出");
        }
        access.SetCallback(() => { ConfigureNumber(bus, "access", ((int)PartStorage.Policy(bus).Access + 1) % 3); Controls(); });
        matching.SetCallback(() => { ConfigureNumber(bus, "exclude", PartStorage.Policy(bus).Exclude ? 0 : 1); Controls(); });
        extraction.SetCallback(() => { ConfigureNumber(bus, "extract_filter", PartStorage.Policy(bus).FilterExtraction ? 0 : 1); Controls(); });
        access.SetDescription("访问模式"); matching.SetDescription("包含或排除幽灵格中的物品 / 类别"); extraction.SetDescription("取出时是否也应用筛选"); Controls();
        var toolbar = MachineryStack.Vertical(new MachineryButton("优先级", () => ShowPriority(bus), 60, 18), access, matching, extraction);
        var main = MachineryStack.Horizontal(toolbar, new MachineryIntake(filterGrid));
        var content = MachineryStack.Vertical(main, new MachineryPanel("升级", Borrow(bus), "upgrades"));
        partWindow = MachineHelper.CreateMachineryWindow(true, MachineHelper.Panel(policy.Tags ? "类别筛选" : "物品筛选", content, "storage_bus", false));
        partWindow.SetTitle("AE 存储总线"); partWindow.closeCallback = ClosePart;
        RenderFilters(); partWindow.Show().Center();
    }
    private void RenderFilters()
    {
        if (displayedPart == null || filterGrid == null) return;
        var bus = displayedPart; var policy = PartStorage.Policy(bus); var filters = PartStorage.Filters(bus);
        filterGrid.ClearTiles(); filterGrid.SetShape(9, policy.Slots / 9);
        for (int i = 0; i < policy.Slots; i++)
        {
            int slot = i; string key = filters[i];
            Sprite? sprite = policy.Tags ? NativeWidgets.Sprite("tag-upgrade") : filterSprites.TryGetValue(key, out var known) ? known : null;
            if (!policy.Tags && key.Length > 0 && sprite == null)
            {
                var source = PartStorage.Text(bus, "filter_icon_" + i).Split('\n');
                if (source.Length == 3)
                    sprite = source[2] == "1" ? ModLoader.Mods.FirstOrDefault(m => m.Manifest.ID == source[0])?.Sprites.GetValueOrDefault(source[1])
                        : RenderHandler.TryLoadFromAtlas(source[0], source[1]);
                if (sprite != null) filterSprites[key] = sprite;
            }
            string name = string.IsNullOrEmpty(key) ? "拖入物品以设置筛选" : policy.Tags ? NativeWidgets.TagName(key) : FilterName(bus, slot, key);
            filterGrid.GhostTile(i, string.IsNullOrEmpty(key) ? null : sprite, name + "\n单击清除", () => { SetFilter(bus, slot, ""); RenderFilters(); });
        }
        partWindow?.Validate();
    }
    private static string FilterName(GameItem bus, int slot, string fallback)
    {
        var names = PartStorage.Text(bus, "filter_names").Split('\n');
        return slot < names.Length && names[slot].Length > 0 ? names[slot] : fallback;
    }
    private void CaptureGhost(GameItem bus, int slot, GameItem item)
    {
        var policy = PartStorage.Policy(bus);
        if (slot < 0 || slot >= policy.Slots || !ReferenceEquals(bus.parentInventory, MainInventory()) || !GeneralHelper.IsItemEffectivelyOwned(bus)) return;
        if (!policy.Tags)
        {
            SetFilter(bus, slot, item.identifier);
            var sprite = (item as GameItemElement)?.ResolveSpriteByName(item.spritePath); if (sprite != null) filterSprites[item.identifier] = sprite;
            var names = PartStorage.Text(bus, "filter_names").Split('\n').Concat(Enumerable.Repeat("", 63)).Take(63).ToArray();
            names[slot] = item.name ?? ""; PartStorage.String(bus, "filter_names", string.Join("\n", names));
            PartStorage.String(bus, "filter_icon_" + slot, item.spriteAtlasPath + "\n" + item.spritePath + "\n" + (item.useSpriteFromMod ? "1" : "0"));
            RenderFilters(); return;
        }
        var tags = item.GetGameItemType().Distinct(StringComparer.Ordinal).ToArray();
        if (tags.Length == 0) return;
        if (tags.Length == 1) { SetFilter(bus, slot, tags[0]); RenderFilters(); return; }
        CloseEditor();
        int chosen = 0;
        editorWindow = MachineHelper.CreateMachineryWindow(true, MachineHelper.Panel("选择此物品的类别", MachineryStack.Vertical(
            NativeWidgets.Dropdown(tags.Select(NativeWidgets.TagName).ToArray(), 0, i => chosen = i, 140),
            new MachineryButton("确定", () => { SetFilter(bus, slot, tags[chosen]); CloseEditor(); RenderFilters(); })), "tag", false));
        editorWindow.closeCallback = CloseEditor; editorWindow.Show().Center();
    }
    private void CloseEditor()
    { if (editorWindow != null) { var w = editorWindow; editorWindow = null; w.Hide(); UnityEngine.Object.Destroy(w.handler.gameObject); } }
    private void ShowPriority(GameItem bus)
    {
        CloseEditor(); var field = NativeWidgets.Input("优先级", PartStorage.Policy(bus).Priority.ToString(), v => { if (int.TryParse(v, out var n)) ConfigureNumber(bus, "priority", n); }, 110);
        void Change(int delta)
        {
            long next = (long)PartStorage.Policy(bus).Priority + delta;
            ConfigureNumber(bus, "priority", (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, next))); field.Control.SetString(PartStorage.Policy(bus).Priority.ToString());
        }
        editorWindow = MachineHelper.CreateMachineryWindow(true, MachineHelper.Panel("优先级", MachineryStack.Vertical(field,
            MachineryStack.Horizontal(new MachineryButton("-10", () => Change(-10)), new MachineryButton("-1", () => Change(-1)),
            new MachineryButton("+1", () => Change(1)), new MachineryButton("+10", () => Change(10))), new MachineryButton("返回", CloseEditor)), "priority", false));
        editorWindow.closeCallback = CloseEditor; editorWindow.Show().Center();
    }
    internal void NativeStorageChanged(GameItem? owner = null)
    {
        if (owner == null || ReferenceEquals(owner, displayedPart)) RefreshPartLater();
    }
    private void RefreshPartLater()
    {
        if (displayedPart == null || partRefreshQueued) return;
        partRefreshQueued = true;
        LetterboxManager.RunNextUpdate(() => {
            partRefreshQueued = false;
            if (displayedPart == null || ItemMouseDragHandler.current?.IsDraggingItem == true) return;
            if (!ReferenceEquals(displayedPart.parentInventory, MainInventory()) || !GeneralHelper.IsItemEffectivelyOwned(displayedPart)) { ClosePart(); return; }
            if (filterGrid != null) RenderFilters();
            else if (displayedPart.identifier == "deepspaceae.core")
            {
                var position = partWindow?.rectTransform.position;
                ShowCore(displayedPart);
                if (position.HasValue && partWindow != null) { partWindow.rectTransform.position = position.Value; partWindow.FixBounds(); }
            }
        });
    }
}
