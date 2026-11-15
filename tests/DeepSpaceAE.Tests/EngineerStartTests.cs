using System.Linq;
using DeepSpaceAE.Core.Network;
using Xunit;
namespace DeepSpaceAE.Tests;

public sealed class EngineerStartTests
{
    [Fact]
    public void StartsAndUpgradesSupplyTwoWirelessConnectorsAndNoBattery()
    {
        var normal = EngineerStart.Items(false); var skipped = EngineerStart.Items(true);
        Assert.Equal(32, normal.Count); Assert.Equal(34, skipped.Count); Assert.Equal(50, EngineerStart.GeneralistCost);
        Assert.Equal(24,normal.Count(id=>id.StartsWith("deepspaceae.")));
        foreach(var kit in new[]{normal,skipped,AcquisitionCatalog.UpgradeGifts})
        {
            Assert.Equal(2,kit.Count(id=>id==AcquisitionCatalog.Wireless));
            Assert.DoesNotContain("energy_credit",kit);
            Assert.DoesNotContain(kit,id=>id.Contains("battery",System.StringComparison.OrdinalIgnoreCase));
        }
        Assert.Equal(2, normal.Count(id => id == "toilet_paper"));
        Assert.Equal(2, normal.Count(id => id == "processed_meat"));
        Assert.DoesNotContain("sign_food", normal); Assert.Single(skipped, id => id == "sign_food");
        Assert.Single(skipped, id => id == "sign_household");
    }
}
