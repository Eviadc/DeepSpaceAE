using System;
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
    private ModLog log = null!;
    private GameSession session = null!;

    public void Init(ModManifest manifest)
    {
        log = new ModLog(manifest);
        session = new GameSession(log);
        log.Log("DeepSpaceAE 0.1.0 / 100-REV3 test build initialized.");
    }

    public void OnEnable()
    {
        ModHook.OnModItemDirectoryInit += Register;
        ModHook.OnGameLoadedEarly += Reset;
        ModHelper.AddDebugFunction(ModId, ModHelper.DEBUG_CATEGORY_SPAWN, "AE: Spawn kit", SpawnKit);
        ModHelper.AddDebugFunction(ModId, ModHelper.DEBUG_CATEGORY_SPECIAL, "AE: Open terminal", OpenTerminal);
        log.Log("Enabled. Load a TEST save, then use F9 > Spawn > AE: Spawn kit. Native writes default OFF.");
    }

    public void OnDisable()
    {
        ModHook.OnModItemDirectoryInit -= Register;
        ModHook.OnGameLoadedEarly -= Reset;
        ModHelper.RemoveDebugFunctions(ModId);
        session.Close();
    }

    private void Reset() => session.Reset();

    private void Register(ModItemDirectory directory)
    {
        foreach (var part in Parts)
        {
            var definition = part;
            directory.Add(definition.Id, () => {
                var item = ItemDirectory.CreateEmptyItem(definition.Id);
                item.name = definition.Name;
                item.shortDescription = "DeepSpaceAE 测试部件；放在主物品栏，侧边接触核心或管线。终端/总线可激活。";
                item.flavorText = "0.1.0 测试版：频道默认核心32、管线8。禁用前先移除全部 AE 部件。";
                item.unitValue = 1;
                item.SetSpriteAndShapeFromMod(ModId, definition.Sprite);
                // Intentional custom part definition, not a guess about other inventory shapes.
                item.SetShape(new GridShapeBuilder(1, 1).SetDataFill(1).Build());
                item.mayStackItemFunc = (_, _) => false;
                if (definition.Part != NetworkPart.Pipeline)
                {
                    item.activateDefault = true;
                    item.mayActivateSlotItemFunc = (_, _, _) => true;
                    item.canActivateSlotItemFunc = (_, _, _) => true;
                    item.onActivateSlotItemFunc = (self, _, _) => session.Open(self);
                }
                return item;
            });
        }
        log.Log("Registered four AE parts.");
    }

    private void SpawnKit()
    {
        try
        {
            var root = PlayerStore.Instance?.GetStoreInventory();
            if (root == null) { log.Warning("Load a test save before spawning AE parts."); return; }
            int placed = 0;
            foreach (var part in Parts)
                for (int i = 0; i < (part.Part == NetworkPart.Pipeline ? 6 : 1); i++)
                {
                    var item = ModHelper.SetItemOwnerShip(ModHelper.SpawnItem(part.Id), true);
                    if (GraphUtils.TryAcceptAllOrDestroy(root, item, "DeepSpaceAE spawn kit")) placed++;
                }
            log.Log($"Spawn kit: {placed}/9 parts placed. Arrange them in the MAIN inventory; no space means that new part is not kept.");
        }
        catch (Exception error) { log.Error(error.ToString()); }
    }

    private void OpenTerminal()
    {
        var terminal = PlayerStore.Instance?.GetStoreInventory()?.childItems.FirstOrDefault(i => i.identifier == "deepspaceae.terminal");
        if (terminal == null) { log.Warning("Place an AE terminal in the main inventory first."); return; }
        session.Open(terminal);
    }
}
