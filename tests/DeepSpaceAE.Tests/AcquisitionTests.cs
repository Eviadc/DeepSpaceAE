using System;
using System.Linq;
using DeepSpaceAE.Core.Network;
using Xunit;
namespace DeepSpaceAE.Tests;
public sealed class AcquisitionTests
{
    [Fact]
    public void StockCatalogCoversEveryComponentWithDistinctNonzeroAppearanceChances()
    {
        Assert.Equal(13,AcquisitionCatalog.Offers.Count);
        Assert.Equal(13,AcquisitionCatalog.Offers.Select(o=>o.Id).Distinct().Count());
        Assert.Equal(13,AcquisitionCatalog.Offers.Select(o=>o.Chance).Distinct().Count());
        Assert.All(AcquisitionCatalog.Offers,o=>{ Assert.InRange(o.Chance,1,100); Assert.True(o.Price>0); Assert.InRange(o.Minimum,1,o.Maximum); });
        Assert.Equal(AcquisitionCatalog.Offers.Count,AcquisitionCatalog.RollStock(_=>true,(min,_)=>min).Distinct().Count());
        Assert.All(AcquisitionCatalog.Starter,id=>Assert.Contains(AcquisitionCatalog.Offers,o=>o.Id==id));
    }
    [Fact]
    public void SteveCannotRollBeforeDay15OrRerollAnAlreadyRecordedDay()
    {
        Assert.False(AcquisitionCatalog.CanRoll(14,-1,-1)); Assert.True(AcquisitionCatalog.CanRoll(15,14,14));
        Assert.False(AcquisitionCatalog.CanRoll(15,15,-1)); Assert.False(AcquisitionCatalog.CanRoll(15,14,15));
        Assert.True(AcquisitionCatalog.CanRoll(16,15,15)); Assert.Equal(5,AcquisitionCatalog.VisitChance);
    }
    [Fact]
    public void StockRollHonorsEveryQuantityAndGuaranteesCableAtTheConfiguredOdds()
    {
        var basic=AcquisitionCatalog.RollStock(p=>p==100,(min,_)=>min);
        Assert.Equal(4,basic.Count); Assert.All(basic,id=>Assert.Equal("deepspaceae.pipeline",id));
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
    [Fact]
    public void UnconfirmedGiftCannotReplayAfterReconstructionAndConfirmedItemsAdvanceOnce()
    {
        var journal=new DeliveryProgress(2); Assert.True(journal.Begin()); Assert.False(journal.Begin());
        var restored=new DeliveryProgress(journal.Total,journal.Next,journal.InFlight); Assert.False(restored.Begin());
        journal.Confirm(); Assert.Equal(1,journal.Next); Assert.Equal(0,journal.InFlight);
        Assert.Throws<InvalidOperationException>(()=>journal.Confirm());
        Assert.True(journal.Begin()); journal.Confirm(); Assert.False(journal.Begin());
        Assert.Throws<ArgumentOutOfRangeException>(()=>new DeliveryProgress(2,-1));
    }
}
