using System;
namespace DeepSpaceAE.Core.Network;
public static class CellCapacity
{
    public static bool Fits(int capacity,int used,int replaced,int incoming)
        =>capacity>=0 && used>=0 && replaced>=0 && incoming>0 && replaced<=used && (long)used-replaced+incoming<=capacity;
}
