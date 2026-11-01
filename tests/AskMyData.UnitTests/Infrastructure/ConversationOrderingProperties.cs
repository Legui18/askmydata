using AskMyData.Infrastructure.Conversations;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AskMyData.UnitTests.Infrastructure;

public sealed class ConversationOrderingProperties
{
    // Feature: askmydata-conversational-analytics, Property 23: Conversation store preserves order and concurrent appends
    /// <summary>**Validates: Requirements 8.2, 12.4, 12.5**</summary>
    [Property(MaxTest = 100)]
    public Property Store_preserves_order_and_every_concurrent_append()
    {
        var totals = Gen.Choose(0, 40).ToArbitrary();
        var workers = Gen.Choose(1, 8).ToArbitrary();
        var counts = Gen.Choose(0, 15).ToArbitrary();

        return Prop.ForAll(totals, workers, counts, (total, workerCount, k) =>
        {
            var store = new InMemoryConversationStore(
                Options.Create(new ConversationStoreOptions()), new FakeTimeProvider());
            var id = store.CreateAsync(CancellationToken.None).GetAwaiter().GetResult();

            // Spread `total` uniquely labelled appends across `workerCount` concurrent tasks.
            var results = Task.WhenAll(Enumerable.Range(0, workerCount).Select(w => Task.Run(async () =>
            {
                var ok = true;
                for (var i = w; i < total; i += workerCount)
                {
                    ok &= await store.AppendTurnAsync(id, $"q{i}", $"s{i}", $"e{i}", CancellationToken.None);
                }

                return ok;
            }))).GetAwaiter().GetResult();

            var all = store.LoadRecentTurnsAsync(id, int.MaxValue, CancellationToken.None).GetAwaiter().GetResult()!;
            var recent = store.LoadRecentTurnsAsync(id, k, CancellationToken.None).GetAwaiter().GetResult()!;

            var allAppended = results.All(r => r);
            var noLossOrDuplicates = all.Count == total
                && all.Select(t => t.Question).Distinct().Count() == total;
            var uniqueSequences = all.Select(t => t.Sequence).Distinct().Count() == total;
            var allAscending = all.Zip(all.Skip(1), (a, b) => a.Sequence < b.Sequence).All(x => x);
            var expected = all.Skip(Math.Max(0, total - k)).ToList();
            var recentOk = recent.Count == Math.Min(k, total)
                && recent.Select(t => t.Sequence).SequenceEqual(expected.Select(t => t.Sequence))
                && recent.Zip(recent.Skip(1), (a, b) => a.Sequence < b.Sequence).All(x => x);

            return (allAppended && noLossOrDuplicates && uniqueSequences && allAscending && recentOk)
                .Label($"total={total} workers={workerCount} k={k} stored={all.Count} recent={recent.Count}");
        });
    }
}
