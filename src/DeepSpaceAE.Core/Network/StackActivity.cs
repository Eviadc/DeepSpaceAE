using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DeepSpaceAE.Core.Network;

/// <summary>Network-owned stack totals and modification order; never written to items.</summary>
public sealed class StackActivity
{
    private readonly Dictionary<string,(long Count,long Modified)> entries=new(StringComparer.Ordinal);
    private long clock;
    private bool initialized;
    public long Modified(string key)=>entries.TryGetValue(key,out var entry)?entry.Modified:0;
    public bool Observe(IReadOnlyDictionary<string,long> totals,long now,ISet<string>? touched=null)
    {
        bool changed=false;
        long stamp=Math.Max(now,clock+1);
        foreach(var key in entries.Keys.Where(k=>!totals.ContainsKey(k)).ToArray())
        { entries.Remove(key); changed=true; }
        foreach(var pair in totals)
        {
            if(entries.TryGetValue(pair.Key,out var old) && old.Count==pair.Value && touched?.Contains(pair.Key)!=true) continue;
            entries[pair.Key]=(pair.Value,initialized?stamp:0); changed=true;
        }
        if(changed) clock=stamp;
        initialized=true;
        return changed;
    }
    public string Encode()=>"1\n"+clock.ToString(CultureInfo.InvariantCulture)+"\n"+
        string.Join("\n",entries.OrderBy(e=>e.Key,StringComparer.Ordinal).Select(e=>
            e.Key+"\t"+e.Value.Count.ToString(CultureInfo.InvariantCulture)+"\t"+e.Value.Modified.ToString(CultureInfo.InvariantCulture)));
    public static StackActivity Decode(string text)
    {
        var result=new StackActivity();
        if(string.IsNullOrEmpty(text)) return result;
        var rows=text.Split('\n');
        if(rows.Length<2 || rows[0]!="1" || !long.TryParse(rows[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out result.clock))
            throw new FormatException("Invalid network activity header.");
        foreach(var row in rows.Skip(2).Where(r=>r.Length>0))
        {
            var cells=row.Split('\t');
            if(cells.Length!=3 || !long.TryParse(cells[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out var count)
                || !long.TryParse(cells[2],NumberStyles.Integer,CultureInfo.InvariantCulture,out var modified))
                throw new FormatException("Invalid network activity entry.");
            result.entries.Add(cells[0],(count,modified));
        }
        result.initialized=true;
        return result;
    }
}
