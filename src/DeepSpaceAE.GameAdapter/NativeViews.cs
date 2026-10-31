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
    private BorrowedInventoryView? borrowedStorage;
    private GameItem? displayedPart;
    private View? displayedView;
    private IReadOnlyList<CatalogRecord>? packedRows;
    private VirtualInventoryLayout? packing;
    private MachineryText? terminalStatus;
    private MachineryText? pageLabel;
    private NativeWidgets.Field? tagField;
    private string[] displayedTags = Array.Empty<string>();
    private bool partRefreshQueued;
    private readonly Dictionary<string, Sprite> filterSprites = new(StringComparer.Ordinal);

    private void CloseTerminal()
    {
        if (NativeDrag.Gesture != null && ReferenceEquals(NativeDrag.Gesture.Surface.Owner, openTerminal)) NativeDrag.Gesture.Cancelled = true;
        terminalGrid?.ClearTiles();
        Unobserve(); displayedView = null; terminalGrid = null; packedRows = null; packing = null;
        terminalStatus = null; pageLabel = null; tagField = null; displayedTags = Array.Empty<string>();
        if (terminalWindow != null) { var w = terminalWindow; terminalWindow = null; w.Hide(); UnityEngine.Object.Destroy(w.handler.gameObject); }
    }
    private void ClosePart()
    {
        if (editorWindow != null) { var w = editorWindow; editorWindow = null; w.Hide(); UnityEngine.Object.Destroy(w.handler.gameObject); }
        borrowedStorage?.Return(); borrowedStorage = null; displayedPart = null; filterGrid = null;
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
        CloseNotice(); noticeWindow = MachineHelper.CreateMachineryWindow(true, MachineHelper.Panel(UiText.Get("ui_status"), MachineryStack.Vertical(
            MachineryText.Status("", text), new MachineryButton(UiText.Get("ui_close"), CloseNotice)), "notice", false));
        noticeWindow.closeCallback = CloseNotice; noticeWindow.Show().Center();
    }
    private PixelElement Borrow(GameItem owner)
    {
        var storage = (PixelElement)PartStorage.Inventory(owner);
        if (storage is GameSlotInventory slot) slot.SetMachineryStyle();
        if (storage is GameGridInventory grid) grid.SetMachineryStyle();
        borrowedStorage = new BorrowedInventoryView(storage);
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
            var query = NativeWidgets.Input(UiText.Get("ui_search"), search, value => { if (search == value) return; search = value; page = 0; RenderTerminal(); }, 150);
            tagField = NativeWidgets.Dropdown(new[] { UiText.Get("ui_all_categories") }, 0, i => { tag = i > 0 && i <= displayedTags.Length ? displayedTags[i - 1] : ""; page = 0; RenderTerminal(); }, 100);
            var sortField = NativeWidgets.Dropdown(new[] { UiText.Get("ui_native_order"), UiText.Get("ui_name"), UiText.Get("ui_quantity") }, (int)sort, i => { sort = (CatalogSort)i; page = 0; RenderTerminal(); }, 70);
            terminalStatus = NativeWidgets.Text("").SetFillWidth(false);
            pageLabel = NativeWidgets.Text("1 / 1").SetFillWidth(false);
            var paging = MachineryStack.Horizontal(new MachineryButton("<", () => { page = Math.Max(0, page - 1); RenderTerminal(); }, 20), pageLabel,
                new MachineryButton(">", () => { if (packing != null && page + 1 < packing.PageCount) page++; RenderTerminal(); }, 20));
            var searchBar = MachineryStack.Horizontal(query, tagField, sortField).SetMainAlign(MachineryStack.Align.Start);
            var body = MachineryStack.Vertical(searchBar, new MachineryIntake(terminalGrid),
                MachineryStack.Horizontal(terminalStatus, paging));
            terminalWindow = MachineHelper.CreateMachineryWindow(true, new MachineryPanel(UiText.Get("ui_network"), body, "terminal"));
            terminalWindow.SetTitle(UiText.Get("ui_terminal_title")); terminalWindow.closeCallback = CloseTerminal;
        }
        var tags = view.Search.Tags.ToArray();
        if (!displayedTags.SequenceEqual(tags) || create)
        {
            displayedTags = tags;
            int selected = Array.IndexOf(tags, tag) + 1; if (selected == 0) tag = "";
            tagField!.Control.SetOptions(new[] { UiText.Get("ui_all_categories") }.Concat(tags.Select(NativeWidgets.TagName)).ToArray()).SetIndex(selected);
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
        terminalStatus!.SetText(writeFault ? UiText.Get("ui_transfer_locked") : string.IsNullOrEmpty(message) ? UiText.Get("ui_item_count", rows.Count) : message);
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
            var icon = NativeWidgets.Icon(NativeWidgets.Sprite(definition.Sprite), 32, 32,
                UiText.Get(definition.NameKey) + UiText.Get("ui_part_stat", counts[column], NetworkEvaluation.NightCost(kind)));
            stats.AttachPos(icon, column, 0);
            stats.AttachPos(NativeWidgets.Text("× " + counts[column]), column, 1);
        }
        int used = board.Evaluation.ChannelsUsed.TryGetValue(Id(core), out var u) ? u : 0;
        var power = MachineryStack.Horizontal(NativeWidgets.Icon(MachineryStyle.IconPower), MachineryText.Status(UiText.Get("ui_nightly"), cost + " kJ"));
        int remaining = battery == null ? 0 : PowerHelper.GetAvailableEnergyFromItem(battery, false);
        bool infinite = battery != null && HypertechBatteryHelper.IsHypertechBattery(battery);
        int maximum = battery == null || infinite ? 0 : battery.GetTagReadonly(PowerHelper.POWER_SOURCE_ITEM_MAX_ENERGY).GetInt();
        var bar = new MachineryProgressBar(infinite ? 1 : Math.Min(1, (float)remaining / Math.Max(1, maximum)), 120);
        string batteryState = battery == null ? UiText.Get("ui_insert_battery") : lease.Phase == NightPhase.Fault || lease.Phase == NightPhase.DebitStarted ? UiText.Get("ui_billing_unknown") : lease.CanRemove(PartStorage.Day) ? UiText.Get("ui_replaceable") : UiText.Get("ui_battery_locked");
        var overview = MachineryStack.Vertical(stats, power, bar,
            MachineryText.Status(UiText.Get("ui_energy"), infinite ? UiText.Get("ui_hypertech_battery") : remaining + " / " + maximum + " kJ"),
            MachineryText.Status(UiText.Get("ui_reserved"), (lease.Phase == NightPhase.Reserved ? lease.Required : 0) + " kJ"),
            MachineryText.Status(UiText.Get("ui_channels"), used + " / 32"), NativeWidgets.Text(batteryState));
        partWindow = MachineHelper.CreateMachineryWindow(true,
            MachineHelper.IconPanel(MachineryStyle.IconPower, Borrow(core), "power"),
            MachineHelper.Panel(UiText.Get("ui_controller"), overview, "network", false));
        partWindow.SetTitle(UiText.Get("ui_controller_title")); partWindow.closeCallback = ClosePart; partWindow.Show().Center();
    }
    private void ShowCable(GameItem cable)
    {
        ClosePart(); displayedPart = cable;
        var board = ReadBoard();
        bool online = board.Evaluation.ComponentCore.TryGetValue(Id(cable), out var core) && board.Powered.Contains(core);
        partWindow = MachineHelper.CreateMachineryWindow(true, MachineHelper.Panel(UiText.Get("ui_cable"), MachineryStack.Vertical(
            NativeWidgets.Icon(NativeWidgets.Sprite("pipeline")), MachineryText.Status(UiText.Get("ui_status"), online ? UiText.Get("ui_online") : UiText.Get("ui_inactive")),
            MachineryText.Status(UiText.Get("ui_channels"), board.Evaluation.ChannelsUsed[Id(cable)] + " / 8")), "cable", false));
        partWindow.closeCallback = ClosePart; partWindow.Show().Center();
    }
    private void ShowBus(GameItem bus)
    {
        ClosePart(); displayedPart = bus;
        var policy = PartStorage.Policy(bus);
        filterGrid = new VirtualInventory(bus, 9, policy.Slots / 9, true);
        filterGrid.ConfigureFilter = (index, item) => CaptureGhost(bus, index, item);
        filterGrid.Redraw = RenderFilters;
        var access = NativeWidgets.Dropdown(new[] { UiText.Get("ui_access_both"), UiText.Get("ui_access_extract"), UiText.Get("ui_access_insert") },
            (int)policy.Access, value => ConfigureNumber(bus, "access", value), 110);
        var matching = NativeWidgets.Dropdown(new[] { UiText.Get("ui_include"), UiText.Get("ui_exclude") }, policy.Exclude ? 1 : 0,
            value => ConfigureNumber(bus, "exclude", value), 110);
        var extraction = NativeWidgets.Toggle(UiText.Get("ui_filter_extract"), policy.FilterExtraction,
            value => ConfigureNumber(bus, "extract_filter", value ? 1 : 0), 110);
        var options = MachineryStack.Vertical(NativeWidgets.Text(UiText.Get("ui_access_mode")), access,
            NativeWidgets.Text(UiText.Get("ui_filter_matching")), matching, extraction,
            new MachineryButton(UiText.Get("ui_priority"), () => ShowPriority(bus), 110, 18));
        partWindow = MachineHelper.CreateMachineryWindow(true,
            new MachineryPanel(UiText.Get("ui_bus_settings"), options, "settings"),
            MachineHelper.Panel(policy.Tags ? UiText.Get("ui_category_filter") : UiText.Get("ui_item_filter"), filterGrid, "storage_bus"),
            new MachineryPanel(UiText.Get("ui_upgrades"), Borrow(bus), "upgrades"));
        partWindow.SetTitle(UiText.Get("ui_bus_title")); partWindow.closeCallback = ClosePart;
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
            string name = string.IsNullOrEmpty(key) ? UiText.Get("ui_set_filter") : policy.Tags ? NativeWidgets.TagName(key) : FilterName(bus, slot, key);
            filterGrid.GhostTile(i, string.IsNullOrEmpty(key) ? null : sprite, name + UiText.Get("ui_click_clear"), () => { SetFilter(bus, slot, ""); RenderFilters(); });
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
        editorWindow = MachineHelper.CreateMachineryWindow(true, MachineHelper.Panel(UiText.Get("ui_choose_category"), MachineryStack.Vertical(
            NativeWidgets.Dropdown(tags.Select(NativeWidgets.TagName).ToArray(), 0, i => chosen = i, 140),
            new MachineryButton(UiText.Get("ui_confirm"), () => { SetFilter(bus, slot, tags[chosen]); CloseEditor(); RenderFilters(); })), "tag", false));
        editorWindow.closeCallback = CloseEditor; editorWindow.Show().Center();
    }
    private void CloseEditor()
    { if (editorWindow != null) { var w = editorWindow; editorWindow = null; w.Hide(); UnityEngine.Object.Destroy(w.handler.gameObject); } }
    private void ShowPriority(GameItem bus)
    {
        CloseEditor(); var field = NativeWidgets.Input(UiText.Get("ui_priority"), PartStorage.Policy(bus).Priority.ToString(), v => { if (int.TryParse(v, out var n)) ConfigureNumber(bus, "priority", n); }, 110);
        void Change(int delta)
        {
            long next = (long)PartStorage.Policy(bus).Priority + delta;
            ConfigureNumber(bus, "priority", (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, next))); field.Control.SetString(PartStorage.Policy(bus).Priority.ToString());
        }
        editorWindow = MachineHelper.CreateMachineryWindow(true, MachineHelper.Panel(UiText.Get("ui_priority"), MachineryStack.Vertical(field,
            MachineryStack.Horizontal(new MachineryButton("-10", () => Change(-10)), new MachineryButton("-1", () => Change(-1)),
            new MachineryButton("+1", () => Change(1)), new MachineryButton("+10", () => Change(10))), new MachineryButton(UiText.Get("ui_back"), CloseEditor)), "priority", false));
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
