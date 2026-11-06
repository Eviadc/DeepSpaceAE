using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
namespace DeepSpaceAE.Tests;
public sealed class LocalizationAssetTests
{
    private static Dictionary<string,string> Read(string locale)
    {
        var result = new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory,"assets","game","Localization",locale+".csv")))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#",StringComparison.Ordinal)) continue;
            int comma=line.IndexOf(',');Assert.True(comma>0);
            var key=line.Substring(0,comma);var value=line.Substring(comma+1);
            Assert.Matches("^[a-z][a-z0-9_]*$",key);
            if (value.StartsWith("\"",StringComparison.Ordinal))
            { Assert.EndsWith("\"",value);value=value.Substring(1,value.Length-2).Replace("\"\"","\""); }
            Assert.NotEmpty(value);Assert.True(result.TryAdd(key,value),"Duplicate localization key: "+key);
        }
        return result;
    }
    [Fact]
    public void NativeLocaleFilenamesHaveMatchingStableAsciiKeys()
    {
        var en=Read("en");var zh=Read("zh");Assert.True(en.Count>80);
        Assert.Equal(en.Keys.OrderBy(k=>k,StringComparer.Ordinal),zh.Keys.OrderBy(k=>k,StringComparer.Ordinal));
        Assert.Equal("AE Controller",en["item_core_name"]);Assert.Equal("AE 核心",zh["item_core_name"]);
    }
    [Fact]
    public void TranslationsKeepTheSameFormattingArguments()
    {
        var en=Read("en");var zh=Read("zh");
        foreach (var key in en.Keys)
        {
            string[] Slots(string s)=>Regex.Matches(s,@"\{(\d+)\}").Select(m=>m.Groups[1].Value).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
            Assert.Equal(Slots(en[key]),Slots(zh[key]));
            string.Format(CultureInfo.InvariantCulture,en[key],11,22);
            string.Format(CultureInfo.InvariantCulture,zh[key],11,22);
        }
    }
}
