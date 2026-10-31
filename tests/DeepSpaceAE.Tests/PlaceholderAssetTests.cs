using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using DeepSpaceAE.Core.Network;
using DeepSpaceAE.Core;
using DeepSpaceAE.Tests.Simulation;
using Xunit;

namespace DeepSpaceAE.Tests;

public sealed class PlaceholderAssetTests
{
    [Fact]
    public void VisualReportEmbedsIconsAndUsesCurrentTerminalDataWithEscapedText()
    {
        var prototype = new NetworkPrototype();
        prototype.Withdraw(NetworkPrototype.ChargedId);
        prototype.Deposit("bus-b", NetworkPrototype.ChargedId);
        prototype.Storage["locker-a"].Seed(new ItemInstance(Guid.NewGuid(), "<script>", "unsafe&state"));
        using var output = new StringWriter();
        PrototypeReport.Write(prototype, output);
        var html = output.ToString();
        foreach (var kind in prototype.Topology.Nodes.Values.Select(n=>n.Part).Distinct()) Assert.Contains($"data-part=\"{kind}\"", html);
        Assert.Contains("<td>battery</td><td>charge=100</td><td>locker-b</td>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("unsafe&amp;state", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("not network storage", html);
    }

    [Fact]
    public void AllPartsHaveOriginalStandaloneIconsAndMatchingChannelCosts()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "assets", "placeholders");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "parts.json")));
        var parts = document.RootElement.GetProperty("parts").EnumerateArray().ToArray();
        Assert.Equal(7, parts.Length);
        Assert.Equal(7, parts.Select(p => p.GetProperty("id").GetString()).Distinct().Count());
        Assert.Equal(Enum.GetValues<NetworkPart>().OrderBy(p => p), parts.Select(p => Enum.Parse<NetworkPart>(p.GetProperty("part").GetString()!)).OrderBy(p => p));
        foreach (var part in parts)
        {
            var kind = Enum.Parse<NetworkPart>(part.GetProperty("part").GetString()!);
            var node = new NetworkNode("asset-check", kind, kind == NetworkPart.ItemStorageBus || kind == NetworkPart.DiskDrive ? "box" : null);
            Assert.Equal(node.ConsumesChannel ? 1 : 0, part.GetProperty("channelCost").GetInt32());
            var icon = part.GetProperty("icon").GetString()!;
            Assert.Equal(icon, Path.GetFileName(icon));
            var svg = XDocument.Load(Path.Combine(root, icon)).Root!;
            Assert.Equal("{http://www.w3.org/2000/svg}svg", svg.Name.ToString());
            Assert.Equal("0 0 64 64", svg.Attribute("viewBox")?.Value);
            Assert.NotNull(svg.Element(XName.Get("title", "http://www.w3.org/2000/svg")));
            Assert.DoesNotContain(svg.Descendants(), element => element.Name.LocalName is "script" or "image" or "foreignObject");
        }
    }
}
