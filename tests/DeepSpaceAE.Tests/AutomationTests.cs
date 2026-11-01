using DeepSpaceAE.Core.Network;
using Xunit;
namespace DeepSpaceAE.Tests;
public sealed class AutomationTests
{
    [Fact]
    public void Ae2DirectionsUseOnlyTheOppositeMachineRoles()
    {
        Assert.Equal("output",AutomationRules.MachineRole(true));
        Assert.Equal("input",AutomationRules.MachineRole(false));
    }
    [Fact]
    public void PaidNightPowerWorksWithAnEmptySettledBatteryButNeverExpandsThePaidPeak()
    {
        Assert.True(AutomationRules.PaidNightPower(5,5,true,42,42,12,12));
        Assert.False(AutomationRules.PaidNightPower(5,5,true,42,42,12,13));
        Assert.False(AutomationRules.PaidNightPower(6,5,true,42,42,12,2));
        Assert.False(AutomationRules.PaidNightPower(5,5,false,42,42,12,2));
        Assert.False(AutomationRules.PaidNightPower(5,5,true,42,43,12,2));
    }
    [Fact]
    public void NativePartialFullAndStackedTransfersHaveDifferentSourcePostconditions()
    {
        Assert.True(AutomationRules.SourceRemainder(10,4,6,true,false,false));
        Assert.True(AutomationRules.SourceRemainder(10,10,10,false,true,false));
        Assert.True(AutomationRules.SourceRemainder(10,10,0,false,false,true));
        Assert.False(AutomationRules.SourceRemainder(10,4,10,true,false,false));
        Assert.False(AutomationRules.SourceRemainder(10,10,10,true,true,false));
        Assert.False(AutomationRules.SourceRemainder(10,10,10,false,false,false));
        Assert.False(AutomationRules.SourceRemainder(10,11,0,false,true,false));
    }
    [Fact]
    public void IoBusesAreChannelLeavesAndAreIncludedInDailyCost()
    {
        var nodes=new[]{new NetworkNode("core",NetworkPart.Core),new NetworkNode("wire",NetworkPart.Pipeline),
            new NetworkNode("import",NetworkPart.ImportBus),new NetworkNode("export",NetworkPart.ExportBus)};
        var evaluation=new NetworkEvaluation(PhysicalNetworkProjection.Build(nodes,new[]{new NetworkLink("core","wire"),
            new NetworkLink("wire","import"),new NetworkLink("wire","export")},new ChannelSettings()));
        Assert.Equal(2,evaluation.ChannelsUsed["core"]); Assert.Equal(4,evaluation.NightCosts["core"]);
        Assert.Equal(DeviceStatus.Online,evaluation.Devices["import"].Status);
        Assert.Equal(DeviceStatus.Online,evaluation.Devices["export"].Status);
    }
    [Fact]
    public void SourceDecrementAloneCannotProveSuccessfulStackOrSplitDelivery()
    {
        Assert.True(AutomationRules.SourceRemainder(10,4,6,true,false,false));
        Assert.False(AutomationRules.DestinationReceipt(4,0,0,false)); // lost split
        Assert.False(AutomationRules.DestinationReceipt(4,20,20,true)); // unchanged target
        Assert.False(AutomationRules.DestinationReceipt(4,20,25,true)); // unexpected callback
        Assert.True(AutomationRules.DestinationReceipt(4,0,4,true));
        Assert.True(AutomationRules.DestinationReceipt(4,20,24,true));
        Assert.False(AutomationRules.DestinationReceipt(1,int.MaxValue,int.MinValue,true));
    }
}
