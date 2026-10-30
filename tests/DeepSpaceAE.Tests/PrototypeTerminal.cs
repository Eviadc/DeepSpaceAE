using System;
using System.IO;
using System.Linq;
using DeepSpaceAE.Core;
using DeepSpaceAE.Core.Network;
using DeepSpaceAE.Tests.Simulation;

namespace DeepSpaceAE.Tests;

internal static class PrototypeTerminal
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--smoke") return Smoke(Console.Out);
            if ((args.Length == 3 || args.Length == 4) && args[0] == "--audit-game") return GamePatchAudit.Run(args[1], args[2], args.Length == 4 ? args[3] : null);
            if (args.Length == 1 && args[0] == "--benchmark") return PerformanceProbe.Run();
            if (args.Length == 2 && args[0] == "--report")
            {
                PrototypeReport.Export(new NetworkPrototype(), args[1]);
                Console.WriteLine("Exported offline prototype snapshot: " + args[1]);
                return 0;
            }
            if (args.Length != 0) { Console.Error.WriteLine("Usage: demo.sh [--smoke | --benchmark | --report <new-file.html>]"); return 2; }
            return Run(Console.In, Console.Out);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    internal static int Run(TextReader input, TextWriter output)
    {
        var prototype = new NetworkPrototype();
        output.WriteLine("DeepSpaceAE — IN-MEMORY PROTOTYPE / 内存原型，未连接真实游戏");
        output.WriteLine("[CORE] -- [PIPE] -- [TERMINAL] / [BUS-A] / [BUS-B]");
        Execute(prototype, "help", output);
        Execute(prototype, "status", output);
        Execute(prototype, "list", output);
        string? line;
        output.Write("ae> ");
        while ((line = input.ReadLine()) != null)
        {
            if (line.Trim().Equals("quit", StringComparison.OrdinalIgnoreCase)) break;
            try { Execute(prototype, line, output); }
            catch (ArgumentException error) { output.WriteLine("Invalid command: " + error.Message); }
            catch (IOException error) { output.WriteLine("File operation failed: " + error.Message); }
            output.Write("ae> ");
        }
        return 0;
    }

    private static void Execute(NetworkPrototype prototype, string line, TextWriter output)
    {
        var args = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 0) return;
        switch (args[0].ToLowerInvariant())
        {
            case "help" when args.Length == 1:
                output.WriteLine("status | list [filter] | inventory | take <instance-prefix> | put <instance-prefix> <bus-id>");
                output.WriteLine("disconnect <a> <b> | connect <a> <b> | channels <on|off> [pipe-limit] [core-limit] | quit");
                output.WriteLine("export <new-file.html>  (offline visual snapshot; existing files are preserved)");
                break;
            case "status" when args.Length == 1:
                var view = prototype.Read();
                var settings = prototype.Topology.Channels;
                output.WriteLine($"Channels: {(settings.Enabled ? "ON" : "OFF")} (pipe={settings.PipelineCapacity}, core={settings.CoreCapacity})");
                foreach (var usage in view.Network.ChannelsUsed.OrderBy(p => p.Key, StringComparer.Ordinal))
                    output.WriteLine($"  {usage.Key}: used={usage.Value}");
                foreach (var device in view.Network.Devices.OrderBy(p => p.Key, StringComparer.Ordinal))
                    output.WriteLine($"  {device.Key}: {device.Value.Status} via {string.Join(" -> ", device.Value.Route)}");
                foreach (var link in prototype.Topology.Links) output.WriteLine($"  link {link.A} -- {link.B}");
                break;
            case "list" when args.Length <= 2:
                var catalog = prototype.Read();
                output.WriteLine($"Terminal: {catalog.Connection.Status}; visible={catalog.Index.ByInstance.Count}");
                foreach (var row in catalog.Index.ByInstance.Values.OrderBy(v => v.Item.InstanceId))
                    if (args.Length == 1 || (row.Item.ItemId + " " + row.Item.StateTag).Contains(args[1], StringComparison.OrdinalIgnoreCase))
                        WriteItem(output, row.Item, row.ContainerId);
                foreach (var missing in catalog.MissingContainerIds) output.WriteLine("  Missing container: " + missing);
                break;
            case "inventory" when args.Length == 1:
                foreach (var item in prototype.Player.Snapshot().Items) WriteItem(output, item, "player");
                output.WriteLine("Player instances: " + prototype.Player.Count);
                break;
            case "take" when args.Length == 2:
                var id = Select(prototype.Read().Index.ByInstance.Keys.ToArray(), args[1]);
                WriteResult(output, prototype.Withdraw(id));
                break;
            case "put" when args.Length == 3:
                var carried = Select(prototype.Player.Snapshot().Items.Select(item => item.InstanceId).ToArray(), args[1]);
                WriteResult(output, prototype.Deposit(args[2], carried));
                break;
            case "disconnect" when args.Length == 3:
            case "connect" when args.Length == 3:
                prototype.Link(args[1], args[2], args[0].Equals("connect", StringComparison.OrdinalIgnoreCase));
                output.WriteLine("Topology refreshed. Use list/status to inspect current access.");
                break;
            case "channels" when args.Length >= 2 && args.Length <= 4:
                if (args[1] != "on" && args[1] != "off") throw new ArgumentException("channels on|off [pipe-limit] [core-limit]");
                var previous = prototype.Topology.Channels;
                var pipe = args.Length >= 3 ? ParseCapacity(args[2]) : previous.PipelineCapacity;
                var core = args.Length >= 4 ? ParseCapacity(args[3]) : previous.CoreCapacity;
                prototype.SetChannels(new ChannelSettings(args[1] == "on", core, pipe));
                output.WriteLine("Channel allocation refreshed.");
                break;
            case "export" when args.Length == 2:
                PrototypeReport.Export(prototype, args[1]);
                output.WriteLine("Exported offline prototype snapshot: " + args[1]);
                break;
            default: throw new ArgumentException("Use help for supported commands.");
        }
    }

    private static int ParseCapacity(string value) => int.TryParse(value, out var result) && result >= 0
        ? result : throw new ArgumentException("Capacity must be a nonnegative integer.");

    private static Guid Select(Guid[] ids, string prefix)
    {
        var normalized = prefix.Replace("-", "");
        if (normalized.Length < 4) throw new ArgumentException("Use at least four instance ID digits.");
        var matches = ids.Where(id => id.ToString("N").StartsWith(normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new ArgumentException("Instance is unavailable or the prefix is ambiguous; refresh list/inventory.");
        return matches[0];
    }

    private static void WriteItem(TextWriter output, ItemInstance item, string container) =>
        output.WriteLine($"  {item.InstanceId:N}  {item.ItemId}  [{item.StateTag}]  @{container}");

    private static void WriteResult(TextWriter output, TerminalMoveResult result) =>
        output.WriteLine(result.AccessFailure ?? result.Movement?.ToString() ?? "Unknown");

    internal static int Smoke(TextWriter output)
    {
        var prototype = new NetworkPrototype();
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException("SMOKE FAILED: " + reason); }
        void Command(string command) { output.WriteLine("ae> " + command); Execute(prototype, command, output); }
        output.WriteLine("IN-MEMORY ONLY — no game compatibility claim");
        Command("status"); Command("list");
        Check(prototype.Read().Index.ByInstance.Count == 3, "initial bus index");
        Command("take 1111");
        Check(prototype.Player.Contains(NetworkPrototype.ChargedId) && prototype.Read().Index.ByInstance.Count == 2, "withdraw");
        Command("put 1111 bus-b");
        Check(prototype.Storage["locker-b"].Contains(NetworkPrototype.ChargedId) && prototype.Player.Count == 0, "deposit");
        Command("disconnect trunk bus-a"); Command("list");
        Check(!prototype.Read().Index.ByInstance.ContainsKey(NetworkPrototype.EmptyId), "disconnected bus hidden");
        Command("channels on 1"); Command("list");
        Check(prototype.Read().Index.ByInstance.Count == 0, "channel-limited buses hidden");
        Command("channels off"); Command("connect trunk bus-a"); Command("list");
        Check(prototype.Read().Index.ByInstance.Count == 3, "reconnected visibility");
        output.WriteLine("SMOKE PASSED: read, withdraw, deposit, disconnect, channel limit, reconnect");
        return 0;
    }
}
