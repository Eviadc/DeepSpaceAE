using System.Linq;
using DeepSpaceAE.Core.Network;
using Xunit;
namespace DeepSpaceAE.Tests;

public sealed class EngineerStartTests
{
    [Fact]
    public void EngineerAddsTheUnchangedBasicKitAndOnePowerSourceToGeneralistSupplies()
    {
        var normal = EngineerStart.Items(false); var skipped = EngineerStart.Items(true);
        Assert.Equal(25, normal.Count); Assert.Equal(27, skipped.Count); Assert.Equal(50, EngineerStart.GeneralistCost);
        Assert.Equal(AcquisitionCatalog.Starter, normal.Where(id => id.StartsWith("deepspaceae.")));
        Assert.Single(normal, id => id == EngineerStart.Battery);
        Assert.Equal(2, normal.Count(id => id == "toilet_paper"));
        Assert.Equal(2, normal.Count(id => id == "processed_meat"));
        Assert.DoesNotContain("sign_food", normal); Assert.Single(skipped, id => id == "sign_food");
        Assert.Single(skipped, id => id == "sign_household");
    }
}
