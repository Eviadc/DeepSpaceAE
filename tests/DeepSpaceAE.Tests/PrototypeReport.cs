using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using DeepSpaceAE.Core.Network;
using DeepSpaceAE.Tests.Simulation;

namespace DeepSpaceAE.Tests;

// Offline visualization of the SAME C# read model used by CLI/tests. No second JS network engine.
internal static class PrototypeReport
{
    public static void Write(NetworkPrototype prototype, TextWriter output)
    {
        var view = prototype.Read();
        var assets = Path.Combine(AppContext.BaseDirectory, "assets", "placeholders");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "parts.json")));
        var icons = manifest.RootElement.GetProperty("parts").EnumerateArray().ToDictionary(
            part => Enum.Parse<NetworkPart>(part.GetProperty("part").GetString()!),
            part => File.ReadAllText(Path.Combine(assets, part.GetProperty("icon").GetString()!)));
        static string H(string value) => WebUtility.HtmlEncode(value);
        output.Write("""
<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>DeepSpaceAE · 存储网络原型</title><style>
:root{color-scheme:dark}*{box-sizing:border-box}body{margin:0;background:#10191f;color:#dbe7ee;font:15px/1.6 system-ui,sans-serif}
main{max-width:1060px;margin:auto;padding:42px 24px}h1{font-size:32px;margin:8px 0}h2{font-size:19px;margin-top:32px}
.label{color:#f8b65b;letter-spacing:.15em;font-size:12px}p,.muted{color:#96abb8}.nodes{display:grid;grid-template-columns:repeat(auto-fit,minmax(155px,1fr));gap:12px}
.node{padding:18px;border:1px solid #344650;background:#17242c;border-radius:8px}.node svg{display:block;margin-bottom:10px}.name{font-weight:650}
.status{color:#76e3ac;font-size:13px}.warning{color:#f8b65b}table{border-collapse:collapse;width:100%;background:#17242c}td,th{text-align:left;border-bottom:1px solid #344650;padding:12px}
th{color:#96abb8;font-size:12px;font-weight:500}code{font:12px ui-monospace,monospace}.scroll{overflow-x:auto}.links{color:#96abb8;display:flex;gap:18px;flex-wrap:wrap;margin-top:14px}
</style><main><div class="label">DEEPSPACEAE / IN-MEMORY PROTOTYPE</div><h1>深空网络 · 存储终端</h1>
<p>离线观测快照 · 未连接真实游戏。操作请使用命令行终端；本页不会执行取放。</p>
""");
        var settings = prototype.Topology.Channels;
        output.Write($"<p>频道：{(settings.Enabled ? "开启" : "关闭容量限制")} · 核心 {settings.CoreCapacity} / 管线 {settings.PipelineCapacity} · 终端 {view.Connection.Status}</p><section class=\"nodes\">");
        var index = 0;
        foreach (var node in prototype.Topology.Nodes.Values.OrderBy(n => n.Part).ThenBy(n => n.Id, StringComparer.Ordinal))
        {
            var id = "icon-" + index++;
            var svg = icons[node.Part].Replace("id=\"title\"", $"id=\"{id}\"").Replace("aria-labelledby=\"title\"", $"aria-labelledby=\"{id}\"");
            var status = node.ConsumesChannel ? view.Network.Devices[node.Id].Status.ToString()
                : $"频道用量 {view.Network.ChannelsUsed[node.Id]}";
            output.Write($"<article class=\"node\" data-part=\"{node.Part}\">{svg}<div class=\"name\">{H(node.Id)}</div><div class=\"muted\">{node.Part}</div><div class=\"status\">{H(status)}</div>");
            if (node.ContainerId != null) output.Write($"<div class=\"muted\">→ {H(node.ContainerId)}</div>");
            output.Write("</article>");
        }
        output.Write("</section><div class=\"links\">");
        foreach (var link in prototype.Topology.Links) output.Write($"<code>{H(link.A)} ↔ {H(link.B)}</code>");
        output.Write($"</div><h2>接入物品 <span class=\"muted\">/ {view.Index.ByInstance.Count} 个独立实例</span></h2><div class=\"scroll\"><table><thead><tr><th>物品 ID</th><th>状态</th><th>容器</th><th>实例 ID</th></tr></thead><tbody>");
        foreach (var location in view.Index.ByInstance.Values.OrderBy(v => v.Item.InstanceId))
            output.Write($"<tr><td>{H(location.Item.ItemId)}</td><td>{H(location.Item.StateTag)}</td><td>{H(location.ContainerId)}</td><td><code>{location.Item.InstanceId:N}</code></td></tr>");
        output.Write("</tbody></table></div>");
        foreach (var missing in view.MissingContainerIds) output.Write($"<p class=\"warning\">容器观测缺失：{H(missing)}</p>");
        output.Write("<p>同物品 ID 不同状态不合并；断线或缺少频道的总线不显示物品。模型测试不证明真实游戏安全。</p></main></html>");
    }

    public static void Export(NetworkPrototype prototype, string path)
    {
        // Do not overwrite existing reports or user files silently.
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var output = new StreamWriter(stream);
        Write(prototype, output);
    }
}
