using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;

namespace DeepSpaceAE.GameAdapter;

/// <summary>One fresh native observation. Never infer cells from screen/pixel positions.</summary>
internal sealed class InventoryLayout
{
    internal readonly List<GameItem> Items;
    internal readonly List<GameItem> Parts = new();
    internal readonly HashSet<GameItem> Owned = new();
    private readonly HashSet<GameItem> partSet = new();
    private readonly OccupiedGrid<GameItem> grid = new();
    private readonly Dictionary<GameItem, List<GridCell>> footprints = new();

    internal InventoryLayout(GameGridInventory root, bool partsOnly = false)
    {
        Items = root.childItems; // The native getter allocates; read it once.
        foreach (var item in Items)
        {
            if (!ReferenceEquals(item.parentInventory, root)) throw new InvalidOperationException("Inconsistent MAIN inventory observation.");
            var part = GameSession.IsPart(item);
            if (partsOnly && !part) continue;
            if (GeneralHelper.IsItemEffectivelyOwned(item)) Owned.Add(item);
            if (part && Owned.Contains(item)) { Parts.Add(item); partSet.Add(item); }
            var cells = new List<GridCell>();
            var shape = item.modifiedShape;
            for (int y = 0; y < shape.height; y++)
                for (int x = 0; x < shape.width; x++)
                    if (shape.GetLocal(x, y) == 1)
                    {
                        var (gx, gy) = shape.ForwardTransform(x, y);
                        var cell = new GridCell(gx, gy);
                        grid.Add(cell, item);
                        cells.Add(cell);
                    }
            footprints.Add(item, cells);
        }
    }

    // Transform an authored local port with the game's actual rotation and flip mapping.
    internal static GridCell Step(GameItem item, GridSide localSide)
    {
        var offset = GridSides.Step(localSide);
        var shape = item.modifiedShape;
        var (x, y) = shape.ForwardTransform(0, 0);
        var (nx, ny) = shape.ForwardTransform(offset.X, offset.Y);
        return new GridCell(nx - x, ny - y);
    }

    internal GameItem? FrontOwner(GameItem bus)
    {
        if (!footprints.TryGetValue(bus, out var cells) || cells.Count != 1)
            throw new InvalidOperationException("Storage bus must occupy exactly one cell.");
        return grid.At(cells[0] + Step(bus, GridSide.East));
    }

    private static bool Allows(GameItem item, GridCell toward)
        => item.identifier != "deepspaceae.item_storage_bus" || GridSides.IsBackFace(Step(item, GridSide.East), toward);

    internal HashSet<GameItem> Neighbors(GameItem part)
    {
        var result = new HashSet<GameItem>();
        foreach (var cell in footprints[part])
            for (int side = 0; side < 4; side++)
            {
                var step = GridSides.Step((GridSide)side);
                if (!Allows(part, step)) continue;
                var other = grid.At(cell + step);
                if (other != null && !ReferenceEquals(other, part) && partSet.Contains(other)
                    && Allows(other, new GridCell(-step.X, -step.Y))) result.Add(other);
            }
        return result;
    }

    internal IEnumerable<NetworkLink> Edges(Func<GameItem, string> id)
    {
        var ids = Parts.ToDictionary(item => item, id);
        foreach (var part in Parts)
            foreach (var other in Neighbors(part))
                if (StringComparer.Ordinal.Compare(ids[part], ids[other]) < 0)
                    yield return new NetworkLink(ids[part], ids[other]);
    }

    internal int CableMask(GameItem cable)
    {
        var cells = footprints[cable];
        if (cells.Count != 1) throw new InvalidOperationException("Cable must occupy exactly one cell.");
        return grid.ConnectionMask(cells[0], side => Step(cable, side),
            (other, toward) => partSet.Contains(other) && Allows(other, toward));
    }
}
