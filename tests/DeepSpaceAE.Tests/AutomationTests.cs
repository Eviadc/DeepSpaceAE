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
    public void IoBusesAreLeavesAndAreIncludedInDailyCost()
    {
        var nodes=new[]{new NetworkNode("core",NetworkPart.Core),new NetworkNode("wire",NetworkPart.Pipeline),
            new NetworkNode("import",NetworkPart.ImportBus),new NetworkNode("export",NetworkPart.ExportBus)};
        var evaluation=new NetworkEvaluation(PhysicalNetworkProjection.Build(nodes,new[]{new NetworkLink("core","wire"),
            new NetworkLink("wire","import"),new NetworkLink("wire","export")}));
        Assert.Equal(2,evaluation.EndpointCounts["core"]); Assert.Equal(4,evaluation.NightCosts["core"]);
        Assert.Equal(DeviceStatus.Online,evaluation.Devices["import"].Status);
        Assert.Equal(DeviceStatus.Online,evaluation.Devices["export"].Status);
    }
}
