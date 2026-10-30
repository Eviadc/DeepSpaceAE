using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;

namespace DeepSpaceAE.GameAdapter;

public sealed class ModEntry : IMod
{
    internal const string ModId = "deepspaceae";
    internal static readonly (string Id, string NameKey, string Sprite, NetworkPart Part)[] Parts = {
        ("deepspaceae.core", "item_core_name", "core", NetworkPart.Core),
        ("deepspaceae.pipeline", "item_pipeline_name", "pipeline", NetworkPart.Pipeline),
        ("deepspaceae.item_storage_bus", "item_bus_name", "item-storage-bus", NetworkPart.ItemStorageBus),
        ("deepspaceae.terminal", "item_terminal_name", "terminal", NetworkPart.Terminal) };
    internal static readonly Dictionary<string, NetworkPart> PartKinds = Parts.ToDictionary(part => part.Id, part => part.Part, StringComparer.Ordinal);
    internal static GameSession? ActiveSession { get; private set; }
    private ModLog log = null!;
    private GameSession session = null!;

    public void Init(ModManifest manifest)
    {
        log = new ModLog(manifest); // Manifest Name is ASCII; keep official metadata intact.
        session = new GameSession(log);
        ActiveSession = session;
        log.Log("DeepSpaceAE 0.3.1 / 100-REV3 initialized.");
    }

    public void OnEnable()
    {
        ActiveSession = session;
        ModHook.OnModItemDirectoryInit += Register;
        ModHook.OnGameLoadedEarly += Reset;
        ModHook.OnGameLoadedLate += AttachInventory;
        ModHook.OnModAssetsLoaded += AssetsLoaded;
        ModHook.OnGoingSleepEarly += session.Night;
        ModHook.OnLootTablesLoaded += LootLoaded;
        log.Log("Enabled.");
    }

    public void OnDisable()
    {
        ModHook.OnModItemDirectoryInit -= Register;
        ModHook.OnGameLoadedEarly -= Reset;
        ModHook.OnGameLoadedLate -= AttachInventory;
        ModHook.OnModAssetsLoaded -= AssetsLoaded;
        ModHook.OnGoingSleepEarly -= session.Night;
        ModHook.OnLootTablesLoaded -= LootLoaded;
        foreach (var group in lootGroups) ModHelper.RemoveLootGroupEntry(ModId, group, "deepspaceae.scavenging");
        lootGroups.Clear();
        session.Close();
        session.Visuals.Detach();
        ActiveSession = null;
    }

    private void AssetsLoaded(LoadedMod mod)
    {
        if (mod.Manifest.ID != ModId) return;
        session.Visuals.AssetsLoaded(mod);
        log.Log("Assets ready: sprites=" + mod.Sprites.Count + "; locales=" + mod.Localization.Count + ".");
    }

    private void Reset()
    {
        session.Reset();
    }

    private void AttachInventory() { session.Visuals.Attach(); session.DayStarted(); }

    private void Register(ModItemDirectory directory)
    {
        foreach (var part in Parts)
        {
            var definition = part;
            directory.Add(definition.Id, () => {
                var item = ItemDirectory.CreateEmptyItem(definition.Id);
                item.name = UiText.Get(definition.NameKey);
                item.shortDescription = definition.Part == NetworkPart.ItemStorageBus ? UiText.Get("item_bus_desc") : UiText.Get("item_part_desc");
                item.flavorText = UiText.Get("item_flavor");
                item.unitValue = 1;
                item.SetSpriteAndShapeFromMod(ModId, definition.Sprite + "-off");
                // Intentional custom part definition, not a guess about other inventory shapes.
                int size = definition.Part == NetworkPart.Core ? 3 : 1;
                item.SetShape(new GridShapeBuilder(size, size).SetDataFill(1).Build());
                item.mayStackItemFunc = (_, _) => false;
                // Native default activation runs on single press. All AE windows use the
                // verified DoubleClickAction patch instead, regardless of single-click settings.
                item.activateDefault = false;
                item.mayActivateSlotItemFunc = (_, _, _) => false;
                item.canActivateSlotItemFunc = (_, _, _) => false;
                PartStorage.Create(item);
                return item;
            });
        }
        foreach (var entry in new[] { (PartStorage.Capacity, "item_capacity_name", "capacity-upgrade"),
            (PartStorage.Tag, "item_tag_name", "tag-upgrade"), (PartStorage.Wrench, "item_wrench_name", "wrench") })
        {
            var definition = entry;
            directory.Add(definition.Item1, () => {
                var item = ItemDirectory.CreateEmptyItem(definition.Item1);
                item.name = UiText.Get(definition.Item2); item.unitValue = 20;
                item.SetSpriteAndShapeFromMod(ModId, definition.Item3);
                item.SetShape(new GridShapeBuilder(1, 1).SetDataFill(1).Build());
                item.mayStackItemFunc = (_, _) => false;
                item.shortDescription = definition.Item1 == PartStorage.Capacity ? UiText.Get("item_capacity_desc") :
                    definition.Item1 == PartStorage.Tag ? UiText.Get("item_tag_desc") : UiText.Get("item_wrench_desc");
                if (definition.Item1 == PartStorage.Wrench)
                {
                    item.SetGameItemType("TOOL");
                    item.maySelectSlotItemFunc = (_, _, _) => true;
                    item.canSelectSlotItemFunc = (_, _, _) => true;
                    item.mayThisTargetItemFunc = (_, target) => target.identifier == "deepspaceae.item_storage_bus";
                    item.canThisTargetItemFunc = (_, target) => target.identifier == "deepspaceae.item_storage_bus" && PartStorage.Inventory(target).childItems.Count > 0;
                    item.onThisTargetItemFunc = (tool, target) => session.UseWrench(tool, target);
                }
                return item;
            });
        }
        log.Log("Registration complete: 7 AE item factories.");
    }

    private readonly HashSet<string> lootGroups = new(StringComparer.Ordinal);
    private void LootLoaded()
    {
        const string table = "deepspaceae.scavenging";
        if (!LootRegistry.HasTable(table)) ModHelper.RegisterLootTable(ModId, table, ("deepspaceae.core", 1f), ("deepspaceae.pipeline", 8f),
            ("deepspaceae.item_storage_bus", 3f), ("deepspaceae.terminal", 2f),
            (PartStorage.Capacity, 2f), (PartStorage.Tag, 1f), (PartStorage.Wrench, 3f));
        foreach (var group in LootRegistry.GroupIds.ToArray())
            if (group.StartsWith("POI_", StringComparison.Ordinal) && lootGroups.Add(group))
                ModHelper.AddLootGroupEntry(ModId, group, table, 20f);
        if (lootGroups.Count == 0) log.Warning("No POI loot groups found; AE scavenging drops are unavailable.");
        else log.Log("AE scavenging loot registered in " + lootGroups.Count + " POI groups.");
    }
}
