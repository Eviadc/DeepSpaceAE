using System;
using System.Linq;
using System.Reflection;

namespace DeepSpaceAE.GameAdapter;

/// <summary>Read-only value semantics shared by drawer admission and terminal grouping.</summary>
internal static class ItemStackState
{
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    internal static readonly FieldInfo[] FeatureFields=typeof(ItemFeature).GetFields(Fields)
        .Where(f=>f.Name!="parentItemUniqueId").OrderBy(f=>f.Name,StringComparer.Ordinal).ToArray();
    internal static readonly FieldInfo[] ConditionFields=typeof(ItemCondition).GetFields(Fields)
        .OrderBy(f=>f.Name,StringComparer.Ordinal).ToArray();

    internal static bool IncludeTag(string key,TagState tag,bool storageGrouping)
    {
        if(!storageGrouping) return true; // IO exact samples keep their full raw state.
        if(key==PlayerStore.BOUGHT_PRICE_TAG) return false;
        // Native GetTagReadonly calls GetTag, which inserts a disabled, default
        // record when absent. Reading a tag must not create a new storage kind.
        // Disabled tags with any payload still carry state and must be compared.
        return tag.valueEnabled || tag.valueInt!=0 || tag.valueLong!=0
            || tag.valueFloat!=0 || tag.valueDouble!=0 || tag.valueBool
            || !string.IsNullOrEmpty(tag.valueString);
    }

    internal static bool SameTags(TagSystem a,TagSystem b)
    {
        int count=0;
        foreach(var entry in a.dict)
        {
            var tag=entry.Value;
            if(!IncludeTag(entry.Key,tag,true)) continue;
            count++;
            if(!b.dict.TryGetValue(entry.Key,out var other)
                || tag.valueEnabled!=other.valueEnabled || tag.valueInt!=other.valueInt
                || tag.valueLong!=other.valueLong || !tag.valueFloat.Equals(other.valueFloat)
                || !tag.valueDouble.Equals(other.valueDouble) || tag.valueBool!=other.valueBool
                || tag.valueString!=other.valueString) return false;
        }
        // The dictionary key is the tag identity; identifierName is display metadata.
        foreach(var entry in b.dict) if(IncludeTag(entry.Key,entry.Value,true)) count--;
        return count==0;
    }

    internal static bool SameFeature(ItemFeature a,ItemFeature b)
    {
        if(a.GetType()!=typeof(ItemFeature) || b.GetType()!=typeof(ItemFeature)) return false;
        foreach(var field in FeatureFields)
            if(!SameValue(field.GetValue(a),field.GetValue(b))) return false;
        return true;
    }

    private static bool SameValue(object? a,object? b)
    {
        if(a==null || b==null) return a==null && b==null;
        if(a.GetType()!=b.GetType()) return false;
        if(a is ItemCondition condition && a.GetType()==typeof(ItemCondition))
        {
            foreach(var field in ConditionFields)
                if(!SameValue(field.GetValue(condition),field.GetValue(b))) return false;
            return true;
        }
        // Do not fall back to reference equality for unknown extension state.
        return (a is string or bool or int or long or float or double or Enum) && a.Equals(b);
    }
}
