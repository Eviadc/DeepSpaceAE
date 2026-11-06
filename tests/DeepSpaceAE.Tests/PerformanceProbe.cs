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
            var itemTags = new[] { "METAL", "POWER" };
            var policies = Enumerable.Range(0,31).Select(i => new StoragePolicy(i, StorageAccess.ReadWrite, i % 2 == 0,
                true, true, 0, new[] { "excluded-" + i, "POWER" })).ToArray();
            var filter = new ExtractionFilter(policies);
            var filtered = Measure(() => {
                int allowed = 0;
                for (int i = 0; i < count; i++) if (filter.Allows("item", itemTags)) allowed++;
                if (allowed != count) throw new InvalidOperationException("Filter probe failed.");
            });
            var records = items.Select((item,i) => new CatalogRecord(item.InstanceId,"battery","Cell " + i,itemTags,i+1)).ToArray();
            var catalog = Measure(() => {
                var search = new CatalogSearch(records);
                var found = search.Find("cell #power", "METAL", CatalogSort.Quantity);
                if (found.Count != count || !ReferenceEquals(found, search.Find("cell #power","METAL",CatalogSort.Quantity)))
                    throw new InvalidOperationException("Search cache probe failed.");
            });
            var footprints = items.Select(item => new DisplayFootprint(item.InstanceId, new[] {
                new GridCell(0,0), new GridCell(1,0), new GridCell(0,1), new GridCell(1,1), new GridCell(0,2), new GridCell(1,2) })).ToArray();
            var virtualPacking = Measure(() => {
                var layout = new VirtualInventoryLayout(footprints);
                if (layout.PageCount < 1 || layout.At(0,0,0) != items[0].InstanceId) throw new InvalidOperationException("Projection probe failed.");
            });
            foreach (bool limited in new[] { true, false })
            {
                NetworkEvaluation evaluation = null!;
                var network = Measure(() => evaluation = new NetworkEvaluation(PhysicalNetworkProjection.Build(nodes, edges, new ChannelSettings(limited))));
                results.Add(new { parts = count * 2 + 1, items = count, channels = limited, spatial, index, filtered, catalog, virtualPacking, network,
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
