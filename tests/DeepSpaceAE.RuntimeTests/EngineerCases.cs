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
            ["disk_drive"] = 1, ["disk_small"] = 2, ["wrench"] = 1,["tag_upgrade"]=1,["import_bus"]=1,["export_bus"]=1,["formation_bus"]=1,["annihilation_bus"]=1,["steve_contact"]=1,["wireless_connector"]=2 };
        var all=MainItems();
        foreach (var pair in expected)
            Check(all.Count(i => i.identifier == Product + "." + pair.Key) == pair.Value, "AE start quantity mismatch: " + pair.Key);
        Check(all.Count(i => i.identifier.StartsWith(Product + ".", StringComparison.Ordinal)) == 24, "Unexpected extra AE components/contact slip.");
        foreach (var pair in new[] { ("storage_bay",1), ("processed_meat",2), ("processed_juice",1), ("toothpaste",2), ("toilet_paper",2), ("sign_food",1), ("sign_household",1) })
            Check(root.childItems.Count(i => i.identifier == pair.Item1) >= pair.Item2, "Missing native generalist supply: " + pair.Item1);
        var plan=ModHelper.GetModData(Product,"gift_plan_v2","").Split('\n');
        Check(plan.Count(id=>id==Product+".wireless_connector")==2 && !plan.Contains("energy_credit"),"Grant plan did not replace the battery with a wireless pair.");
        Check(root.childItems.Where(i => i.identifier.StartsWith(Product + ".", StringComparison.Ordinal)).All(i => GeneralHelper.IsItemEffectivelyOwned(i)), "Start supplied unowned items.");
        Check(ModHelper.GetModDataInt(Product,"gift_cursor",-1) == 34 && ModHelper.GetModDataInt(Product,"gift_inflight",-1) == 0, "Starting delivery did not finish with the current skipped-intro loadout.");
        details = "Real inventory tree: exact 23-piece AE kit + contact slip including two wireless connectors; generalist supplies/signs; no battery in grant plan; cursor 34/34; owned real instances.";
    }
    private void StartNoDuplicate()
    {
        Check(ModHelper.IsNetworkUpgradeUnlocked(Upgrade), "Wilde AE Engineer gift is not marked claimed.");
        var ids = root.childItems.Select(i => i.GetUniqueID()).OrderBy(i => i).ToArray(); int cash = PlayerStore.Instance.playerCash;
        StartingScenario.Get(ExpectedScenario).onStart();
        Check(PlayerStore.Instance.playerCash == cash, "Repeated scenario callback charged start cash again.");
        ModHelper.GetNetworkUpgrade(Upgrade).Unlock(); // Native callback must not replay the completed entitlement.
        Check(ids.SequenceEqual(root.childItems.Select(i => i.GetUniqueID()).OrderBy(i => i)), "Start/upgrade callback duplicated or replaced stock.");
        Check(ModHelper.GetModDataInt(Product,"gift_cursor",-1) == 34 && ModHelper.GetModDataInt(Product,"gift_inflight",-1) == 0, "Completed cursor was reset.");
        details = "Native upgrade is already claimed; repeated native scenario and unlock callbacks do not re-charge or replace/duplicate real stock.";
    }
    private void StartPower()
    {
        var core = root.childItems.Single(i => i.identifier == Product + ".core");
        var source = Spawn("energy_credit"); Add(source);
        int energy = PowerHelper.GetAvailableEnergyFromItem(source, false);
        Adapter.Move(source, (GameInventory)Adapter.Static("PartStorage","Inventory",core)!);
        var powered = (HashSet<string>)Adapter.Field(Adapter.Board(),"Powered")!;
        bool charges=(bool)Adapter.Static("ModSettings","get_EnergyEnabled")!;
        Check(powered.Contains(Adapter.Id(core)) && (source.MaxNumRemove()==0)==charges, "Starting battery/free-power slot state is incorrect.");
        Check(PowerHelper.GetAvailableEnergyFromItem(source, false) == energy, "Starting reservation debited battery early.");
        details = "A separately spawned test battery enters the granted controller; optional reservation locks without early debit. The starting gift does not supply a battery.";
    }
}
