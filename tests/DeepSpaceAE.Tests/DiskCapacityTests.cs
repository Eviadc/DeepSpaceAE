using System;
using DeepSpaceAE.Core;
using DeepSpaceAE.Core.Network;
using Xunit;
namespace DeepSpaceAE.Tests;

public sealed class DiskCapacityTests
{
    [Theory]
    [InlineData(64)]
    [InlineData(256)]
    public void FiveCellObjectsConsumeFiveCellsUntilTheLastObjectNoLongerFits(int capacity)
    {
        int used=0;
        while(CellCapacity.Fits(capacity,used,0,5)) used+=5;
        Assert.Equal(capacity/5*5,used);
        Assert.False(CellCapacity.Fits(capacity,used,0,5));
        Assert.True(CellCapacity.Fits(capacity,used,0,capacity-used));
        Assert.False(CellCapacity.Fits(capacity,capacity,0,1));
        Assert.True(CellCapacity.Fits(capacity,capacity-5,0,5));
    }
    [Fact]
    public void ShapeChangesCountReplacementAreaAndOverfullLoadsCannotGrow()
    {
        Assert.True(CellCapacity.Fits(64,64,5,5)); // rotation preserves area
        Assert.True(CellCapacity.Fits(64,64,5,3));
        Assert.False(CellCapacity.Fits(64,64,5,6));
        Assert.False(CellCapacity.Fits(64,70,0,1));
        Assert.True(CellCapacity.Fits(64,70,10,4));
        Assert.False(CellCapacity.Fits(int.MaxValue,int.MaxValue,0,int.MaxValue));
        Assert.False(CellCapacity.Fits(64,5,6,1));
    }
    [Fact]
    public void DriveUsesOneChannelRegardlessOfItsSixBaysAndNeverBridgesNetworks()
    {
        var core=new NetworkNode("core",NetworkPart.Core); var wire=new NetworkNode("wire",NetworkPart.Pipeline);
        var drive=new NetworkNode("drive",NetworkPart.DiskDrive,"six-bay-drive");
        var nodes=new[]{core,wire,drive,new NetworkNode("terminal",NetworkPart.Terminal)};
        var edges=new[]{new NetworkLink("core","wire"),new NetworkLink("wire","drive"),new NetworkLink("wire","terminal")};
        var topology=PhysicalNetworkProjection.Build(nodes,edges,new ChannelSettings());
        var evaluation=new NetworkEvaluation(topology);
        Assert.Equal(DeviceStatus.Online,evaluation.Devices["drive"].Status);
        Assert.Equal(2,evaluation.ChannelsUsed["core"]); Assert.Equal(4,evaluation.NightCosts["core"]);
        var item=new ItemInstance(Guid.NewGuid(),"stored-object","preserved-state");
        var catalog=new TerminalCatalog(topology,"terminal",new[]{new ContainerSnapshot("six-bay-drive",new[]{item})},evaluation);
        Assert.Equal(item.InstanceId,Assert.Single(catalog.Index.ByInstance).Key);
        var isolated=new NetworkNode("other",NetworkPart.Pipeline);
        var ambiguous=PhysicalNetworkProjection.Build(new[]{core,wire,drive,isolated},
            new[]{edges[0],edges[1],new NetworkLink("drive","other")},new ChannelSettings());
        Assert.NotEqual(DeviceStatus.Online,new NetworkEvaluation(ambiguous).Devices["drive"].Status);
        Assert.Throws<ArgumentException>(()=>new NetworkNode("bad",NetworkPart.DiskDrive));
    }
}
