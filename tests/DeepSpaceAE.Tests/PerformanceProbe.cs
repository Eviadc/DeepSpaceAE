using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using DeepSpaceAE.Core;
using DeepSpaceAE.Core.Network;

namespace DeepSpaceAE.Tests;

internal static class PerformanceProbe
{
    internal static int Run()
    {
        var results = new List<object>();
        foreach (int count in new[] { 100, 1000, 5000, 10000 })
        {
            var (nodes, edges) = NetworkEvaluationParityTests.LongNetwork(count);
            var items = Enumerable.Range(0, count).Select(_ => new ItemInstance(Guid.NewGuid(), "item", "opaque-state")).ToArray();
            var snapshots = new[] { new ContainerSnapshot("box", items) };
            var spatial = Measure(() => {
                var grid = new OccupiedGrid<object>();
                var token = new object();
                for (int x = 0; x < count; x++) grid.Add(new GridCell(x, 0), token);
                int checksum = 0;
                for (int x = 0; x < count; x++) checksum += grid.ConnectionMask(new GridCell(x, 0), GridSides.Step, (_, _) => true);
                if (checksum == 0) throw new InvalidOperationException("Spatial probe failed.");
            });
            var index = Measure(() => {
                var observation = new SnapshotIndex(snapshots);
                if (!observation.ByInstance.ContainsKey(items[count - 1].InstanceId)) throw new InvalidOperationException("Index probe failed.");
            });
            foreach (bool limited in new[] { true, false })
            {
                NetworkEvaluation evaluation = null!;
                var network = Measure(() => evaluation = new NetworkEvaluation(PhysicalNetworkProjection.Build(nodes, edges, new ChannelSettings(limited))));
                results.Add(new { parts = count * 2 + 1, items = count, channels = limited, spatial, index, network,
                    searches = evaluation.SearchPasses, transportVisits = evaluation.VisitedTransportNodes,
                    online = evaluation.Devices.Values.Count(device => device.Status == DeviceStatus.Online) });
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { scope = "Core model only; not Unity or game FPS", samples = 5, results }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static object Measure(Action action)
    {
        action(); // JIT/warmup excluded.
        var times = new double[5];
        var allocations = new long[5];
        for (int i = 0; i < times.Length; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            action();
            timer.Stop();
            times[i] = timer.Elapsed.TotalMilliseconds;
            allocations[i] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Array.Sort(times);
        Array.Sort(allocations);
        return new { medianMs = Math.Round(times[2], 3), allocatedBytes = allocations[2] };
    }
}
