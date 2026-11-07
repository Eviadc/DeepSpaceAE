using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DeepSpaceAE.Core.Network;

namespace DeepSpaceAE.GameAdapter;

/// <summary>Display grouping only. The first real matching item remains the hit target.</summary>
internal sealed class TerminalStack
{
    internal readonly CatalogRecord Top;
    internal long Count;
    private TerminalStack(CatalogRecord top) { Top=top; Count=top.Quantity; }
    internal static IReadOnlyList<TerminalStack> Pack(IReadOnlyList<CatalogRecord> rows,
        IReadOnlyDictionary<Guid,string?> keys,CatalogSort sort,IReadOnlyDictionary<Guid,long> modified)
    {
        var stacks=new List<TerminalStack>(); var byKey=new Dictionary<string,TerminalStack>(StringComparer.Ordinal);
        foreach(var row in rows)
        {
            string? key=keys[row.Instance];
            if(key!=null && byKey.TryGetValue(key,out var stack)) { stack.Count+=row.Quantity; continue; }
            stack=new TerminalStack(row); stacks.Add(stack);
            if(key!=null) byKey.Add(key,stack);
        }
        IEnumerable<TerminalStack> ordered=stacks;
        if(sort==CatalogSort.Name) ordered=ordered.OrderBy(s=>s.Top.Name,StringComparer.CurrentCultureIgnoreCase);
        if(sort==CatalogSort.Quantity) ordered=ordered.OrderByDescending(s=>s.Count)
            .ThenBy(s=>s.Top.ItemId,StringComparer.Ordinal);
        if(sort==CatalogSort.Modified) ordered=ordered.OrderByDescending(s=>modified[s.Top.Instance])
            .ThenBy(s=>s.Top.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(s=>s.Top.ItemId,StringComparer.Ordinal);
        return ordered.ToArray();
    }
}

/// <summary>Exact known state, excluding identity/count/placement; no item cloning or native writes.</summary>
internal static class TerminalStackKey
{
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static readonly FieldInfo gridItems=typeof(GameGridInventory).GetField("items",Fields)!;
    private static readonly FieldInfo scrollItems=typeof(GameGridScrollableInventory).GetField("items",Fields)!;

    internal static string? Read(GameItem item,ISet<GameInventory>? dependencies=null,bool storageGrouping=false,bool includeContents=false)
    {
        // Unknown item/feature subclasses may carry additional state. Keep their
        // native entries independent rather than guessing that they are equal.
        if(item.GetType()!=typeof(GameItemElement) || item.itemFeatures.Any(f=>f.GetType()!=typeof(ItemFeature))) return null;
        var contents=GameSession.Contents(item);
        foreach(var inv in contents) dependencies?.Add(inv);
        if(item.child!=null && contents.Count==0) return null;
        foreach(var inv in contents)
        {
            bool empty=inv switch {
                GameSlotInventory slot=>slot.childItem==null,
                GameGridInventory grid when grid.GetType()==typeof(GameGridInventory) || grid is DiskAreaInventory || grid is DrawerInventory
                    =>((ICollection)gridItems.GetValue(grid)!).Count==0,
                GameGridScrollableInventory scroll when scroll.GetType()==typeof(GameGridScrollableInventory)
                    =>((ICollection)scrollItems.GetValue(scroll)!).Count==0,
                _=>false
            };
            if(!empty && !includeContents) return null;
        }
        using var bytes=new MemoryStream(); using var writer=new BinaryWriter(bytes);
        foreach(var text in new[]{item.identifier,item.name,item.spriteAtlasPath,item.spritePath,item.customText,
            item.shortDescription,item.longDescription,item.flavorText}) WriteString(writer,text);
        writer.Write(item.useSpriteFromMod); writer.Write(item.unitValue); writer.Write(item.unitBaseValue);
        writer.Write(item.backupUnitValue); writer.Write(item.lateUnitValue); writer.Write(ItemPinHelper.IsPinned(item));
        var types=item.GetGameItemType(); writer.Write(types.Count);
        foreach(var type in types) WriteString(writer,type);
        WriteShape(writer,item.shape); WriteShape(writer,item.modifiedShape);
        WriteTags(writer,item.state,storageGrouping); WriteTags(writer,item.modifiedState,storageGrouping);
        writer.Write(item.itemFeatures.Count);
        foreach(var feature in item.itemFeatures) foreach(var field in ItemStackState.FeatureFields)
            if(!WriteValue(writer,field.GetValue(feature))) return null;
        writer.Write(contents.Count);
        foreach(var inv in contents)
        {
            WriteString(writer,inv.GetType().FullName);
            writer.Write(inv.IsInsertLocked()); writer.Write(inv.IsRemoveLocked());
            if(inv is GameGridInventory grid) WriteShape(writer,grid.inventoryShape);
            if(inv is GameGridScrollableInventory scroll) { writer.Write(scroll.width); writer.Write(scroll.height); }
        }
        // Use the complete canonical bytes as the dictionary key: hash collisions
        // never turn differing state into an identical display stack.
        return Convert.ToBase64String(bytes.GetBuffer(),0,(int)bytes.Length);
    }
    private static void WriteString(BinaryWriter writer,string? value)
    { writer.Write(value!=null); if(value!=null) writer.Write(value); }
    private static void WriteShape(BinaryWriter writer,GridShape shape)
    {
        writer.Write(shape.width); writer.Write(shape.height);
        for(int y=0;y<shape.height;y++) for(int x=0;x<shape.width;x++) writer.Write(shape.GetLocal(x,y));
    }
    private static void WriteTags(BinaryWriter writer,TagSystem tags,bool storageGrouping)
    {
        int count=0;
        foreach(var entry in tags.dict) if(ItemStackState.IncludeTag(entry.Key,entry.Value,storageGrouping)) count++;
        writer.Write(count);
        foreach(var entry in tags.dict.OrderBy(p=>p.Key,StringComparer.Ordinal))
        {
            if(!ItemStackState.IncludeTag(entry.Key,entry.Value,storageGrouping)) continue;
            WriteString(writer,entry.Key); var tag=entry.Value;
            if(!storageGrouping) { WriteString(writer,tag.identifier); WriteString(writer,tag.identifierName); }
            writer.Write(tag.valueEnabled); writer.Write(tag.valueInt); writer.Write(tag.valueLong);
            writer.Write(tag.valueFloat); writer.Write(tag.valueDouble); writer.Write(tag.valueBool);
            WriteString(writer,tag.valueString);
        }
    }
    private static bool WriteValue(BinaryWriter writer,object? value)
    {
        if(value==null) { writer.Write((byte)0); return true; }
        WriteString(writer,value.GetType().FullName);
        switch(value)
        {
            case string text: writer.Write(text); break;
            case bool flag: writer.Write(flag); break;
            case int number: writer.Write(number); break;
            case long number: writer.Write(number); break;
            case float number: writer.Write(number); break;
            case double number: writer.Write(number); break;
            case Enum number: writer.Write(Convert.ToInt64(number)); break;
            case ItemCondition condition when condition.GetType()==typeof(ItemCondition):
                foreach(var field in ItemStackState.ConditionFields) if(!WriteValue(writer,field.GetValue(condition))) return false;
                break;
            default: return false;
        }
        return true;
    }
}
