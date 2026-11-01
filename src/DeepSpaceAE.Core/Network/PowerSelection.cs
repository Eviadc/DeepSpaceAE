using System;
using System.Collections.Generic;
using System.Linq;
namespace DeepSpaceAE.Core.Network;
public sealed class PowerCandidate
{
    public long Id { get; }
    public int Capacity { get; }
    public bool Preferred { get; }
    public PowerCandidate(long id,int capacity,bool preferred=false) { Id=id; Capacity=capacity; Preferred=preferred; }
}
public sealed class PowerShare
{
    public long Id { get; }
    public int Amount { get; }
    public PowerShare(long id,int amount) { Id=id; Amount=amount; }
}
public static class PowerSelection
{
    // Minimum cardinality first, maximum retained reservations second. With a fixed
    // number from each group, the highest capacities are the optimal feasible set.
    public static IReadOnlyList<PowerShare>? Select(int required,IEnumerable<PowerCandidate> candidates)
    {
        if(required<=0) throw new ArgumentOutOfRangeException(nameof(required));
        var all=candidates.ToArray();
        if(all.Any(c=>c.Id==0 || c.Capacity<0) || all.Select(c=>c.Id).Distinct().Count()!=all.Length)
            throw new ArgumentException("Invalid/duplicate power source.",nameof(candidates));
        all=all.Where(c=>c.Capacity>0).OrderByDescending(c=>c.Capacity).ThenBy(c=>c.Id).ToArray();
        int count=0; long total=0;
        while(count<all.Length && total<required) total+=all[count++].Capacity;
        if(total<required) return null;
        var preferred=all.Where(c=>c.Preferred).ToArray(); var spare=all.Where(c=>!c.Preferred).ToArray();
        for(int retain=Math.Min(count,preferred.Length);retain>=0;retain--)
        {
            if(count-retain>spare.Length) continue;
            var selected=preferred.Take(retain).Concat(spare.Take(count-retain)).ToArray();
            if(selected.Sum(c=>(long)c.Capacity)<required) continue;
            var shares=new List<PowerShare>(); int remaining=required;
            foreach(var source in selected) { int amount=Math.Min(remaining,source.Capacity); if(amount>0) shares.Add(new PowerShare(source.Id,amount)); remaining-=amount; }
            return shares.AsReadOnly();
        }
        throw new InvalidOperationException("Feasible power selection was not constructed.");
    }
}
