using System;
namespace DeepSpaceAE.Core.Network;

/// <summary>Local cable faces; upgrades block transmission in that direction.</summary>
public static class CablePorts
{
    public static int ChooseFace(int installedMask, int connectedMask)
    {
        for (int side = 0; side < 4; side++)
            if ((installedMask & (1 << side)) == 0 && (connectedMask & (1 << side)) == 0) return side;
        for (int side = 0; side < 4; side++) if ((installedMask & (1 << side)) == 0) return side;
        return -1;
    }
    public static int TransmissionMask(int installedMask, int connectedMask) => connectedMask & ~installedMask & 15;
}
