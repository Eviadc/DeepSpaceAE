using System;
using System.Linq;
using DeepSpaceAE.Core.Network;
using Xunit;
namespace DeepSpaceAE.Tests;
public sealed class PowerSelectionTests
{
    [Fact]
    public void OneSufficientBatteryLeavesAllOtherBatteriesUnselected()
    {
        var plan=PowerSelection.Select(25,new[]{new PowerCandidate(1,10),new PowerCandidate(2,40),new PowerCandidate(3,8)})!;
        var source=Assert.Single(plan); Assert.Equal(2,source.Id); Assert.Equal(25,source.Amount);
    }
    [Fact]
    public void SmallBatteriesCombineAndOnlyTheLastRequiredShareIsCharged()
    {
        var plan=PowerSelection.Select(9,new[]{new PowerCandidate(1,6),new PowerCandidate(2,4),new PowerCandidate(3,1)})!;
        Assert.Equal(2,plan.Count); Assert.Equal(6,plan[0].Amount); Assert.Equal(3,plan[1].Amount);
        Assert.Null(PowerSelection.Select(12,new[]{new PowerCandidate(1,6),new PowerCandidate(2,4),new PowerCandidate(3,1)}));
    }
    [Fact]
    public void ExistingReservationsWinTiesButNeverForceMoreBatteriesThanNecessary()
    {
        Assert.Equal(1,Assert.Single(PowerSelection.Select(20,new[]{new PowerCandidate(1,25,true),new PowerCandidate(2,30)})!).Id);
        var plan=PowerSelection.Select(40,new[]{new PowerCandidate(1,25,true),new PowerCandidate(2,20,true),new PowerCandidate(3,50)})!;
        Assert.Equal(3,Assert.Single(plan).Id);
        Assert.Equal(2,Assert.Single(PowerSelection.Select(20,new[]{new PowerCandidate(1,5,true),new PowerCandidate(2,30)})!).Id);
    }
    [Fact]
    public void SelectionAgreesWithExhaustiveMinimumCountAndMaximumRetentionOracle()
    {
        var random=new Random(9307);
        for(int trial=0;trial<160;trial++)
        {
            var sources=Enumerable.Range(1,random.Next(2,9)).Select(i=>new PowerCandidate(i,random.Next(0,51),random.Next(2)==0)).ToArray();
            int required=random.Next(1,181),minimum=int.MaxValue,retained=-1;
            for(int mask=1;mask<(1<<sources.Length);mask++)
            {
                var subset=sources.Where((_,i)=>(mask&(1<<i))!=0).ToArray();
                if(subset.Sum(s=>(long)s.Capacity)<required) continue;
                int keep=subset.Count(s=>s.Preferred);
                if(subset.Length<minimum) { minimum=subset.Length; retained=keep; }
                else if(subset.Length==minimum) retained=Math.Max(retained,keep);
            }
            var selected=PowerSelection.Select(required,sources);
            if(minimum==int.MaxValue) { Assert.Null(selected); continue; }
            Assert.NotNull(selected); Assert.Equal(minimum,selected.Count);
            Assert.Equal(retained,selected.Count(s=>sources.Single(c=>c.Id==s.Id).Preferred));
            Assert.Equal(required,selected.Sum(s=>s.Amount));
            Assert.All(selected,s=>Assert.InRange(s.Amount,1,sources.Single(c=>c.Id==s.Id).Capacity));
        }
    }
    [Fact]
    public void InvalidIdentitiesAreRejectedAndLargeCapacitiesDoNotOverflow()
    {
        Assert.Throws<ArgumentException>(()=>PowerSelection.Select(1,new[]{new PowerCandidate(1,2),new PowerCandidate(1,3)}));
        Assert.Throws<ArgumentException>(()=>PowerSelection.Select(1,new[]{new PowerCandidate(0,2)}));
        Assert.Throws<ArgumentException>(()=>PowerSelection.Select(1,new[]{new PowerCandidate(1,-1)}));
        Assert.Equal(int.MaxValue,Assert.Single(PowerSelection.Select(int.MaxValue,new[]{new PowerCandidate(1,int.MaxValue),new PowerCandidate(2,int.MaxValue)})!).Amount);
    }
    [Fact]
    public void LargePoolCanReleaseEveryPreferredSmallBatteryForTheMinimumCombination()
    {
        const int count=2048;
        var sources=Enumerable.Range(1,count).Select(i=>new PowerCandidate(i,1,true))
            .Concat(Enumerable.Range(count+1,count).Select(i=>new PowerCandidate(i,3)));
        var selected=PowerSelection.Select(3*count,sources)!;
        Assert.Equal(count,selected.Count); Assert.All(selected,s=>Assert.True(s.Id>count));
        Assert.Equal(3*count,selected.Sum(s=>s.Amount));
    }
    [Fact]
    public void ReceiverIsAZeroCostChannelLeafAndDoesNotJoinTwoNetworks()
    {
        var nodes=new[]{new NetworkNode("core",NetworkPart.Core),new NetworkNode("wire",NetworkPart.Pipeline),
            new NetworkNode("receiver",NetworkPart.EnergyReceiver),new NetworkNode("other",NetworkPart.Pipeline)};
        var topology=PhysicalNetworkProjection.Build(nodes,new[]{new NetworkLink("core","wire"),new NetworkLink("wire","receiver")},new ChannelSettings());
        var evaluation=new NetworkEvaluation(topology);
        Assert.Equal(1,evaluation.ChannelsUsed["core"]); Assert.Equal(2,evaluation.NightCosts["core"]);
        var ambiguous=PhysicalNetworkProjection.Build(nodes,topology.Links.Append(new NetworkLink("receiver","other")),new ChannelSettings());
        Assert.NotEqual(DeviceStatus.Online,new NetworkEvaluation(ambiguous).Devices["receiver"].Status);
    }
}
