using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DeepSpaceAE.Core.Network;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

/// <summary>Coalesced native events, no per-frame inventory poll or runtime texture allocation.</summary>
internal sealed class CableVisuals
{
    private GameGridInventory? root;
    private bool queued;
    private int generation;
    private sealed class BatteryState { internal int Energy, Output, Grid; }
    private readonly ConditionalWeakTable<GameItem, BatteryState> batteryStates = new();
    private readonly ModLog log;
    private readonly GameSession session;
    private Dictionary<string, Sprite>? sprites;
    private readonly HashSet<GameItem> animatedCores = new();
    private static readonly List<string> coreFrames = Enumerable.Range(0, 12).Select(i => "core-" + i).ToList();
    private readonly Dictionary<GameItem, (int, int, int, int, int, bool, bool)> poses = new();
    internal CableVisuals(ModLog log, GameSession session) { this.log = log; this.session = session; }
    internal void AssetsLoaded(LoadedMod mod) { if (mod.Manifest.ID == ModEntry.ModId) { sprites = mod.Sprites; Invalidate(); } }
    internal void Attach()
    {
        var current = GameSession.MainInventory(); if (ReferenceEquals(current, root)) return;
        Detach(); root = current; if (root == null) return;
        sprites ??= ModLoader.Mods.FirstOrDefault(m => m.Manifest.ID == ModEntry.ModId)?.Sprites;
        root.onSlotAddItemEndFunc += Changed; root.onSlotRemoveItemEndFunc += Changed;
        Invalidate();
    }
    internal void Detach()
    {
        foreach (var core in animatedCores.ToArray()) StopAnimation(core);
        if (root != null) { root.onSlotAddItemEndFunc -= Changed; root.onSlotRemoveItemEndFunc -= Changed; }
        root = null; generation++; queued = false; poses.Clear();
    }
    private static string Base(GameItem part) => ModEntry.Parts.First(p => p.Id == part.identifier).Sprite;
    internal void Inactive(GameItem part)
    { if (GameSession.IsPart(part)) { StopAnimation(part); Apply(part, Base(part) + "-off"); } }
    private void StopAnimation(GameItem core)
    {
        if (!animatedCores.Remove(core)) return;
        if (core is GameItemElement element && !element.IsDestroyed()) SpriteAnimationHelper.RemoveSpriteAnimation(core);
    }
    private void Animate(GameItem core, bool active)
    {
        if (!active) { StopAnimation(core); return; }
        if (sprites == null || coreFrames.Any(frame => !sprites.ContainsKey(frame))) return;
        if (animatedCores.Add(core))
            // AE2 controller_lights metadata: 25 Minecraft ticks at 50ms per tick.
            // Native animation only changes sprites; no textures or network scans per frame.
            SpriteAnimationHelper.InitSpriteAnimation(core, coreFrames, 500);
    }
    private void Apply(GameItem part, string path)
    {
        if (sprites == null || !sprites.TryGetValue(path, out var sprite)) return;
        if (part is GameItemElement element)
        {
            if (part.spritePath != path) { element.ApplyAnimationFrame(sprite); part.spritePath = path; }
            if (part.identifier == "deepspaceae.pipeline") element.HandleInnateModSprite();
        }
    }
    internal static List<ItemSpriteModifier> BusMarkers(GameItem cable)
    {
        var result = new List<ItemSpriteModifier>();
        string suffix = cable.spritePath.EndsWith("-off", StringComparison.Ordinal) ? "-off" : "";
        foreach (var bus in PartStorage.CableBuses(cable))
        {
            int face = PartStorage.Int(bus, "cable_face", -1);
            var sprite = face >= 0 && face < 4 ? NativeWidgets.Sprite(IoBus.FacePrefix(bus) + face + suffix) : null;
            if (sprite != null) result.Add(new ItemSpriteModifier(sprite, Vector2.zero));
        }
        return result;
    }
    private void Changed(GameItem item, GameInventory inventory, SlotMarker slot)
    {
        session.AutomationStructureChanged();
        if (GameSession.IsPart(item) && !ReferenceEquals(item.parentInventory, root)) Inactive(item);
        poses.Remove(item); Invalidate(); session.NativeStorageChanged();
    }
    private static (int, int, int, int, int, bool, bool) Pose(GameItem item)
    { var s = item.modifiedShape; return (s.minX, s.minY, s.width, s.height, s.orientation, s.flipped, GeneralHelper.IsItemEffectivelyOwned(item)); }
    internal void ShapeChanged(GameItem item)
    {
        session.ItemChanged(item);
        if (item.parentInventory is DrawerInventory drawer) drawer.RefreshItemDisplay(item);
        if (GameSession.IsPart(item) && !ReferenceEquals(item.parentInventory, root)) Inactive(item);
        if (root == null) return;
        if (ReferenceEquals(item.parentInventory, root))
        {
            if (!poses.TryGetValue(item, out var old) || old != Pose(item)) { session.AutomationStructureChanged(); Invalidate(); session.StructureChanged(item); session.NativeStorageChanged(); }
        }
        else if(item.parentInventory is DiskAreaInventory area)
        {
            var drive=DiskStorage.HostDrive(area.Disk); if(drive!=null) session.NativeStorageChanged(drive);
        }
        else if (item.parentInventory?.GetParentItem() is GameItem owner && (owner.identifier == "deepspaceae.core" || EnergyReceiver.IsReceiver(owner)))
        {
            int energy = PowerHelper.GetAvailableEnergyFromItem(item, false);
            int output = HypertechBatteryHelper.IsHypertechBattery(item) ? HypertechBatteryHelper.GetOutput(item) : 0;
            int grid = PartStorage.IsGrid(item) ? (CurrentConverterHelper.IsOn(item) ? 1 : 0) + (CurrentConverterHelper.IsBlackedOut() ? 2 : 0) : -1;
            var previous = batteryStates.GetValue(item, _ => new BatteryState { Energy = -1, Output = -1 });
            if (previous.Energy != energy || previous.Output != output || previous.Grid != grid)
            { previous.Energy = energy; previous.Output = output; previous.Grid = grid; Invalidate(); session.StructureChanged(owner); session.NativeStorageChanged(owner); }
        }
    }
    internal void Invalidate()
    {
        if (root == null || queued) return;
        queued = true; int expected = generation;
        LetterboxManager.RunNextUpdate(() => {
            if (generation != expected || root == null) return; queued = false;
            try
            {
                var board = session.ReadBoard(); var layout = board.Layout; poses.Clear();
                foreach (var item in layout.Items) poses.Add(item, Pose(item));
                foreach (var item in layout.Items.Where(GameSession.IsPart))
                {
                    string id = session.Id(item);
                    bool active = board.Evaluation.ComponentCore.TryGetValue(id, out var core) && board.Powered.Contains(core);
                    if (active && board.Evaluation.Devices.TryGetValue(id, out var device)) active = device.Status == DeviceStatus.Online;
                    if (active && item.identifier == "deepspaceae.item_storage_bus")
                    {
                        var owner = layout.FrontOwner(item);
                        active = owner != null && layout.Owned.Contains(owner) && !GameSession.IsPart(owner) && (LiquidStorage.IsContainer(owner) || GameSession.Contents(owner).Count > 0);
                    }
                    if(active && IoBus.IsIo(item))
                    {
                        var machine=layout.FrontOwner(item);
                        active=machine!=null && layout.Owned.Contains(machine) && IoBus.MachineSlots(machine,item.identifier==IoBus.Import).Count>0;
                    }
                    string path = item.identifier == "deepspaceae.pipeline" ? "pipeline-" + layout.CableMask(item) : Base(item);
                    Apply(item, path + (active ? "" : "-off"));
                    if (item.identifier == "deepspaceae.core") Animate(item, active);
                }
            }
            catch (Exception e)
            {
                foreach (var item in root.childItems.Where(GameSession.IsPart)) Inactive(item);
                log.Error("AE appearance update failed: " + Diagnostics.ConsoleText(e.ToString()));
            }
        });
    }
}
