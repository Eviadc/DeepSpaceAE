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
    private sealed class BatteryState { internal int Energy, Output; }
    private readonly ConditionalWeakTable<GameItem, BatteryState> batteryStates = new();
    private readonly ModLog log;
    private readonly GameSession session;
    private Dictionary<string, Sprite>? sprites;
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
        if (root != null) { root.onSlotAddItemEndFunc -= Changed; root.onSlotRemoveItemEndFunc -= Changed; }
        root = null; generation++; queued = false; poses.Clear();
    }
    private static string Base(GameItem part) => ModEntry.Parts.First(p => p.Id == part.identifier).Sprite;
    internal void Inactive(GameItem part) { if (GameSession.IsPart(part)) Apply(part, Base(part) + "-off"); }
    private void Apply(GameItem part, string path)
    {
        if (part.spritePath == path || sprites == null || !sprites.TryGetValue(path, out var sprite)) return;
        if (part is GameItemElement element) { element.ApplyAnimationFrame(sprite); part.spritePath = path; }
    }
    private void Changed(GameItem item, GameInventory inventory, SlotMarker slot)
    {
        if (GameSession.IsPart(item) && !ReferenceEquals(item.parentInventory, root)) Inactive(item);
        poses.Remove(item); Invalidate();
    }
    private static (int, int, int, int, int, bool, bool) Pose(GameItem item)
    { var s = item.modifiedShape; return (s.minX, s.minY, s.width, s.height, s.orientation, s.flipped, GeneralHelper.IsItemEffectivelyOwned(item)); }
    internal void ShapeChanged(GameItem item)
    {
        session.ItemChanged(item);
        if (GameSession.IsPart(item) && !ReferenceEquals(item.parentInventory, root)) Inactive(item);
        if (root == null) return;
        if (ReferenceEquals(item.parentInventory, root))
        {
            if (!poses.TryGetValue(item, out var old) || old != Pose(item)) { Invalidate(); session.StructureChanged(item); }
        }
        else if (item.parentInventory?.GetParentItem() is GameItem owner && owner.identifier == "deepspaceae.core")
        {
            int energy = PowerHelper.GetAvailableEnergyFromItem(item, false);
            int output = HypertechBatteryHelper.IsHypertechBattery(item) ? HypertechBatteryHelper.GetOutput(item) : 0;
            var previous = batteryStates.GetValue(item, _ => new BatteryState { Energy = -1, Output = -1 });
            if (previous.Energy != energy || previous.Output != output)
            { previous.Energy = energy; previous.Output = output; Invalidate(); session.StructureChanged(owner); }
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
                        active = owner != null && layout.Owned.Contains(owner) && !GameSession.IsPart(owner) && GameSession.Contents(owner).Count > 0;
                    }
                    string path = item.identifier == "deepspaceae.pipeline" ? "pipeline-" + layout.CableMask(item) : Base(item);
                    Apply(item, path + (active ? "" : "-off"));
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
