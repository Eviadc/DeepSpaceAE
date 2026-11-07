using System.Collections.Generic;
using DeepSpaceAE.Core.Network;
using Xunit;

namespace DeepSpaceAE.Tests;

public sealed class StackActivityTests
{
    [Fact]
    public void IdenticalNetworkTotalsDoNotChangeOrderWhenContainersOrViewsChange()
    {
        var activity=new StackActivity();
        activity.Observe(new Dictionary<string,long>{{"ore",4},{"tool",2}},10);
        activity.Observe(new Dictionary<string,long>{{"ore",5},{"tool",2}},20);
        Assert.Equal(20,activity.Modified("ore"));
        Assert.False(activity.Observe(new Dictionary<string,long>{{"tool",2},{"ore",5}},30));
        Assert.Equal(20,activity.Modified("ore"));
        Assert.Equal(0,activity.Modified("tool"));
    }
    [Fact]
    public void SavedStackOrderSurvivesReloadAndNewWithdrawalsUpdateTheRemainingStack()
    {
        var activity=new StackActivity();
        activity.Observe(new Dictionary<string,long>{{"ore",4}},10);
        activity.Observe(new Dictionary<string,long>{{"ore",5}},20);
        var loaded=StackActivity.Decode(activity.Encode());
        Assert.False(loaded.Observe(new Dictionary<string,long>{{"ore",5}},100));
        Assert.Equal(20,loaded.Modified("ore"));
        loaded.Observe(new Dictionary<string,long>{{"ore",3}},110);
        Assert.Equal(110,loaded.Modified("ore"));
    }
    [Fact]
    public void RemovedStacksArePrunedAndAReintroducedStackGetsANewMonotonicTime()
    {
        var activity=new StackActivity();
        activity.Observe(new Dictionary<string,long>{{"ore",1}},100);
        activity.Observe(new Dictionary<string,long>(),200);
        Assert.Equal(0,activity.Modified("ore"));
        activity.Observe(new Dictionary<string,long>{{"ore",1}},50);
        Assert.Equal(201,activity.Modified("ore"));
    }
    [Fact]
    public void ContainerContentsCanTouchOneStackWithoutAffectingOtherNetworks()
    {
        var first=new StackActivity(); var second=new StackActivity();
        var totals=new Dictionary<string,long>{{"container",1},{"ore",1}};
        first.Observe(totals,10); second.Observe(totals,10);
        first.Observe(totals,20,new HashSet<string>{"container"});
        Assert.Equal(20,first.Modified("container"));
        Assert.Equal(0,first.Modified("ore"));
        Assert.Equal(0,second.Modified("container"));
    }
}
