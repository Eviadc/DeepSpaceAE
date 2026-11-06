using System;
using System.Collections.Generic;

namespace DeepSpaceAE.Core.Network;

public readonly struct GridCell : IEquatable<GridCell>
{
    public int X { get; }
    public int Y { get; }
    public GridCell(int x, int y) { X = x; Y = y; }
    public bool Equals(GridCell other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is GridCell other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public static GridCell operator +(GridCell a, GridCell b) => new GridCell(checked(a.X + b.X), checked(a.Y + b.Y));
}

public enum GridSide { North, East, South, West }

public static class GridSides
{
    public static bool IsBackFace(GridCell containerFace, GridCell toward)
        => toward.X == -containerFace.X && toward.Y == -containerFace.Y;
    public static GridCell Step(GridSide side) => side switch {
        GridSide.North => new GridCell(0, -1), GridSide.East => new GridCell(1, 0),
        GridSide.South => new GridCell(0, 1), GridSide.West => new GridCell(-1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(side)) };
}

/// <summary>Explicit occupied cells supplied by the adapter, not a simulated game inventory.</summary>
public sealed class OccupiedGrid<T> where T : class
{
    private readonly Dictionary<GridCell, T> cells = new Dictionary<GridCell, T>();
    public int Count => cells.Count;
    public void Add(GridCell cell, T owner)
    {
        if (owner is null) throw new ArgumentNullException(nameof(owner));
        if (cells.TryGetValue(cell, out var previous))
        {
            if (!ReferenceEquals(previous, owner)) throw new ArgumentException("Overlapping observed footprints.", nameof(cell));
            return;
        }
        cells.Add(cell, owner);
    }
    public T? At(GridCell cell) => cells.TryGetValue(cell, out var owner) ? owner : null;

    /// <summary>Exactly four cell probes; the adapter supplies its native local-to-grid transform.</summary>
    public int ConnectionMask(GridCell origin, Func<GridSide, GridCell> step, Func<T, GridCell, bool> accepts)
    {
        int mask = 0;
        for (int side = 0; side < 4; side++)
        {
            var offset = step((GridSide)side);
            var owner = At(origin + offset);
            if (owner != null && accepts(owner, new GridCell(-offset.X, -offset.Y))) mask |= 1 << side;
        }
        return mask;
    }
}
