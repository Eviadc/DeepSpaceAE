using System;
namespace DeepSpaceAE.Core.Network;

/// <summary>Local cable faces; upgrades block transmission in that direction.</summary>
public static class CablePorts
{
    // Footprint of storage_bus_base.json projected from above: two face ribs and a stem.
    public static int FaceAt(int installedMask, float x, float y)
    {
        if (x < 0 || y < 0 || x >= 16 || y >= 16) return -1;
        for (int side = 0; side < 4; side++)
        {
            if ((installedMask & (1 << side)) == 0) continue;
            float u = x, v = y;
            if (side == 1) { u = y; v = 16 - x; }
            else if (side == 2) { u = 16 - x; v = 16 - y; }
            else if (side == 3) { u = 16 - y; v = x; }
            if ((v >= 0 && v < 1 && u >= 3 && u < 13)
                || (v >= 1 && v < 2 && u >= 2 && u < 14)
                || (v >= 2 && v < 4 && u >= 5 && u < 11)
                || (v >= 4 && v < 6 && u >= 6 && u < 10)) return side;
        }
        return -1;
    }
    public static int ChooseFace(int installedMask, int connectedMask)
    {
        for (int side = 0; side < 4; side++)
            if ((installedMask & (1 << side)) == 0 && (connectedMask & (1 << side)) == 0) return side;
        for (int side = 0; side < 4; side++) if ((installedMask & (1 << side)) == 0) return side;
        return -1;
    }
    public static int TransmissionMask(int installedMask, int connectedMask) => connectedMask & ~installedMask & 15;
}
