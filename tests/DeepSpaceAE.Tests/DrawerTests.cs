using System;
using DeepSpaceAE.Core.Network;
using Xunit;
namespace DeepSpaceAE.Tests;
public sealed class DrawerTests
{
    [Fact] public void CapacityIsSixteenUnitsTimesFourPerCard()
    {
        Assert.Equal(new[] { 16, 64, 256, 1024, 4096, 16384 }, new[] { DrawerCapacity.Limit(0), DrawerCapacity.Limit(1), DrawerCapacity.Limit(2), DrawerCapacity.Limit(3), DrawerCapacity.Limit(4), DrawerCapacity.Limit(5) });
        Assert.Throws<ArgumentOutOfRangeException>(() => DrawerCapacity.Limit(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DrawerCapacity.Limit(6));
    }
    [Fact] public void CapacityCountsUnitsAndRetainsOverfullStockWithoutFurtherInsertion()
    {
        Assert.True(DrawerCapacity.Fits(16, 12, 0, 4)); Assert.False(DrawerCapacity.Fits(16, 12, 0, 5));
        Assert.True(DrawerCapacity.Fits(16, 16, 4, 4)); Assert.False(DrawerCapacity.Fits(16, 64, 0, 1));
        Assert.False(DrawerCapacity.Fits(16, long.MaxValue - 1, 0, int.MaxValue));
        Assert.False(DrawerCapacity.Fits(16, 0, 1, 1));
    }
}
