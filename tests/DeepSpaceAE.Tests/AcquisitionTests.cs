using System;
using System.Linq;
using DeepSpaceAE.Core.Network;
using Xunit;
namespace DeepSpaceAE.Tests;
public sealed class AcquisitionTests
{
    [Fact]
    public void StockCatalogExcludesTheReceiverAndKeepsNonzeroAppearanceChances()
    {
        Assert.Equal(17,AcquisitionCatalog.Offers.Count);
        Assert.Equal(17,AcquisitionCatalog.Offers.Select(o=>o.Id).Distinct().Count());
        Assert.DoesNotContain(AcquisitionCatalog.Offers,o=>o.Id=="deepspaceae.energy_receiver");
        Assert.DoesNotContain(AcquisitionCatalog.Offers,o=>o.Id=="deepspaceae.fluid_interface");
        Assert.Contains(AcquisitionCatalog.Offers,o=>o.Id=="deepspaceae.wireless_connector" && o.Minimum>=2);
        Assert.Equal(350,AcquisitionCatalog.Price("deepspaceae.energy_receiver"));
        Assert.All(AcquisitionCatalog.Offers,o=>{ Assert.InRange(o.Chance,1,100); Assert.True(o.Price>0); Assert.InRange(o.Minimum,1,o.Maximum); });
        Assert.Equal(AcquisitionCatalog.Offers.Count,AcquisitionCatalog.RollStock(_=>true,(min,_)=>min).Distinct().Count());
        Assert.All(AcquisitionCatalog.Starter,id=>Assert.Contains(AcquisitionCatalog.Offers,o=>o.Id==id));
    }
    [Fact]
    public void StockRollHonorsEveryQuantityAndGuaranteesCableAndCapacityCards()
    {
        var basic=AcquisitionCatalog.RollStock(p=>p==100,(min,_)=>min);
        Assert.Equal(3,basic.Count(id=>id=="deepspaceae.pipeline"));
        Assert.Equal(3,basic.Count(id=>id=="deepspaceae.capacity_upgrade"));
        Assert.Equal(10,basic.Count);
        Assert.Single(basic,id=>id=="deepspaceae.tag_upgrade");
        Assert.Equal("deepspaceae.disk_large",basic[0]);
        Assert.Single(basic,id=>id=="deepspaceae.disk_large");
        Assert.Equal(2,basic.Count(id=>id=="deepspaceae.disk_small"));
        var all=AcquisitionCatalog.RollStock(_=>true,(_,max)=>max);
        foreach(var offer in AcquisitionCatalog.Offers)
            Assert.Equal(offer.Maximum+AcquisitionCatalog.GuaranteedStock.Count(id=>id==offer.Id),all.Count(id=>id==offer.Id));
        Assert.Throws<InvalidOperationException>(()=>AcquisitionCatalog.RollStock(_=>true,(_,max)=>max+1));
    }
    [Fact]
    public void StarterKitAndUpgradeCostMatchTheAgreedBaseline()
    {
        Assert.Equal(200,AcquisitionCatalog.EngineerCost); Assert.Equal(18,AcquisitionCatalog.Starter.Count);
        Assert.Equal(8,AcquisitionCatalog.Starter.Count(id=>id=="deepspaceae.pipeline"));
        Assert.Equal(2,AcquisitionCatalog.Starter.Count(id=>id=="deepspaceae.disk_small"));
        Assert.Single(AcquisitionCatalog.Starter,id=>id=="deepspaceae.core");
        Assert.DoesNotContain("deepspaceae.disk_large",AcquisitionCatalog.Starter);
    }
    [Fact]
    public void WirelessOffersKeepTheirChanceAndAlwaysContainCompletePairs()
    {
        foreach(bool optional in new[]{false,true})
        foreach(bool maximum in new[]{false,true})
        {
            var stock=AcquisitionCatalog.RollStock(chance=>chance==100 || optional,(min,max)=>maximum?max:min);
            int count=optional?(maximum?4:2):0;
            Assert.Equal(count,stock.Count(id=>id==AcquisitionCatalog.Wireless));
            Assert.All(stock.Take(count),id=>Assert.Equal(AcquisitionCatalog.Wireless,id));
        }
        var old=new[]{AcquisitionCatalog.Wireless,"deepspaceae.core",AcquisitionCatalog.Wireless,AcquisitionCatalog.Wireless};
        Assert.Equal(new[]{AcquisitionCatalog.Wireless,AcquisitionCatalog.Wireless,"deepspaceae.core"},AcquisitionCatalog.PairWirelessStock(old));
    }
    [Fact]
    public void SmallerLotsLeaveRoomForLaterStockWhileRetainingGuaranteedSupplies()
    {
        var maximal=AcquisitionCatalog.RollStock(_=>true,(_,max)=>max);
        Assert.InRange(maximal.Count,30,55);
        Assert.True(AcquisitionCatalog.Offers.Sum(o=>o.Chance/100.0*(o.Minimum+o.Maximum)/2)+AcquisitionCatalog.GuaranteedStock.Count<30);
        Assert.All(AcquisitionCatalog.GuaranteedStock,id=>Assert.Contains(id,maximal));
        Assert.Contains("deepspaceae.export_bus",maximal);
        Assert.Contains("deepspaceae.pipeline",maximal);
        Assert.Equal(4,maximal.Count(id=>id==AcquisitionCatalog.Wireless));
    }
}
