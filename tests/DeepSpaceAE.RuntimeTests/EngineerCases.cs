using System;
using System.Collections.Generic;
using System.Linq;
namespace DeepSpaceAE.RuntimeTests;

public sealed partial class RuntimePilot
{
    private const string Product = "deepspaceae", Upgrade = "deepspaceae.ae_engineer";
    private void StartRegistry()
    {
        var scenario = StartingScenario.Get(ExpectedScenario);
        Check(scenario != null && scenario.source == Product && StartingScenario.GetVisible().Any(s => s.id == ExpectedScenario), "Production start is absent from the native visible registry.");
        Check(!string.IsNullOrEmpty(scenario!.GetTitle()) && scenario.GetStartWith().Count >= 3 && scenario.CanStart(), "Native start metadata is unavailable.");
        Check(report.freshScenarioConfirmed && !config.existingSlots.Contains(report.saveSlot), "Start did not enter its fresh native slot.");
        details = "Native visible scenario registered with localized title: " + scenario.GetTitle() + "; real new-game callback and fresh slot confirmed.";
    }
    private void StartSupplies()
    {
        var expected = new Dictionary<string, int> { ["core"] = 1, ["pipeline"] = 8, ["item_storage_bus"] = 2, ["terminal"] = 1,
            ["disk_drive"] = 1, ["disk_small"] = 2, ["wrench"] = 1 };
        foreach (var pair in expected)
            Check(root.childItems.Count(i => i.identifier == Product + "." + pair.Key) == pair.Value, "AE start quantity mismatch: " + pair.Key);
        Check(root.childItems.Count(i => i.identifier.StartsWith(Product + ".", StringComparison.Ordinal)) == 16, "Unexpected extra AE components.");
        foreach (var pair in new[] { ("storage_bay",1), ("processed_meat",2), ("processed_juice",1), ("toothpaste",2), ("toilet_paper",2), ("sign_food",1), ("sign_household",1) })
            Check(root.childItems.Count(i => i.identifier == pair.Item1) >= pair.Item2, "Missing native generalist supply: " + pair.Item1);
        var battery = root.childItems.FirstOrDefault(i => i.identifier == "energy_credit");
        Check(battery != null, "Starting power source missing.");
        int maximum = battery!.GetTagReadonly(PowerHelper.POWER_SOURCE_ITEM_MAX_ENERGY).GetInt();
        Check(maximum > 0 && PowerHelper.GetAvailableEnergyFromItem(battery, false) == maximum, "Starting battery is not full.");
        Check(root.childItems.Where(i => i.identifier.StartsWith(Product + ".", StringComparison.Ordinal) || ReferenceEquals(i,battery)).All(i => GeneralHelper.IsItemEffectivelyOwned(i)), "Start supplied unowned items.");
        Check(ModHelper.GetModDataInt(Product,"gift_cursor",-1) == 27 && ModHelper.GetModDataInt(Product,"gift_inflight",-1) == 0, "Starting delivery did not finish with the verified skipped-intro loadout.");
        details = "Real main inventory: exact 16-piece AE kit; native generalist supplies/signs; fully charged power source=" + maximum + " kJ; journal 27/27; owned real instances.";
    }
    private void StartNoDuplicate()
    {
        Check(ModHelper.IsNetworkUpgradeUnlocked(Upgrade), "Wilde AE Engineer gift is not marked claimed.");
        var ids = root.childItems.Select(i => i.GetUniqueID()).OrderBy(i => i).ToArray(); int cash = PlayerStore.Instance.playerCash;
        StartingScenario.Get(ExpectedScenario).onStart();
        Check(PlayerStore.Instance.playerCash == cash, "Repeated scenario callback charged start cash again.");
        ModHelper.GetNetworkUpgrade(Upgrade).Unlock(); // Native callback must not replay the completed entitlement.
        Check(ids.SequenceEqual(root.childItems.Select(i => i.GetUniqueID()).OrderBy(i => i)), "Start/upgrade callback duplicated or replaced stock.");
        Check(ModHelper.GetModDataInt(Product,"gift_cursor",-1) == 27 && ModHelper.GetModDataInt(Product,"gift_inflight",-1) == 0, "Completed journal was reset.");
        details = "Native upgrade is already claimed; repeated native scenario and unlock callbacks do not re-charge or replace/duplicate real stock.";
    }
    private void StartPower()
    {
        var core = root.childItems.Single(i => i.identifier == Product + ".core");
        var source = root.childItems.First(i => i.identifier == "energy_credit");
        int energy = PowerHelper.GetAvailableEnergyFromItem(source, false);
        Adapter.Move(source, (GameInventory)Adapter.Static("PartStorage","Inventory",core)!);
        var powered = (HashSet<string>)Adapter.Field(Adapter.Board(),"Powered")!;
        Check(powered.Contains(Adapter.Id(core)) && source.MaxNumRemove() == 0, "Starting battery did not supply/lock in the real controller.");
        Check(PowerHelper.GetAvailableEnergyFromItem(source, false) == energy, "Starting reservation debited battery early.");
        details = "Granted real battery enters the granted controller and supplies its actual network; reservation locks without early debit. No mouse input or night settlement.";
    }
}
