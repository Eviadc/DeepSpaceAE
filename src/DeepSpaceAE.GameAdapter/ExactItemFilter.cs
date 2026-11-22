using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DeepSpaceAE.GameAdapter;

// The saved sample is also the hover description. Project the original binary
// snapshots through the same rules so existing white/blacklists keep working.
internal static class ExactItemFilter
{
    private const string Version="AE2\n";
    internal static string? Read(GameItem item) => Normalize(TerminalStackKey.Read(item));
    internal static string? Normalize(string? sample)
    {
        if(string.IsNullOrEmpty(sample)) return null;
        if(sample!.StartsWith(Version,StringComparison.Ordinal)) return sample;
        try
        {
            using var stream=new MemoryStream(Convert.FromBase64String(sample));
            using var r=new BinaryReader(stream);
            var lines=new List<string>();
            Add(lines,"ID",Text(r));
            Text(r); // Localized name
            Add(lines,"Atlas",Text(r));
            Add(lines,"Sprite",Text(r));
            Add(lines,"Text",Text(r));
            for(int i=0;i<3;i++) Text(r); // Localized descriptions
            if(r.ReadBoolean()) Add(lines,"Sprite source","mod");
            for(int i=0;i<4;i++) r.ReadInt64(); // Price and its cached derivatives
            r.ReadBoolean(); // Inventory pin
            for(int i=r.ReadInt32();i>0;i--) Add(lines,"Type",Text(r));
            Add(lines,"Shape",Shape(r));
            Shape(r); // Orientation and inventory modifiers
            Tags(r,null); // Base state
            Tags(r,lines); // Effective item properties
            var features=new List<string>();
            for(int i=r.ReadInt32();i>0;i--)
            {
                var feature=new List<string>();
                bool eventFeature=false, marketPrice=false;
                foreach(var field in ItemStackState.FeatureFields)
                {
                    var value=Value(r);
                    if(field.Name=="featureType") eventFeature=value==((int)ItemFeature.FeatureType.Event).ToString(CultureInfo.InvariantCulture);
                    if(field.Name=="valueStage") marketPrice=value==((int)ItemFeature.ValueStage.Market).ToString(CultureInfo.InvariantCulture);
                    if(value.Length==0 || field.Name is "valueModifier" or "preExposeValueModifier" or "valueStage"
                        or "isExposing" or "usePreExposeValue" or "actualDisplay" or "publicDisplay") continue;
                    Add(feature,field.Name.TrimStart('<').Replace(">k__BackingField",""),value);
                }
                // Native event features encode the temporary market price.
                if(!eventFeature || !marketPrice) features.Add(string.Join("\n",feature));
            }
            features.Sort(StringComparer.Ordinal);
            for(int i=0;i<features.Count;i++)
                foreach(string field in features[i].Split('\n')) lines.Add("Feature "+(i+1)+"."+field);
            for(int i=r.ReadInt32();i>0;i--)
            {
                var type=Text(r);
                string locks=(r.ReadBoolean()?" insert-locked":"")+(r.ReadBoolean()?" remove-locked":"");
                string shape=type==typeof(GameGridScrollableInventory).FullName ? r.ReadInt32()+"x"+r.ReadInt32()
                    : type==typeof(GameSlotInventory).FullName ? "" : Shape(r);
                Add(lines,"Inventory",type+" "+shape+locks);
            }
            if(stream.Position!=stream.Length) return null;
            lines.Sort(StringComparer.Ordinal);
            return Version+string.Join("\n",lines);
        }
        catch(Exception e) when(e is FormatException or IOException or InvalidDataException or OverflowException or ArgumentException)
        { return null; }
    }

