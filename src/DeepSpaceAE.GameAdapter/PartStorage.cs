using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DeepSpaceAE.Core.Network;

namespace DeepSpaceAE.GameAdapter;

internal static partial class PartStorage
{
    internal const string Capacity = "deepspaceae.capacity_upgrade", Tag = "deepspaceae.tag_upgrade", Wrench = "deepspaceae.wrench";
    private sealed class Slots
    {
        internal GameInventory Inventory = null!;
        internal bool RemovingUpgrade;
    }
    private static readonly ConditionalWeakTable<GameItem, Slots> slots = new();
    internal static GameInventory Inventory(GameItem item) => slots.GetValue(item, _ => throw new InvalidOperationException("Missing AE storage graph.")).Inventory;
    internal static List<GameItem> CableBuses(GameItem cable) => Inventory(cable).childItems;
    internal static GameItem? BusHost(GameItem bus)
    {
        var owner = bus.parentInventory?.GetParentItem();
        return owner?.identifier == "deepspaceae.pipeline" ? owner : null;
    }
    internal static bool CanConfigure(GameItem bus)
    {
        var host = BusHost(bus);
        return host != null && ReferenceEquals(host.parentInventory, GameSession.MainInventory())
            && GeneralHelper.IsItemEffectivelyOwned(host) && GeneralHelper.IsItemEffectivelyOwned(bus);
    }
    internal static int Day => StoreStation.GetDayCounter();
    internal static TagState? Read(GameItem item, string key) => item.state.dict.TryGetValue("deepspaceae." + key, out var value) ? value : null;
    internal static int Int(GameItem item, string key, int fallback = 0) => Read(item, key)?.GetInt() ?? fallback;
    internal static string Text(GameItem item, string key) => Read(item, key)?.GetString() ?? "";
    internal static void Number(GameItem item, string key, int value) => item.ModifyTag("deepspaceae." + key, t => t.Enable().SetInt(value), true);
    internal static void String(GameItem item, string key, string value) => item.ModifyTag("deepspaceae." + key, t => t.Enable().SetString(value), true);
    internal static string[] Filters(GameItem item)
    {
        var values = Text(item, Inventory(item).childItems.Any(c => c.identifier == Tag) ? "tag_filters" : "filters").Split('\n');
        var result = new string[63];
        for (int i = 0; i < result.Length; i++) result[i] = i < values.Length ? values[i] : "";
        return result;
    }
    internal static StoragePolicy Policy(GameItem bus)
    {
        var cards = Inventory(bus).childItems;
        bool tags = cards.Any(i => i.identifier == Tag);
        int capacity = Math.Min(tags ? 4 : 5, cards.Count(i => i.identifier == Capacity));
        return new StoragePolicy(Int(bus, "priority"), (StorageAccess)Math.Max(0, Math.Min(2, Int(bus, "access"))), tags,
            Int(bus, "exclude") != 0, Int(bus, "extract_filter", 1) != 0, capacity, Filters(bus));
    }
    internal static string[] Tags(GameItem item) => item.modifiedState.dict.Where(p => p.Value.valueEnabled && !p.Key.StartsWith("deepspaceae.", StringComparison.Ordinal))
        .Select(p => p.Key).Concat(item.GetGameItemType()).Distinct(StringComparer.Ordinal).ToArray();

