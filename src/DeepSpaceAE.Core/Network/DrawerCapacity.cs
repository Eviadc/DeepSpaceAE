using System;
namespace DeepSpaceAE.Core.Network;

public static class DrawerCapacity
{
    public const int Base = 16, MaximumUpgrades = 5;
    public static int Limit(int upgrades)
    {
        if (upgrades < 0 || upgrades > MaximumUpgrades) throw new ArgumentOutOfRangeException(nameof(upgrades));
        int limit = Base;
        for (int i = 0; i < upgrades; i++) limit = checked(limit * 4);
        return limit;
    }
    public static bool Fits(int limit, long used, int existing, int incoming)
        => limit > 0 && used >= 0 && existing >= 0 && existing <= used && incoming > 0 && incoming <= limit && used - existing <= limit - incoming;
}
