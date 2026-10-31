using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;
using Xunit;
namespace DeepSpaceAE.Tests;

public sealed class LiquidCompositionTests
{
    private static LiquidComposition Mix(params (string Id,int Amount)[] parts)=>new(parts.Select(p=>new KeyValuePair<string,int>(p.Id,p.Amount)));
    [Fact]
    public void TinyDifferencesAndDifferentContaminantsNeverShareAComposition()
    {
        var a=Mix(("water",999999),("heavy_metal",1));
        var b=Mix(("water",999998),("heavy_metal",2));
        var c=Mix(("water",999999),("microbe",1));
        Assert.NotEqual(a.Signature,b.Signature); Assert.NotEqual(a.Signature,c.Signature);
        Assert.NotEqual(a.Signature,Mix(("water",1000000)).Signature);
    }
    [Fact]
    public void OrderZeroComponentsAndVolumeDoNotChangeTheExactMixtureRatio()
    {
        var a=Mix(("water",900), ("mineral",100), ("microbe",0));
        var b=Mix(("mineral",200), ("water",1800));
        Assert.Equal(a.Signature,b.Signature); Assert.Equal(1000L,a.Volume); Assert.Equal(2000L,b.Volume);
        Assert.Equal(2,a.Parts.Count); Assert.Equal("empty",Mix(("water",0)).Signature);
    }
    [Fact]
    public void CatalogPreservesSeparateSourcesEvenForTheSameComposition()
    {
        var mixture=Mix(("water",1000)); var first=Guid.NewGuid(); var second=Guid.NewGuid();
        var catalog=new CatalogSearch(new[]{new CatalogRecord(first,mixture.Signature,"Water",Array.Empty<string>(),1000),
            new CatalogRecord(second,mixture.Signature,"Water",Array.Empty<string>(),2000)});
        var rows=catalog.Find("water",sort:CatalogSort.Quantity);
        Assert.Equal(2,rows.Count); Assert.Equal(second,rows[0].Instance); Assert.Equal(first,rows[1].Instance);
    }
    [Fact]
    public void InvalidComponentsAreRejectedAndLargeTotalsCannotOverflow()
    {
        Assert.Throws<ArgumentException>(()=>Mix(("water",-1)));
        Assert.Throws<ArgumentException>(()=>Mix(("water",1),("water",2)));
        Assert.Throws<ArgumentException>(()=>Mix(("",1)));
        Assert.Equal(2L*int.MaxValue,Mix(("water",int.MaxValue),("mineral",int.MaxValue)).Volume);
        Assert.NotEqual(Mix(("a;1:b",1)).Signature,Mix(("a",1),("b",1)).Signature);
    }
}
