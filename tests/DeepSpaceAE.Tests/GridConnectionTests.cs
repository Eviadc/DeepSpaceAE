using System;
using DeepSpaceAE.Core.Network;
using Xunit;

namespace DeepSpaceAE.Tests;

public sealed class GridConnectionTests
{
    [Fact]
    public void OccupiedCellsKeepExactOwnersAndRejectOverlap()
    {
        var grid = new OccupiedGrid<object>();
        var core = new object();
        var box = new object();
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++) grid.Add(new GridCell(x, y), core);
        grid.Add(new GridCell(2, 0), box);
        Assert.Equal(10, grid.Count);
        Assert.Same(core, grid.At(new GridCell(1, 1)));
        Assert.Same(box, grid.At(new GridCell(2, 0)));
        Assert.Null(grid.At(new GridCell(2, 1)));
        Assert.Throws<ArgumentException>(() => grid.Add(new GridCell(0, 0), box));
    }

    [Fact]
    public void AllSixteenConnectionMasksNeedOnlyFourTransformedProbes()
    {
        for (int wanted = 0; wanted < 16; wanted++)
        {
            var grid = new OccupiedGrid<object>();
            var origin = new GridCell(30, -12);
            for (int side = 0; side < 4; side++)
                if ((wanted & (1 << side)) != 0) grid.Add(origin + GridSides.Step((GridSide)side), new object());
            // An unrelated diagonal must never add a connection.
            grid.Add(origin + new GridCell(1, 1), new object());
            int probes = 0;
            int actual = grid.ConnectionMask(origin, side => { probes++; return GridSides.Step(side); }, (_, _) => true);
            Assert.Equal(wanted, actual);
            Assert.Equal(4, probes);
        }
    }

    [Fact]
    public void DirectionalBusExposesOnlyItsContainerFaceAndAcceptsNetworkOnlyAtBack()
    {
        var grid = new OccupiedGrid<object>();
        var origin = new GridCell(0, 0);
        var containers = new object[4];
        for (int side = 0; side < 4; side++)
        {
            containers[side] = new object();
            grid.Add(GridSides.Step((GridSide)side), containers[side]);
        }
        for (int facing = 0; facing < 4; facing++)
        {
            var front = GridSides.Step((GridSide)facing);
            Assert.Same(containers[facing], grid.At(origin + front));
            for (int contact = 0; contact < 4; contact++)
                Assert.Equal(contact == (facing + 2) % 4, GridSides.IsBackFace(front, GridSides.Step((GridSide)contact)));
        }
    }

    [Fact]
    public void ConnectionMaskUsesSuppliedNativeTransformForRotationAndFlip()
    {
        var grid = new OccupiedGrid<object>();
        grid.Add(new GridCell(1, 0), new object());
        // Supplied transforms are observations, not a reimplementation of a game shape.
        Assert.Equal(2, grid.ConnectionMask(new GridCell(0, 0), GridSides.Step, (_, _) => true));
        Assert.Equal(8, grid.ConnectionMask(new GridCell(0, 0), side => {
            var cell = GridSides.Step(side); return new GridCell(-cell.X, cell.Y);
        }, (_, _) => true));
        Assert.Equal(1, grid.ConnectionMask(new GridCell(0, 0), side => {
            var cell = GridSides.Step(side); return new GridCell(-cell.Y, cell.X);
        }, (_, _) => true));
    }
}
