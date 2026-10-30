using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceAE.Tests.Simulation;

// TEST-ONLY request replay cache. Session-scoped, unbounded, nonpersistent, single-threaded.
// Intentionally no production transfer service / game inventory interface yet.
internal sealed class InMemoryMoveSession
{
    private sealed class Completed
    {
        public InMemoryContainer Source { get; }
        public InMemoryContainer Destination { get; }
        public Guid[] Items { get; }
        public IReadOnlyList<MoveOutcome> Outcomes { get; }

        public Completed(InMemoryContainer source, InMemoryContainer destination, Guid[] items, MoveOutcome[] outcomes)
        {
            Source = source;
            Destination = destination;
            Items = items;
            Outcomes = Array.AsReadOnly(outcomes);
        }
    }

    private readonly Dictionary<Guid, Completed> completed = new();

    public IReadOnlyList<MoveOutcome> Execute(Guid requestId, InMemoryContainer source,
        InMemoryContainer destination, params Guid[] instanceIds)
    {
        if (requestId == Guid.Empty) throw new ArgumentException("Request ID must be nonempty.", nameof(requestId));
        if (completed.TryGetValue(requestId, out var previous))
        {
            if (!ReferenceEquals(previous.Source, source) || !ReferenceEquals(previous.Destination, destination)
                || !previous.Items.SequenceEqual(instanceIds))
                throw new InvalidOperationException("Request ID reused with different payload.");
            return previous.Outcomes;
        }

        var copy = instanceIds.ToArray();
        var outcomes = copy.Select(id => source.MoveOneTo(destination, id)).ToArray();
        var result = new Completed(source, destination, copy, outcomes);
        completed.Add(requestId, result);
        return result.Outcomes;
    }
}
