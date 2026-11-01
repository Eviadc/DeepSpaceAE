using System;
namespace DeepSpaceAE.Core.Network;
public enum NightPhase { Empty, Reserved, DebitStarted, Settled, Fault, Shortfall }

/// <summary>Persist before entering a native debit; an interrupted debit is never replayed.</summary>
public sealed class NightReservation
{
    public int Day { get; private set; }
    public int Required { get; private set; }
    public long Battery { get; private set; }
    public NightPhase Phase { get; private set; }
    public NightReservation(int day = -1, int required = 0, long battery = 0, NightPhase phase = NightPhase.Empty)
    { Day = day; Required = required; Battery = battery; Phase = phase; }
    public bool CanRemove(int day) => Phase == NightPhase.Empty || Phase == NightPhase.Shortfall || (Phase == NightPhase.Settled && day > Day);
    // Only a real pending reservation (or an observed unpaid grid bill) may enter
    // settlement. A settled plan is not renewed merely because the day changed.
    public bool CanSettle(bool knownGridUnpaid = false) => Phase == NightPhase.Reserved
        || Phase == NightPhase.DebitStarted && knownGridUnpaid;
    // An unpaid bill is already fixed. Offline topology growth must neither enlarge
    // that debt nor prevent its payment from producing a matching receipt.
    public int SettlementBudget(int day,int currentCost) => Phase == NightPhase.Reserved && Day==day
        ?Math.Max(Required,currentCost):Required;
    public bool ObservePeak(int day,int required)
    {
        if(required<=0 || Phase==NightPhase.DebitStarted || Phase==NightPhase.Fault) return false;
        if(day>Day && (Phase==NightPhase.Empty || Phase==NightPhase.Settled || Phase==NightPhase.Shortfall))
        { Day=day; Required=required; Battery=0; Phase=NightPhase.Empty; return true; }
        if(day!=Day || Phase==NightPhase.Settled) return false;
        Required=Math.Max(Required,required); return true;
    }
    public bool Reserve(int day, int required, long battery, Func<int, bool> canSupply)
    {
        if(!ObservePeak(day,required)) return false;
        if(Phase==NightPhase.Reserved && Battery!=battery) return false;
        if(Phase==NightPhase.Shortfall && Battery==battery) return false;
        if(!canSupply(Required))
        {
            if(Phase==NightPhase.Reserved) Phase=NightPhase.Shortfall;
            return false;
        }
        Battery=battery; Phase=NightPhase.Reserved; return true;
    }
    public bool RepricePending(int required)
    {
        if (Phase != NightPhase.Reserved || required <= 0) return false;
        Required = required; return true;
    }
    public bool BeginDebit(int day, long battery)
    {
        if (Phase != NightPhase.Reserved || Day > day || Battery != battery) return false;
        Day = day; Phase = NightPhase.DebitStarted; return true;
    }
    public void Complete(bool verified,int? billingDay=null)
    {
        if (Phase != NightPhase.DebitStarted) throw new InvalidOperationException("No debit in progress.");
        if(billingDay.HasValue && billingDay.Value<Day) throw new ArgumentOutOfRangeException(nameof(billingDay));
        if(verified && billingDay.HasValue) Day=billingDay.Value;
        Phase = verified ? NightPhase.Settled : NightPhase.Fault;
    }
}
