using System;
using System.Linq;
using DeepSpaceAE.Core.Network;
using Xunit;
namespace DeepSpaceAE.Tests;
public sealed class VirtualInventoryTests
{
    private static DisplayFootprint Box(int width, int height) => new(Guid.NewGuid(),
        Enumerable.Range(0,height).SelectMany(y => Enumerable.Range(0,width).Select(x => new GridCell(x,y))));
    [Fact]
    public void ContinuousViewportIncludesIntersectingItemsWithoutPages()
    {
        var items = Enumerable.Range(0,1000).Select(_ => Box(3,2)).ToArray();
        var layout = new VirtualInventoryLayout(items,6,4,continuous:true);
        Assert.Equal(1,layout.PageCount); Assert.Equal(1000,layout.Height);
        Assert.Equal(1000,layout.Page(0).Count);
        var visible = layout.Visible(3,4);
        Assert.Equal(6,visible.Count); // three shelves intersect [3,7)
        Assert.All(visible,p => Assert.True(p.Y < 7 && p.Y + p.Footprint.Height > 3));
        Assert.Empty(layout.Visible(1000,4));
        var first = items[0].Instance;
        Assert.False(layout.Reposition(first,0,0,998)); // occupied last shelf
        Assert.Equal(first,layout.At(0,0,0));
    }
    [Fact]
    public void ContinuousLayoutKeepsLargeItemsVisibleAcrossViewportBoundaries()
    {
        var huge = Box(25,30);
        var layout = new VirtualInventoryLayout(new[]{huge},18,12,continuous:true);
        Assert.Equal(25,layout.Width); Assert.Equal(30,layout.Height);
        Assert.Equal(huge.Instance,Assert.Single(layout.Visible(15,12)).Footprint.Instance);
    }
    [Fact]
    public void ItemsKeepTheirActualOccupiedCellsAndIdentity()
    {
        var a = Box(3,2); var b = new DisplayFootprint(Guid.NewGuid(),new[]{new GridCell(0,0),new GridCell(0,1),new GridCell(1,1)});
        var layout = new VirtualInventoryLayout(new[]{a,b},6,3);
        Assert.Equal(1,layout.PageCount);
        Assert.Equal(a.Instance,layout.At(0,2,1)); Assert.Equal(b.Instance,layout.At(0,3,0));
        Assert.Null(layout.At(0,4,0)); Assert.Equal(b.Instance,layout.At(0,4,1));
        Assert.Equal(6,layout.Page(0)[0].Footprint.Cells.Count);
    }
    [Fact]
    public void ShelfPagingHasNoOverlapAndNeverDropsLargeItems()
    {
        var items = Enumerable.Range(0,1000).Select(_=>Box(3,2)).ToArray();
        var layout = new VirtualInventoryLayout(items,6,4);
        Assert.Equal(250,layout.PageCount);
        Assert.Equal(1000,Enumerable.Range(0,layout.PageCount).Sum(p=>layout.Page(p).Count));
        foreach (int page in Enumerable.Range(0,layout.PageCount))
        {
            var cells=layout.Page(page).SelectMany(p=>p.Footprint.Cells.Select(c=>(p.X+c.X,p.Y+c.Y))).ToArray();
            Assert.Equal(cells.Length,cells.Distinct().Count());
        }
        var huge = new VirtualInventoryLayout(new[]{Box(25,18)},18,12);
        Assert.Equal(25,huge.Width); Assert.Equal(18,huge.Height); Assert.Single(huge.Page(0));
    }
    [Fact]
    public void RearrangementOnlyChangesDisplayCoordinates()
    {
        var a=Box(2,2); var b=Box(2,2); var layout=new VirtualInventoryLayout(new[]{a,b},6,4);
        Assert.False(layout.Reposition(a.Instance,0,2,0));
        Assert.True(layout.Reposition(a.Instance,0,4,2));
        Assert.Null(layout.At(0,0,0)); Assert.Equal(a.Instance,layout.At(0,5,3));
        Assert.Equal(b.Instance,layout.At(0,2,0)); Assert.Equal(4,a.Cells.Count);
        Assert.False(layout.Reposition(a.Instance,0,5,3));
    }
    [Fact]
    public void LocalizedTagSearchKeepsRawPolicyKeysSeparate()
    {
        var item = new CatalogRecord(Guid.NewGuid(), "ore", "矿石", new[] { "MATERIAL" }, tagNames: new[] { "材料" });
        var search = new CatalogSearch(new[] { item });
        Assert.Single(search.Find("#材料")); Assert.Single(search.Find("#material"));
        Assert.Equal("MATERIAL", Assert.Single(search.Tags));
    }
    [Fact]
    public void InvalidOrDuplicateDisplayIdentitiesFailClosed()
    {
        Assert.Throws<ArgumentException>(()=>new DisplayFootprint(Guid.NewGuid(),Array.Empty<GridCell>()));
        var a=Box(1,1); Assert.Throws<ArgumentException>(()=>new VirtualInventoryLayout(new[]{a,a}));
    }
    [Fact]
    public void WiresCostNothingAndActivePartsUseReducedDailyEnergy()
    {
        Assert.Equal(0,NetworkEvaluation.NightCost(NetworkPart.Pipeline));
        Assert.Equal(2,NetworkEvaluation.NightCost(NetworkPart.Core));
        Assert.Equal(1,NetworkEvaluation.NightCost(NetworkPart.ItemStorageBus));
        Assert.Equal(1,NetworkEvaluation.NightCost(NetworkPart.Terminal));
    }
    [Fact]
    public void BalanceMigrationCannotResetOrReplayAnAlreadyStartedDebit()
    {
        var lease=new NightReservation(); lease.Reserve(2,30,11,_=>true);
        Assert.True(lease.RepricePending(4)); Assert.Equal(2,lease.Day); Assert.Equal(11,lease.Battery); Assert.Equal(4,lease.Required);
        Assert.True(lease.BeginDebit(2,11)); Assert.False(lease.RepricePending(1)); lease.Complete(true);
        Assert.False(lease.RepricePending(1)); Assert.False(new NightReservation(2, 30, 11, NightPhase.Fault).RepricePending(4)); Assert.False(lease.BeginDebit(2,11)); Assert.False(lease.CanRemove(2)); Assert.True(lease.CanRemove(3));
    }
}
