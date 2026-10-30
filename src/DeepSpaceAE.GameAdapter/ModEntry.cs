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
    private int spawnRequests;

    public void Init(ModManifest manifest)
    {
        // ModLog only reads Name. Preserve the actual mod manifest and use an ASCII
        // log label so F8 does not generate a missing-glyph warning for every log line.
        log = new ModLog(new ModManifest { Name = "DeepSpaceAE" });
        session = new GameSession(log);
        ActiveSession = session;
        log.Log("DeepSpaceAE 0.1.0 / 100-REV3 test build initialized.");
    }

    public void OnEnable()
    {
        ActiveSession = session;
        ModHook.OnModItemDirectoryInit += Register;
        ModHook.OnGameLoadedEarly += Reset;
        ModHook.OnGameLoadedLate += AttachInventory;
        ModHook.OnModAssetsLoaded += session.Visuals.AssetsLoaded;
        ModHelper.AddDebugFunction(ModId, ModHelper.DEBUG_CATEGORY_SPAWN, "AE: Spawn kit", SpawnKit);
        ModHelper.AddDebugFunction(ModId, ModHelper.DEBUG_CATEGORY_SPECIAL, "AE: Open terminal", OpenTerminal);
        ModHelper.AddDebugFunction(ModId, ModHelper.DEBUG_CATEGORY_SPECIAL, "AE: Diagnose", session.Diagnose);
        log.Log("Enabled. Load a TEST save, then use F9 > Spawn > AE: Spawn kit. Native writes default OFF.");
    }

    public void OnDisable()
    {
        ModHook.OnModItemDirectoryInit -= Register;
        ModHook.OnGameLoadedEarly -= Reset;
        ModHook.OnGameLoadedLate -= AttachInventory;
        ModHook.OnModAssetsLoaded -= session.Visuals.AssetsLoaded;
        ModHelper.RemoveDebugFunctions(ModId);
        session.Close();
        session.Visuals.Detach();
        ActiveSession = null;
        Diagnostics.Close();
    }

    private void Reset()
    {
        spawnRequests = 0;
        Diagnostics.Close();
        session.Reset();
    }

    private void AttachInventory() => session.Visuals.Attach();

    private void Register(ModItemDirectory directory)
    {
        foreach (var part in Parts)
        {
            var definition = part;
            directory.Add(definition.Id, () => {
                var item = ItemDirectory.CreateEmptyItem(definition.Id);
                item.name = definition.Name;
                item.shortDescription = "DeepSpaceAE 测试部件；主物品栏内双击打开，单击保留拖拽。总线箭头面接容器，背面接线。";
                item.flavorText = "0.1.0 测试版：频道默认核心32、管线8。禁用前先移除全部 AE 部件。";
                item.unitValue = 1;
                item.SetSpriteAndShapeFromMod(ModId, definition.Sprite);
                // Intentional custom part definition, not a guess about other inventory shapes.
                int size = definition.Part == NetworkPart.Core ? 3 : 1;
                item.SetShape(new GridShapeBuilder(size, size).SetDataFill(1).Build());
                item.mayStackItemFunc = (_, _) => false;
                // Native default activation runs on single press. All AE windows use the
                // verified DoubleClickAction patch instead, regardless of single-click settings.
                item.activateDefault = false;
                item.mayActivateSlotItemFunc = (_, _, _) => false;
                item.canActivateSlotItemFunc = (_, _, _) => false;
                return item;
            });
        }
        log.Log("Registered four AE parts.");
    }

    private void SpawnKit()
    {
        var request = ++spawnRequests;
        log.Log($"Spawn kit request #{request} received.");
        try
        {
            session.Visuals.Attach();
            var root = GameSession.MainInventory();
            if (root == null)
            {
                log.Warning("Load a test save before spawning AE parts.");
                Diagnostics.Show("DeepSpaceAE - Spawn kit", new[] { "Load a test save first. No parts spawned." });
                return;
            }
            var before = Diagnostics.PartCounts(root);
            int placed = 0;
            int nested = 0;
            foreach (var part in Parts)
                for (int i = 0; i < (part.Part == NetworkPart.Pipeline ? 6 : 1); i++)
                {
                    var item = ModHelper.SetItemOwnerShip(ModHelper.SpawnItem(part.Id), true);
                    if (GraphUtils.TryAcceptAllOrDestroy(root, item, "DeepSpaceAE spawn kit"))
                    {
                        if (ReferenceEquals(item.parentInventory, root)) placed++;
                        else nested++;
                    }
                }
            if (OverlayHandler.current != null) OverlayHandler.current.UpdateInventoryStateUI();
            var lines = new[] { $"Request #{request}: main={placed}; nested={nested}; not kept={9 - placed - nested}.",
                "MAIN before: " + before, "MAIN after: " + Diagnostics.PartCounts(root),
                "Native placement may put parts inside another container. Move them into MAIN to connect.",
                "Each click creates another kit. Use only ONE core in the connected test network." };
            log.Log(string.Join("\n", lines));
            Diagnostics.Show("DeepSpaceAE - Spawn kit result", lines);
        }
        catch (Exception error)
        {
            log.Error(Diagnostics.ConsoleText(error.ToString()));
            Diagnostics.Show("DeepSpaceAE - Spawn failed", new[] { error.Message,
                "Some parts may already exist. Check MAIN and nested containers; do not blindly repeat." });
        }
    }

    private void OpenTerminal()
    {
        var terminal = GameSession.MainInventory()?.childItems.FirstOrDefault(i => i.identifier == "deepspaceae.terminal");
        if (terminal == null)
        {
            log.Warning("Place an AE terminal in the main inventory first.");
            Diagnostics.Show("DeepSpaceAE - Terminal not found", new[] {
                "Place an AE terminal directly in MAIN, outside bags and boxes.", "Use F9 > Special > AE: Diagnose to inspect parts." });
            return;
        }
        session.Open(terminal);
    }
}