    internal static string Describe(string sample)
    {
        var key=Normalize(sample);
        if(key==null) return UiText.Get("ui_match_recapture");
        return string.Join("\n",key.Substring(Version.Length).Split('\n').Select(line=>{
            int split=line.IndexOf('\t');
            if(split<0) return line;
            string field=line.Substring(0,split), value=line.Substring(split+1);
            string label=field.StartsWith("#",StringComparison.Ordinal)?NativeWidgets.TagName(field.Substring(1)):
                field.StartsWith("Feature ",StringComparison.Ordinal)?FeatureLabel(field):
                UiText.Get("ui_exact_"+field.ToLowerInvariant().Replace(' ','_'));
            return label+" = "+value;
        }));
    }
    private static string FeatureLabel(string field)
    {
        int dot=field.IndexOf('.');
        if(dot<0) return field;
        return UiText.Get("ui_exact_feature")+field.Substring(7,dot-7)+" · "+
            UiText.Get("ui_feature_"+field.Substring(dot+1).ToLowerInvariant());
    }
    private static string Text(BinaryReader r)=>r.ReadBoolean()?r.ReadString():"";
    private static void Add(List<string> lines,string key,string value)
    {
        if(value.Length==0) return;
        static string Escape(string s)=>s.Replace("\\","\\\\").Replace("\t","\\t").Replace("\n","\\n").Replace("\r","\\r");
        lines.Add(Escape(key)+"\t"+Escape(value));
    }
    private static string Shape(BinaryReader r)
    {
        int w=r.ReadInt32(), h=r.ReadInt32();
        if(w<0 || h<0 || w>r.BaseStream.Length || h>r.BaseStream.Length || (long)w*h>r.BaseStream.Length-r.BaseStream.Position) throw new InvalidDataException();
        var cells=r.ReadBytes(checked(w*h));
        string rows=string.Join("/",Enumerable.Range(0,h).Select(y=>
            string.Join(",",cells.Skip(y*w).Take(w).Select(cell=>cell.ToString(CultureInfo.InvariantCulture)))));
        return w+"×"+h+" "+rows;
    }
    private static void Tags(BinaryReader r,List<string>? lines)
    {
        for(int i=r.ReadInt32();i>0;i--)
        {
            string key=Text(r);
            Text(r); Text(r); // Raw identifier and display metadata
            bool enabled=r.ReadBoolean();
            int n=r.ReadInt32(); long l=r.ReadInt64();
            float f=r.ReadSingle(); double d=r.ReadDouble();
            bool b=r.ReadBoolean(); string s=Text(r);
            if(lines==null || !enabled || key is "BOUGHT_PRICE_TAG" or "LIQUID_CONTAINER_BASE_PRICE_TAG"
                or "AMMO_BOX_CONTENT_UNIT_VALUE_TAG" or "APPRAISAL_DEFAULT_VALUE" or "APPRAISAL_CLIENT_VALUE"
                or "PINNED_TAG" or "IS_OWNED_TAG" or "PARENT_ITEM_ID" or "CONTENT_TABS_STRING") continue;
            var values=new List<string>();
            if(n!=0) Add(values,"int",n.ToString(CultureInfo.InvariantCulture));
            if(l!=0) Add(values,"long",l.ToString(CultureInfo.InvariantCulture));
            if(f!=0) Add(values,"float",f.ToString("R",CultureInfo.InvariantCulture));
            if(d!=0) Add(values,"double",d.ToString("R",CultureInfo.InvariantCulture));
            if(b) values.Add("true");
            Add(values,"text",s);
            if(values.Count==0 && key.StartsWith("CURRENT_PART_",StringComparison.Ordinal)) continue;
            Add(lines,"#"+key,values.Count==0?"on":string.Join(", ",values).Replace("\t","="));
        }
    }
    private static string Value(BinaryReader r)
    {
        string type=Text(r);
        switch(type)
        {
            case "": return "";
            case "System.String": return r.ReadString();
            case "System.Boolean": return r.ReadBoolean()?"true":"";
            case "System.Int32": return r.ReadInt32().ToString(CultureInfo.InvariantCulture);
            case "System.Single": return r.ReadSingle().ToString("R",CultureInfo.InvariantCulture);
            case "System.Double": return r.ReadDouble().ToString("R",CultureInfo.InvariantCulture);
            case "ItemCondition":
                var fields=new List<string>();
                foreach(var field in ItemStackState.ConditionFields)
                {
                    string value=Value(r);
                    if(field.Name is "identifier" or "category" or "customValue" or "onActivateActionId" or "compareValue" or "isTransformative" or "hiddenAsPublic")
                        Add(fields,field.Name,value);
                }
                return string.Join(", ",fields).Replace("\t","=");
            default: return r.ReadInt64().ToString(CultureInfo.InvariantCulture); // Long or native enum
        }
    }
}
