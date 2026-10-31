using System;
using System.Linq;
using System.Collections.Generic;
using DeepSpaceAE.Core.Network;
using Xunit;
namespace DeepSpaceAE.Tests;

public sealed class StorageFeatureTests
{
    private static StoragePolicy P(string[] keys, bool tags = false, bool exclude = false, StorageAccess access = StorageAccess.ReadWrite,
        int cards = 0, bool extraction = true, int priority = 0) => new(priority, access, tags, exclude, extraction, cards, keys);
    [Fact]
    public void RoutingTriesHighestPriorityEligibleBusWithDeterministicTies()
    {
        var policies = new Dictionary<string,StoragePolicy> {
            ["c"] = P(new[]{"battery"},priority:100), ["b"] = P(Array.Empty<string>(),priority:20),
            ["a"] = P(Array.Empty<string>(),priority:20), ["read"] = P(Array.Empty<string>(),access:StorageAccess.ExtractOnly,priority:999) };
        Assert.Equal(new[]{"c","a","b"},StorageRouting.InsertionOrder(policies,"battery",Array.Empty<string>()));
        Assert.Equal(new[]{"a","b"},StorageRouting.InsertionOrder(policies,"ore",Array.Empty<string>()));
    }
    [Fact]
    public void CardsExpandGhostSlotsAndInactiveFiltersRemainInactive()
    {
        var filters = Enumerable.Range(0,63).Select(i => "type"+i).ToArray();
        Assert.False(P(filters).Matches("type18", Array.Empty<string>()));
        Assert.True(P(filters, cards:1).Matches("type18", Array.Empty<string>()));
        Assert.Equal(63, P(filters, cards:5).Slots);
        Assert.Equal(54, P(filters, tags:true, cards:4).Slots);
        Assert.Throws<ArgumentOutOfRangeException>(()=>P(filters,tags:true,cards:5));
    }
    [Fact]
    public void ItemAndTagFiltersRespectAccessAndExclusion()
    {
        var tags = new[]{"BATTERY","TOOL"};
        Assert.True(P(new[]{"battery"}).CanInsert("battery",tags));
        Assert.False(P(new[]{"BATTERY"}).CanInsert("battery",tags));
        Assert.True(P(new[]{"BATTERY"},tags:true).CanInsert("charged-cell",tags));
        Assert.False(P(new[]{"BATTERY"},tags:true,exclude:true).CanInsert("charged-cell",tags));
        Assert.False(P(Array.Empty<string>(),access:StorageAccess.ExtractOnly).CanInsert("x",tags));
        Assert.False(P(Array.Empty<string>(),access:StorageAccess.InsertOnly).CanExtract("x",tags));
        Assert.True(P(new[]{"different"},extraction:false).CanExtract("battery",tags));
        Assert.True(P(Array.Empty<string>(),exclude:true).Matches("x",tags));
    }
    [Fact]
    public void CompiledContainerUnionAgreesWithEveryPolicyCombination()
    {
        var random = new Random(1835);
        for(int trial=0;trial<400;trial++)
        {
            var policies = Enumerable.Range(0,random.Next(9)).Select(_=>P(
                Enumerable.Range(0,random.Next(5)).Select(_=>"k"+random.Next(8)).ToArray(),
                tags:random.Next(2)==0, exclude:random.Next(2)==0, access:(StorageAccess)random.Next(3),extraction:random.Next(3)!=0)).ToArray();
            var compiled = new ExtractionFilter(policies);
            for(int i=0;i<20;i++)
            {
                string id="k"+random.Next(10); var tags=Enumerable.Range(0,random.Next(6)).Select(_=>"k"+random.Next(10)).ToArray();
                Assert.Equal(policies.Any(p=>p.CanExtract(id,tags)),compiled.Allows(id,tags));
            }
        }
    }
    [Fact]
    public void ExclusionUnionSpansMachineWordBoundaries()
    {
        var policies = Enumerable.Range(0,129).Select(i=>P(new[]{"shared", "unique"+i},exclude:true)).ToArray();
        var filter = new ExtractionFilter(policies);
        Assert.False(filter.Allows("shared",Array.Empty<string>()));
        Assert.True(filter.Allows("unique64",Array.Empty<string>()));
        Assert.True(filter.Allows("other",Array.Empty<string>()));
    }
    [Fact]
    public void SortedSearchCachesPagesWithoutMergingStatefulInstances()
    {
        var a = new CatalogRecord(Guid.NewGuid(),"battery","Cell",Array.Empty<string>(),2);
        var b = new CatalogRecord(Guid.NewGuid(),"battery","Cell",Array.Empty<string>(),9);
        var search = new CatalogSearch(new[]{a,b});
        var result = search.Find("cell",sort:CatalogSort.Quantity);
        Assert.Same(result,search.Find("cell",sort:CatalogSort.Quantity));
        Assert.Equal(2,result.Count); Assert.Equal(b.Instance,result[0].Instance);
    }
    [Fact]
    public void SearchPreservesInstancesAndCombinesWordsAndTags()
    {
        var a=new CatalogRecord(Guid.NewGuid(),"battery","旧电池",new[]{"POWER","METAL"});
        var b=new CatalogRecord(Guid.NewGuid(),"battery","满电电池",new[]{"POWER"});
        var search = new CatalogSearch(new[]{a,b,new CatalogRecord(Guid.NewGuid(),"ore","矿石",new[]{"METAL"})});
        Assert.Equal(2,search.Find("BATTERY #pow").Count);
        Assert.Equal(a.Instance,Assert.Single(search.Find("电池","METAL")).Instance);
        Assert.Equal(b.Instance,Assert.Single(search.Find("满电 #power")).Instance);
        Assert.Empty(search.Find("battery", "missing"));
    }
    [Fact]
    public void ReservationRequiresWholeNightAndKeepsPeakCost()
    {
        var lease=new NightReservation();
        Assert.False(lease.Reserve(3,80,42,n=>n<=79)); Assert.True(lease.CanRemove(3));
        Assert.True(lease.Reserve(3,80,42,n=>n<=100)); Assert.False(lease.CanRemove(3));
        Assert.True(lease.Reserve(3,90,42,n=>n<=100));
        Assert.True(lease.Reserve(3,10,42,n=>n<=100)); Assert.Equal(90,lease.Required);
        Assert.False(lease.Reserve(3,110,42,n=>n<=100)); Assert.Equal(110,lease.Required);
        Assert.Equal(NightPhase.Shortfall,lease.Phase); Assert.True(lease.CanRemove(3));
        Assert.False(lease.Reserve(3,10,42,_=>true)); // same battery cannot clear today's shortage
        Assert.False(lease.Reserve(3,10,43,n=>n<=100));
        Assert.True(lease.Reserve(3,10,43,n=>n<=120)); Assert.Equal(110,lease.Required);
    }
    [Fact]
    public void UnpaidShortfallResetsNextDayWithoutCarryingPeakOrDebit()
    {
        var lease=new NightReservation(); Assert.True(lease.Reserve(3,80,42,_=>true));
        Assert.False(lease.Reserve(3,110,42,n=>n<=100));
        Assert.False(lease.BeginDebit(3,42));
        var restored=new NightReservation(lease.Day,lease.Required,lease.Battery,lease.Phase);
        Assert.True(restored.Reserve(4,10,42,n=>n<=100)); Assert.Equal(10,restored.Required);
        Assert.True(restored.BeginDebit(4,42)); restored.Complete(true);
    }
    [Fact]
    public void PeakWithoutBatterySurvivesReplacementTrialsButResetsTomorrow()
    {
        var lease=new NightReservation(); lease.ObservePeak(3,90);
        Assert.False(lease.Reserve(3,20,42,n=>n<=80)); Assert.Equal(90,lease.Required);
        Assert.True(lease.Reserve(3,20,43,n=>n<=100)); Assert.Equal(90,lease.Required);
        Assert.False(lease.Reserve(3,120,43,n=>n<=100));
        Assert.True(lease.ObservePeak(4,5)); Assert.Equal(5,lease.Required); Assert.Equal(NightPhase.Empty,lease.Phase);
    }
    [Fact]
    public void DebitIsOnceAndBatteryUnlocksOnlyNextDay()
    {
        var lease=new NightReservation(); Assert.True(lease.Reserve(3,25,42,_=>true));
        Assert.False(lease.BeginDebit(3,43)); Assert.True(lease.BeginDebit(3,42)); Assert.False(lease.BeginDebit(3,42));
        var restored = new NightReservation(lease.Day,lease.Required,lease.Battery,lease.Phase);
        Assert.False(restored.Reserve(3,25,42,_=>true)); Assert.False(restored.CanRemove(4));
        lease.Complete(true); Assert.False(lease.BeginDebit(3,42)); Assert.False(lease.CanRemove(3)); Assert.True(lease.CanRemove(4));
        Assert.False(lease.Reserve(3,25,42,_=>true)); Assert.True(lease.Reserve(4,25,43,_=>true));
    }
    [Fact]
    public void LateSettlementUsesActualBillingDayAndCannotChargeTwice()
    {
        var lease = new NightReservation(); lease.Reserve(3,25,42,_=>true);
        Assert.True(lease.BeginDebit(5,42)); lease.Complete(true);
        Assert.Equal(5,lease.Day); Assert.False(lease.CanRemove(5)); Assert.True(lease.CanRemove(6));
        Assert.False(lease.Reserve(5,25,42,_=>true)); Assert.False(lease.BeginDebit(5,42));
    }
    [Fact]
    public void DelayedGridReceiptSettlesOnPaymentDayAndResetsOnlyFollowingDay()
    {
        var lease=new NightReservation(); lease.Reserve(3,25,42,_=>true); lease.BeginDebit(3,42);
        lease.Complete(true,5);
        Assert.Equal(5,lease.Day); Assert.False(lease.CanRemove(5));
        Assert.False(lease.Reserve(5,2,42,_=>true));
        Assert.True(lease.Reserve(6,2,42,_=>true)); Assert.Equal(2,lease.Required);
    }
    [Fact]
    public void UnknownDebitCannotReplayOrUnlockEvenOnFollowingDay()
    {
        var lease=new NightReservation();lease.Reserve(3,25,42,_=>true);lease.BeginDebit(3,42);lease.Complete(false);
        Assert.Equal(NightPhase.Fault,lease.Phase); Assert.False(lease.CanRemove(50)); Assert.False(lease.BeginDebit(50,42));
        Assert.False(lease.Reserve(50,1,42,_=>true));
    }
    [Fact]
    public void SharedComponentCostIncludesAllPartsWithoutMaterializingRoutes()
    {
        var topology=new NetworkTopology(new[]{new NetworkNode("c",NetworkPart.Core),new NetworkNode("p",NetworkPart.Pipeline),
            new NetworkNode("t",NetworkPart.Terminal),new NetworkNode("b",NetworkPart.ItemStorageBus,"box")},
            new[]{new NetworkLink("c","p"),new NetworkLink("p","t"),new NetworkLink("p","b")},new ChannelSettings());
        var e=new NetworkEvaluation(topology); Assert.Equal(4,e.NightCosts["c"]); Assert.Equal("c",e.ComponentCore["t"]);
    }
}
