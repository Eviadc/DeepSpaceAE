using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;
using Xunit;
namespace DeepSpaceAE.Tests;

public sealed class CablePortTests
{
    [Fact]
    public void OnlyInstalledModelFacesOpenAndCenterDoesNotOpenCableUi()
    {
        Assert.Equal(0,CablePorts.FaceAt(15,8,0.5f)); Assert.Equal(1,CablePorts.FaceAt(15,15.5f,8));
        Assert.Equal(2,CablePorts.FaceAt(15,8,15.5f)); Assert.Equal(3,CablePorts.FaceAt(15,0.5f,8));
        Assert.Equal(-1,CablePorts.FaceAt(15,8,8)); Assert.Equal(-1,CablePorts.FaceAt(14,8,0.5f));
        Assert.Equal(-1,CablePorts.FaceAt(15,0,0)); Assert.Equal(-1,CablePorts.FaceAt(15,16,8));
    }
    [Fact]
    public void InstallPrefersDisconnectedFacesThenCutsOneConnectedFace()
    {
        Assert.Equal(1,CablePorts.ChooseFace(0,13));
        Assert.Equal(0,CablePorts.ChooseFace(0,15));
        int installed = 0;
        for (int i = 0; i < 4; i++)
        {
            int side = CablePorts.ChooseFace(installed,15);
            Assert.Equal(0,installed & (1 << side)); installed |= 1 << side;
        }
        Assert.Equal(15,installed); Assert.Equal(-1,CablePorts.ChooseFace(installed,0));
        Assert.Equal(0,CablePorts.TransmissionMask(installed,15));
    }
    [Fact]
    public void BlockedFacesCannotTransmitRegardlessOfConnectionMask()
    {
        for (int installed=0;installed<16;installed++) for(int connected=0;connected<16;connected++)
        {
            int transmitting=CablePorts.TransmissionMask(installed,connected);
            Assert.Equal(0,transmitting & installed); Assert.Equal(0,transmitting & ~connected);
            int selected=CablePorts.ChooseFace(installed,connected);
            if(selected>=0) Assert.Equal(0,installed & (1<<selected));
            if((~installed & ~connected & 15)!=0) Assert.Equal(0,connected & (1<<selected));
        }
    }
    [Fact]
    public void FourInternalBusesDoNotBridgeAnIsolatedCableBackToCore()
    {
        var nodes=new[]{new NetworkNode("core",NetworkPart.Core),new NetworkNode("wire",NetworkPart.Pipeline)}
            .Concat(Enumerable.Range(0,4).Select(i=>new NetworkNode("bus"+i,NetworkPart.ItemStorageBus,"owner"+i)));
        var links=Enumerable.Range(0,4).Select(i=>new NetworkLink("wire","bus"+i));
        var evaluation=new NetworkEvaluation(PhysicalNetworkProjection.Build(nodes,links,new ChannelSettings()));
        Assert.All(evaluation.Devices.Values,d=>Assert.Equal(DeviceStatus.NoCore,d.Status));
        Assert.Equal(2,evaluation.NightCosts["core"]);
    }
    [Fact]
    public void QuantitySortGroupsTypesByTotalWithoutMergingInstances()
    {
        var a=Enumerable.Range(0,3).Select(_=>new CatalogRecord(Guid.NewGuid(),"a","A",new[]{"visible"},1)).ToArray();
        var b=new CatalogRecord(Guid.NewGuid(),"b","B",new[]{"visible"},2);
        var hidden=new CatalogRecord(Guid.NewGuid(),"b","B",new[]{"hidden"},100);
        var search=new CatalogSearch(a.Append(b).Append(hidden));
        var visible=search.Find("#visible",sort:CatalogSort.Quantity);
        Assert.Equal(4,visible.Count); Assert.All(visible.Take(3),r=>Assert.Equal("a",r.ItemId));
        Assert.Same(b,visible[3]); Assert.Equal(4,visible.Select(r=>r.Instance).Distinct().Count());
        Assert.Same(hidden,search.Find("",sort:CatalogSort.Quantity)[0]);
    }
}
