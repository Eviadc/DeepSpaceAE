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
    internal readonly Dictionary<GameItem, GameItem> BusCables = new();
    private readonly HashSet<GameItem> partSet = new();
    private readonly Dictionary<GridCell,GameItem> grid = new();
    private readonly Dictionary<GameItem, List<GridCell>> footprints = new();

    internal InventoryLayout(GameGridInventory root, bool partsOnly = false)
    {
        Items = root.childItems.Distinct().ToList(); // Native inventory views may expose aliases of the same item.
        foreach (var item in Items)
        {
            var part = GameSession.IsPart(item) && !IoBus.IsBus(item);
            if (partsOnly && !part) continue;
            if (GeneralHelper.IsItemEffectivelyOwned(item)) Owned.Add(item);
            if (part && Owned.Contains(item)) { Parts.Add(item); partSet.Add(item); }
            var cells = new List<GridCell>();
            var shape = root.GetIncompleteItemInventorySlot(item).itemGridShape ?? item.modifiedShape;
            for (int y = 0; y < shape.height; y++)
                for (int x = 0; x < shape.width; x++)
                    if (shape.GetLocal(x, y) == 1)
                    {
                        var (gx, gy) = shape.ForwardTransform(x, y);
                        var cell = new GridCell(gx, gy);
                        grid.TryAdd(cell, item);
                        cells.Add(cell);
                    }
            footprints.Add(item, cells);
        }
        foreach (var cable in Parts.Where(i => i.identifier == "deepspaceae.pipeline").ToArray())
            foreach (var bus in PartStorage.CableBuses(cable))
            {
                int face = PartStorage.Int(bus, "cable_face", -1);
                if (face < 0 || face > 3 || !partSet.Add(bus)) continue;
                BusCables.Add(bus, cable); Parts.Add(bus); Owned.Add(bus);
                footprints.Add(bus, footprints[cable]);
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
        if (!BusCables.TryGetValue(bus, out var cable)) return null;
        var cells = footprints[cable];
        return cells.Count==0 ? null : grid.GetValueOrDefault(cells[0] + Step(cable, (GridSide)PartStorage.Int(bus, "cable_face")));
    }

    private static bool Allows(GameItem item, GridCell toward)
    {
        if (item.identifier != "deepspaceae.pipeline") return !IoBus.IsBus(item);
        foreach (var bus in PartStorage.CableBuses(item))
        {
            int face = PartStorage.Int(bus, "cable_face", -1);
            if (face >= 0 && face < 4 && Step(item, (GridSide)face).Equals(toward)) return false;
        }
        return true;
    }

    internal HashSet<GameItem> Neighbors(GameItem part)
    {
        var result = new HashSet<GameItem>();
        if (BusCables.TryGetValue(part, out var host)) { result.Add(host); return result; }
        if (part.identifier == "deepspaceae.pipeline")
            foreach (var bus in PartStorage.CableBuses(part)) if (BusCables.ContainsKey(bus)) result.Add(bus);
        foreach (var cell in footprints[part])
            for (int side = 0; side < 4; side++)
            {
                var step = GridSides.Step((GridSide)side);
                if (!Allows(part, step)) continue;
                var other = grid.GetValueOrDefault(cell + step);
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

    internal IEnumerable<(int X,int Y,int Side)> DevicePorts(GameItem part)
    {
        var shape=part.modifiedShape;
        for(int y=0;y<shape.height;y++) for(int x=0;x<shape.width;x++)
        {
            if(shape.GetLocal(x,y)!=1) continue;
            var (gx,gy)=shape.ForwardTransform(x,y);
            for(int side=0;side<4;side++)
            {
                var step=Step(part,(GridSide)side);
                var neighbor=grid.GetValueOrDefault(new GridCell(gx+step.X,gy+step.Y));
                if(neighbor?.identifier=="deepspaceae.pipeline" && partSet.Contains(neighbor) && Allows(neighbor,new GridCell(-step.X,-step.Y)))
                    yield return (x,y,side);
            }
        }
    }

    internal int CableMask(GameItem cable)
    {
        var cells = footprints[cable];
        if (cells.Count == 0) return 0;
        int mask = 0;
        for (int side = 0; side < 4; side++)
        {
            var offset = Step(cable, (GridSide)side); var other = grid.GetValueOrDefault(cells[0] + offset);
            if (other != null && partSet.Contains(other)
                && Allows(other, new GridCell(-offset.X, -offset.Y))) mask |= 1 << side;
        }
        int installed = 0;
        foreach (var bus in PartStorage.CableBuses(cable))
        { int face = PartStorage.Int(bus, "cable_face", -1); if (face >= 0 && face < 4) installed |= 1 << face; }
        return CablePorts.TransmissionMask(installed, mask);
    }
}
