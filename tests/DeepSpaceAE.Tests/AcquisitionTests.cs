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
        Assert.Equal(15,AcquisitionCatalog.Offers.Count);
        Assert.Equal(15,AcquisitionCatalog.Offers.Select(o=>o.Id).Distinct().Count());
        Assert.DoesNotContain(AcquisitionCatalog.Offers,o=>o.Id=="deepspaceae.energy_receiver");
        Assert.Equal(350,AcquisitionCatalog.Price("deepspaceae.energy_receiver"));
        Assert.All(AcquisitionCatalog.Offers,o=>{ Assert.InRange(o.Chance,1,100); Assert.True(o.Price>0); Assert.InRange(o.Minimum,1,o.Maximum); });
        Assert.Equal(AcquisitionCatalog.Offers.Count,AcquisitionCatalog.RollStock(_=>true,(min,_)=>min).Distinct().Count());
        Assert.All(AcquisitionCatalog.Starter,id=>Assert.Contains(AcquisitionCatalog.Offers,o=>o.Id==id));
    }
    [Fact]
    public void StockRollHonorsEveryQuantityAndGuaranteesCableAndCapacityCards()
    {
        var basic=AcquisitionCatalog.RollStock(p=>p==100,(min,_)=>min);
        Assert.Equal(6,basic.Count(id=>id=="deepspaceae.pipeline"));
        Assert.Equal(6,basic.Count(id=>id=="deepspaceae.capacity_upgrade"));
        Assert.Equal(12,basic.Count);
        Assert.DoesNotContain("deepspaceae.pipeline",AcquisitionCatalog.CounterFill);
        var all=AcquisitionCatalog.RollStock(_=>true,(_,max)=>max);
        foreach(var offer in AcquisitionCatalog.Offers) Assert.Equal(offer.Maximum,all.Count(id=>id==offer.Id));
        Assert.Throws<InvalidOperationException>(()=>AcquisitionCatalog.RollStock(_=>true,(_,max)=>max+1));
    }
    [Fact]
    public void StarterKitAndUpgradeCostMatchTheAgreedBaseline()
    {
        Assert.Equal(200,AcquisitionCatalog.EngineerCost); Assert.Equal(16,AcquisitionCatalog.Starter.Count);
        Assert.Equal(8,AcquisitionCatalog.Starter.Count(id=>id=="deepspaceae.pipeline"));
        Assert.Equal(2,AcquisitionCatalog.Starter.Count(id=>id=="deepspaceae.disk_small"));
        Assert.Single(AcquisitionCatalog.Starter,id=>id=="deepspaceae.core");
        Assert.DoesNotContain("deepspaceae.disk_large",AcquisitionCatalog.Starter);
    }
}
