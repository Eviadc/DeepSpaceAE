using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DeepSpaceAE.GameAdapter;

/// <summary>Batch native inventory events once next update; never poll every frame.</summary>
internal sealed class CableVisuals
{
    private GameGridInventory? root;
    private bool queued;
    private int generation;
    private readonly ModLog log;
    private Dictionary<string, Sprite>? sprites;
    private readonly Dictionary<GameItem, (int X, int Y, int W, int H, int R, bool Flip)> poses = new();
    internal CableVisuals(ModLog log) => this.log = log;

    internal void AssetsLoaded(LoadedMod mod)
    {
        if (mod.Manifest.ID != ModEntry.ModId) return;
        sprites = mod.Sprites;
        QueueRefresh();
    }

    internal void Attach()
    {
        var current = GameSession.MainInventory();
        if (ReferenceEquals(root, current)) return;
        Detach();
        root = current;
        if (root == null) return;
        sprites ??= ModLoader.Mods.FirstOrDefault(mod => mod.Manifest.ID == ModEntry.ModId)?.Sprites;
        root.onSlotAddItemEndFunc += Changed;
        root.onSlotRemoveItemEndFunc += Changed;
        QueueRefresh();
    }

    internal void Detach()
    {
        if (root != null)
        {
            root.onSlotAddItemEndFunc -= Changed;
            root.onSlotRemoveItemEndFunc -= Changed;
        }
        root = null;
        generation++;
        queued = false;
        poses.Clear();
    }

    private void Changed(GameItem item, GameInventory inventory, SlotMarker slot)
    {
        if (GameSession.IsPart(item)) QueueRefresh();
    }

    private static (int, int, int, int, int, bool) Pose(GameItem item)
    {
        var shape = item.modifiedShape;
        return (shape.minX, shape.minY, shape.width, shape.height, shape.orientation, shape.flipped);
    }

    internal void ShapeChanged(GameItem item)
    {
        if (root == null || !GameSession.IsPart(item) || !ReferenceEquals(item.parentInventory, root)) return;
        if (!poses.TryGetValue(item, out var previous) || previous != Pose(item)) QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (root == null || queued) return;
        queued = true;
        int expectedGeneration = generation;
        LetterboxManager.RunNextUpdate(() => {
            if (generation != expectedGeneration || root == null) return;
            queued = false;
            try
            {
                var layout = new InventoryLayout(root, partsOnly: true);
                poses.Clear();
                foreach (var part in layout.Parts)
                {
                    poses.Add(part, Pose(part));
                    if (part.identifier != "deepspaceae.pipeline") continue;
                    var sprite = "pipeline-" + layout.CableMask(part);
                    // Cached sprite-only update: no mod-list search, shape reconstruction,
                    // recursive validation or texture allocation at each changed cable.
                    if (part.spritePath != sprite)
                    {
                        if (sprites == null || !sprites.TryGetValue(sprite, out var image))
                            throw new InvalidOperationException("Missing connected cable sprite: " + sprite);
                        if (part is not GameItemElement element) throw new InvalidOperationException("AE cable is not a native item element.");
                        element.ApplyAnimationFrame(image);
                        part.spritePath = sprite;
                    }
                }
            }
            catch (Exception error) { log.Error("Cable texture refresh failed: " + Diagnostics.ConsoleText(error.ToString())); }
        });
    }
}
