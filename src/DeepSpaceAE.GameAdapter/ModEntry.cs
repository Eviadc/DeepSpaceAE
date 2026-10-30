using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;

namespace DeepSpaceAE.GameAdapter;

public sealed class ModEntry : IMod
{
    internal const string ModId = "deepspaceae";
    internal static readonly (string Id, string Name, string Sprite, NetworkPart Part)[] Parts = {
        ("deepspaceae.core", "AE 核心", "core", NetworkPart.Core),
        ("deepspaceae.pipeline", "AE 管线", "pipeline", NetworkPart.Pipeline),
        ("deepspaceae.item_storage_bus", "AE 物品存储总线", "item-storage-bus", NetworkPart.ItemStorageBus),
        ("deepspaceae.terminal", "AE 存储终端", "terminal", NetworkPart.Terminal) };
    internal static readonly Dictionary<string, NetworkPart> PartKinds = Parts.ToDictionary(part => part.Id, part => part.Part, StringComparer.Ordinal);
    internal static GameSession? ActiveSession { get; private set; }
    private ModLog log = null!;
    private GameSession session = null!;

    public void Init(ModManifest manifest)
    {
        // ModLog only reads Name. Preserve the actual mod manifest and use an ASCII
        // log label so F8 does not generate a missing-glyph warning for every log line.
        log = new ModLog(new ModManifest { Name = "DeepSpaceAE" });
        session = new GameSession(log);
        ActiveSession = session;
        log.Log("DeepSpaceAE 0.3.0 / 100-REV3 initialized.");
    }

    public void OnEnable()
    {
        ActiveSession = session;
        ModHook.OnModItemDirectoryInit += Register;
        ModHook.OnGameLoadedEarly += Reset;
        ModHook.OnGameLoadedLate += AttachInventory;
        ModHook.OnModAssetsLoaded += session.Visuals.AssetsLoaded;
        ModHook.OnGoingSleepEarly += session.Night;
        ModHook.OnLootTablesLoaded += LootLoaded;
        log.Log("Enabled.");
    }

    public void OnDisable()
    {
        ModHook.OnModItemDirectoryInit -= Register;
        ModHook.OnGameLoadedEarly -= Reset;
        ModHook.OnGameLoadedLate -= AttachInventory;
        ModHook.OnModAssetsLoaded -= session.Visuals.AssetsLoaded;
        ModHook.OnGoingSleepEarly -= session.Night;
        ModHook.OnLootTablesLoaded -= LootLoaded;
        foreach (var group in lootGroups) ModHelper.RemoveLootGroupEntry(ModId, group, "deepspaceae.scavenging");
        lootGroups.Clear();
        session.Close();
        session.Visuals.Detach();
        ActiveSession = null;
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
                item.name = definition.Name;
                item.shortDescription = definition.Part == NetworkPart.ItemStorageBus ? "箭头面接容器，背面接线。双击设置优先级与筛选。" : "放置于主物品栏接入网络。双击打开。";
                item.flavorText = "深空中的应用能源。";
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
        foreach (var entry in new[] { (PartStorage.Capacity, "AE 容量升级", "capacity-upgrade"),
            (PartStorage.Tag, "AE 标签升级", "tag-upgrade"), (PartStorage.Wrench, "AE 扳手", "wrench") })
        {
            var definition = entry;
            directory.Add(definition.Item1, () => {
                var item = ItemDirectory.CreateEmptyItem(definition.Item1);
                item.name = definition.Item2; item.unitValue = 20;
                item.SetSpriteAndShapeFromMod(ModId, definition.Item3);
                item.SetShape(new GridShapeBuilder(1, 1).SetDataFill(1).Build());
                item.mayStackItemFunc = (_, _) => false;
                item.shortDescription = definition.Item1 == PartStorage.Capacity ? "安装至存储总线，每张增加 9 个筛选槽。" :
                    definition.Item1 == PartStorage.Tag ? "将总线筛选切换为标签。每条总线限一张。" : "对 AE 总线使用，拆卸一张升级卡。";
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
        log.Log("Registered AE components, upgrades and wrench.");
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
