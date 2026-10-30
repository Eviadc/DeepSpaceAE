using System;
namespace DeepSpaceAE.Core.Network;
public enum NightPhase { Empty, Reserved, DebitStarted, Settled, Fault }

/// <summary>Persist before entering a native debit; an interrupted debit is never replayed.</summary>
public sealed class NightReservation
{
    public int Day { get; private set; }
    public int Required { get; private set; }
    public long Battery { get; private set; }
    public NightPhase Phase { get; private set; }
    public NightReservation(int day = -1, int required = 0, long battery = 0, NightPhase phase = NightPhase.Empty)
    { Day = day; Required = required; Battery = battery; Phase = phase; }
    public bool CanRemove(int day) => Phase == NightPhase.Empty || (Phase == NightPhase.Settled && day > Day);
    public bool Reserve(int day, int required, long battery, Func<int, bool> canSupply)
    {
        if (required <= 0 || Phase == NightPhase.DebitStarted || Phase == NightPhase.Fault) return false;
        if (Phase == NightPhase.Settled && day <= Day) return false;
        if (Phase == NightPhase.Reserved && (Day != day || Battery != battery)) return false;
        int budget = Phase == NightPhase.Reserved ? Math.Max(Required, required) : required;
        if (!canSupply(budget)) return false;
        Day = day; Required = budget; Battery = battery; Phase = NightPhase.Reserved;
        return true;
    }
    public bool BeginDebit(int day, long battery)
    {
        if (Phase != NightPhase.Reserved || Day > day || Battery != battery) return false;
        Day = day; Phase = NightPhase.DebitStarted; return true;
    }
    public void Complete(bool verified)
    {
        if (Phase != NightPhase.DebitStarted) throw new InvalidOperationException("No debit in progress.");
        Phase = verified ? NightPhase.Settled : NightPhase.Fault;
    }
}
