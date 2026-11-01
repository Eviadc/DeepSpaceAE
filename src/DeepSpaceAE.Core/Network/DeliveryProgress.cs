using System;
namespace DeepSpaceAE.Core.Network;
public sealed class DeliveryProgress
{
    public int Next { get; private set; }
    public int InFlight { get; private set; }
    public int Total { get; }
    public DeliveryProgress(int total,int next=0,int inFlight=0)
    {
        if(total<0 || next<0 || next>total || inFlight<0 || (inFlight!=0 && (next==total || inFlight!=next+1)))
            throw new ArgumentOutOfRangeException(nameof(next),"Invalid delivery journal; do not replay it.");
        Total=total; Next=next; InFlight=inFlight;
    }
    public bool Begin()
    { if(InFlight!=0 || Next>=Total) return false; InFlight=Next+1; return true; }
    public void Confirm()
    { if(InFlight!=Next+1 || Next>=Total) throw new InvalidOperationException("No delivery in progress."); Next++; InFlight=0; }
}
