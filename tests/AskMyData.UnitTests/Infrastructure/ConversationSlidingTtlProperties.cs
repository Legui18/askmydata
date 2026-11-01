using AskMyData.Domain.Conversations;
using AskMyData.Infrastructure.Conversations;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AskMyData.UnitTests.Infrastructure;

public sealed class ConversationSlidingTtlProperties
{
    private sealed record Step(int GapSeconds, bool Append, bool SweepFirst);

    // Feature: askmydata-conversational-analytics, Property 24: Sliding TTL expiry
    /// <summary>**Validates: Requirements 8.3, 12.3, 12.6**</summary>
    [Property(MaxTest = 100)]
    public Property Conversation_is_available_exactly_while_idle_time_is_below_ttl()
    {
        var ttls = Gen.Choose(1, 120).ToArbitrary(); // TTL in minutes

        var scenarios = ttls.Generator.SelectMany(ttlMin =>
        {
            var ttlSec = ttlMin * 60;
            // Bias gaps toward the TTL boundary so both sides of expiry are exercised.
            var gap = Gen.OneOf(
                Gen.Constant(0),
                Gen.Constant(ttlSec - 1),
                Gen.Constant(ttlSec),
                Gen.Constant(ttlSec + 1),
                Gen.Choose(0, ttlSec * 2));
            var step = gap.SelectMany(g => Gen.Elements(true, false).SelectMany(a =>
                Gen.Elements(true, false).Select(s => new Step(g, a, s))));
            return Gen.ListOf(step).Select(steps => (ttlMin, steps: steps.Take(30).ToList()));
        }).ToArbitrary();

        return Prop.ForAll(scenarios, scenario =>
        {
            var (ttlMin, steps) = scenario;
            var ttl = TimeSpan.FromMinutes(ttlMin);
            var time = new FakeTimeProvider();
            var store = new InMemoryConversationStore(
                Options.Create(new ConversationStoreOptions { Ttl = ttl }), time);
            var id = store.CreateAsync(CancellationToken.None).GetAwaiter().GetResult();
            var unknown = ConversationId.New();

            var lastAccess = time.GetUtcNow();
            var removed = false;
            var failures = new List<string>();

            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                time.Advance(TimeSpan.FromSeconds(step.GapSeconds));
                var now = time.GetUtcNow();
                var expected = !removed && now - lastAccess < ttl;

                if (step.SweepFirst)
                {
                    var swept = store.SweepExpired();
                    var expectedSwept = !removed && !expected ? 1 : 0;
                    if (swept != expectedSwept)
                    {
                        failures.Add($"step {i}: swept={swept} expected={expectedSwept}");
                    }

                    removed |= !expected;
                }

                bool actual;
                if (step.Append)
                {
                    actual = store.AppendTurnAsync(id, $"q{i}", "SELECT 1", "e", CancellationToken.None)
                        .GetAwaiter().GetResult();
                }
                else
                {
                    actual = store.LoadRecentTurnsAsync(id, 10, CancellationToken.None).GetAwaiter().GetResult() is not null;
                }

                if (actual != expected)
                {
                    failures.Add($"step {i}: gap={step.GapSeconds}s append={step.Append} actual={actual} expected={expected}");
                }

                if (expected)
                {
                    lastAccess = now; // successful access resets idle time
                }
                else
                {
                    removed = true; // expired access evicts the entry
                }

                // Unknown ids are never found.
                store.LoadRecentTurnsAsync(unknown, 10, CancellationToken.None).GetAwaiter().GetResult()
                    .Should().BeNull();
                store.AppendTurnAsync(unknown, "q", "s", "e", CancellationToken.None).GetAwaiter().GetResult()
                    .Should().BeFalse();
            }

            // Once expired, the entry is gone for good and a sweep leaves nothing to remove.
            if (removed)
            {
                store.SweepExpired().Should().Be(0);
                store.LoadRecentTurnsAsync(id, 10, CancellationToken.None).GetAwaiter().GetResult().Should().BeNull();
            }

            return (failures.Count == 0).Label($"ttl={ttlMin}min steps={steps.Count} failures=[{string.Join("; ", failures)}]");
        });
    }

    // Feature: askmydata-conversational-analytics, Property 24: Sliding TTL expiry
    /// <summary>**Validates: Requirements 8.3, 12.3, 12.6**</summary>
    [Property(MaxTest = 100)]
    public Property Idle_gap_beyond_ttl_expires_and_sweep_removes_it()
    {
        var gaps = Gen.Choose(0, 3 * 3600).ToArbitrary();

        return Prop.ForAll(gaps, gapSec =>
        {
            var time = new FakeTimeProvider();
            var store = new InMemoryConversationStore(Options.Create(new ConversationStoreOptions()), time);
            var id = store.CreateAsync(CancellationToken.None).GetAwaiter().GetResult();

            time.Advance(TimeSpan.FromSeconds(gapSec));
            var expired = TimeSpan.FromSeconds(gapSec) >= TimeSpan.FromMinutes(60);
            var swept = store.SweepExpired();
            var load = store.LoadRecentTurnsAsync(id, 5, CancellationToken.None).GetAwaiter().GetResult();
            var append = store.AppendTurnAsync(id, "q", "s", "e", CancellationToken.None).GetAwaiter().GetResult();

            return (swept == (expired ? 1 : 0) && (load is null) == expired && append == !expired)
                .Label($"gap={gapSec}s expired={expired} swept={swept} loadNull={load is null} append={append}");
        });
    }
}
