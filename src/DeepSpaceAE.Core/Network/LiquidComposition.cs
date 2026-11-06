using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace DeepSpaceAE.Core.Network;

/// <summary>Exact native component amounts. No purity rounding or volume-based merging.</summary>
public sealed class LiquidComposition
{
    public IReadOnlyList<KeyValuePair<string,int>> Parts { get; }
    public long Volume { get; }
    public string Signature { get; }
    public LiquidComposition(IEnumerable<KeyValuePair<string,int>> parts)
    {
        if(parts==null) throw new ArgumentNullException(nameof(parts));
        var all=parts.ToArray();
        if(all.Any(p=>string.IsNullOrWhiteSpace(p.Key) || p.Value<0) || all.Select(p=>p.Key).Distinct(StringComparer.Ordinal).Count()!=all.Length)
            throw new ArgumentException("Invalid or duplicate liquid component.",nameof(parts));
        var values=all.Where(p=>p.Value>0).OrderBy(p=>p.Key,StringComparer.Ordinal).ToArray();
        Parts=Array.AsReadOnly(values); Volume=values.Sum(p=>(long)p.Value);
        int divisor=0;
        foreach(var part in values) { int value=part.Value; while(value!=0) { int next=divisor%value; divisor=value; value=next; } }
        Signature=values.Length==0?"empty":string.Join(";",values.Select(p=>p.Key.Length.ToString(CultureInfo.InvariantCulture)+":"+p.Key+"="+(p.Value/divisor).ToString(CultureInfo.InvariantCulture)));
    }
}