    internal static void Create(GameItem item)
    {
        if (item.identifier == "deepspaceae.core")
        {
            var slot = new GameSlotInventory();
            slots.Add(item, new Slots { Inventory = slot });
            EnergyReceiver.RegisterHost(item);
            // Same native battery backdrop and audio as MachineHelper.SetupBatterySlot,
            // without registering a second native machinery power consumer.
            slot.SetBackgroundFadeSprite("Items/items_tool", "battery100");
            slot.onSlotAddItemFunc += (_, _, _) => AudioManager.Instance.Play3("battery_insert");
            slot.onSlotRemoveItemFunc += (_, _, _) => AudioManager.Instance.Play3("battery_removed");
            slot.mayInventoryAddItemFunc = (battery, _) => ReferenceEquals(slot.childItem,battery)
                || slot.childItem==null && CanInstall(item, battery);
            slot.onSlotRemoveItemEndFunc += (_, _, _) => { ModEntry.ActiveSession?.Visuals.Invalidate(); ModEntry.ActiveSession?.NativeStorageChanged(item,true); };
            slot.maxSlotRemoveItemFunc = (_, _, _) => CanRemovePower(item) ? int.MaxValue : 0;
            slot.onSlotAddItemEndFunc += (battery, _, _) => {
                if (ModEntry.ActiveSession?.Loading != false || item.parentInventory == null) { RestoreLocks(item); return; }
                int cost = ModEntry.ActiveSession.CoreCost(item);
                if(UsesPool(item)) { RestoreLocks(item); ModEntry.ActiveSession.Visuals.Invalidate(); return; }
                var lease = Lease(item);
                if (!lease.Reserve(Day, cost, battery.GetUniqueID(), n => CanSupply(battery, n)))
                    throw new InvalidOperationException("Battery reservation failed after native insertion.");
                Save(item, lease); Number(item, "power_policy_revision", 3); slot.LockInv();
                ModEntry.ActiveSession?.Visuals.Invalidate(); ModEntry.ActiveSession?.NativeStorageChanged(item,true);
            };
            var window = new PixelWindow(true, UiText.Get("ui_battery_data")); window.Attach(slot); item.SetContentWindow(window);
        }
        else if (item.identifier == "deepspaceae.pipeline")
        {
            var grid = new GameGridInventory(4, 1); var data = new Slots { Inventory = grid }; slots.Add(item, data);
            grid.mayInventoryAddItemFunc = (bus, _) => ReferenceEquals(item.parentInventory, GameSession.MainInventory())
                && GeneralHelper.IsItemEffectivelyOwned(item) && GeneralHelper.IsItemEffectivelyOwned(bus)
                && IoBus.IsBus(bus) && bus.unitCount == 1 && grid.childItems.Count < 4;
            grid.maxSlotRemoveItemFunc = (_, _, _) => data.RemovingUpgrade ? int.MaxValue : 0;
            grid.mayInventoryRemoveItemFunc = (_, _) => data.RemovingUpgrade;
            grid.onSlotAddItemEndFunc += (bus, _, _) => {
                if (ModEntry.ActiveSession?.Loading == false)
                {
                    int installed = 0;
                    foreach (var existing in grid.childItems.Where(b => !ReferenceEquals(b, bus)))
                    { int face = Int(existing, "cable_face", -1); if (face >= 0 && face < 4) installed |= 1 << face; }
                    var layout = new InventoryLayout(GameSession.MainInventory()!);
                    int chosen = CablePorts.ChooseFace(installed, layout.CableMask(item));
                    if (chosen < 0) throw new InvalidOperationException("Cable has no free bus face.");
                    Number(bus, "cable_face", chosen);
                }
                ModEntry.ActiveSession?.Visuals.Invalidate(); ModEntry.ActiveSession?.NativeStorageChanged(item,true);
            };
            grid.onSlotRemoveItemEndFunc += (bus, _, _) => {
                if (ModEntry.ActiveSession?.Loading == false) Number(bus, "cable_face", -1);
                ModEntry.ActiveSession?.Visuals.Invalidate(); ModEntry.ActiveSession?.NativeStorageChanged(item,true);
            };
            var window = new PixelWindow(true, UiText.Get("ui_cable_bus_data")); window.Attach(grid); item.SetContentWindow(window);
        }
        else if (item.identifier == "deepspaceae.terminal")
        {
            var slot = new GameSlotInventory(); slots.Add(item, new Slots { Inventory = slot });
            slot.SetMachineryStyle();
            slot.mayInventoryAddItemFunc = (card, _) => (slot.childItem == null || ReferenceEquals(slot.childItem, card))
                && card.identifier == Tag && card.unitCount == 1 && GeneralHelper.IsItemEffectivelyOwned(card)
                && GeneralHelper.IsItemEffectivelyOwned(item) && ReferenceEquals(item.parentInventory, GameSession.MainInventory());
            slot.onSlotAddItemEndFunc += (_, _, _) => ModEntry.ActiveSession?.NativeStorageChanged(item);
            slot.onSlotRemoveItemEndFunc += (_, _, _) => ModEntry.ActiveSession?.NativeStorageChanged(item);
            var window = new PixelWindow(true, UiText.Get("ui_upgrade_data")); window.Attach(slot); item.SetContentWindow(window);
        }
        else if (IoBus.IsBus(item))
        {
            var grid = new GameGridInventory(5, 1);
            var data = new Slots { Inventory = grid };
            slots.Add(item, data);
            grid.mayInventoryAddItemFunc = (card, _) => CanConfigure(item) && GeneralHelper.IsItemEffectivelyOwned(card) && card.unitCount == 1
                && (card.identifier == Capacity || (card.identifier == Tag && !grid.childItems.Any(c => c.identifier == Tag)));
            grid.maxSlotRemoveItemFunc = (_, _, _) => data.RemovingUpgrade ? int.MaxValue : 0;
            grid.mayInventoryRemoveItemFunc = (_, _) => data.RemovingUpgrade;
            grid.onSlotAddItemEndFunc += (_, _, _) => { ModEntry.ActiveSession?.Visuals.Invalidate(); ModEntry.ActiveSession?.NativeStorageChanged(item,true); };
            grid.onSlotRemoveItemEndFunc += (_, _, _) => { ModEntry.ActiveSession?.Visuals.Invalidate(); ModEntry.ActiveSession?.NativeStorageChanged(item,true); };
            var window = new PixelWindow(true, UiText.Get("ui_upgrade_data")); window.Attach(grid); item.SetContentWindow(window);
        }
    }
    internal static void WithUpgradeRemoval(GameItem bus, Action action)
    {
        var data = slots.GetValue(bus, _ => throw new InvalidOperationException("Missing upgrade inventory."));
        data.RemovingUpgrade = true;
        try { action(); } finally { data.RemovingUpgrade = false; }
    }
}
