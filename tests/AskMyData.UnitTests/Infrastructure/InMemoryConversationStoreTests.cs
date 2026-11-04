using AskMyData.Domain.Conversations;
using AskMyData.Infrastructure.Conversations;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace AskMyData.UnitTests.Infrastructure;

public class InMemoryConversationStoreTests
{
    private readonly FakeTimeProvider _time = new();

    private InMemoryConversationStore NewStore() =>
        new(Options.Create(new ConversationStoreOptions()), _time);

    [Fact]
    public async Task Create_returns_valid_id_and_load_returns_empty()
    {
        var store = NewStore();
        var id = await store.CreateAsync(default);

        ConversationId.TryParse(id.Value, out _).Should().BeTrue();
        (await store.LoadRecentTurnsAsync(id, 10, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Load_returns_last_n_turns_in_ascending_sequence()
    {
        var store = NewStore();
        var id = await store.CreateAsync(default);
        for (var i = 1; i <= 5; i++)
        {
            (await store.AppendTurnAsync(id, $"q{i}", $"s{i}", "e", default)).Should().BeTrue();
        }

        var turns = await store.LoadRecentTurnsAsync(id, 3, default);

        turns!.Select(t => t.Question).Should().Equal("q3", "q4", "q5");
        turns!.Select(t => t.Sequence).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Unknown_conversation_returns_null_and_false()
    {
        var store = NewStore();
        var id = ConversationId.New();

        (await store.LoadRecentTurnsAsync(id, 10, default)).Should().BeNull();
        (await store.AppendTurnAsync(id, "q", "s", "e", default)).Should().BeFalse();
    }

    [Fact]
    public async Task Access_resets_idle_time_and_expiry_is_sliding()
    {
        var store = NewStore();
        var id = await store.CreateAsync(default);

        _time.Advance(TimeSpan.FromMinutes(59));
        (await store.LoadRecentTurnsAsync(id, 10, default)).Should().NotBeNull();
        _time.Advance(TimeSpan.FromMinutes(59));
        (await store.AppendTurnAsync(id, "q", "s", "e", default)).Should().BeTrue();

        _time.Advance(TimeSpan.FromMinutes(60));
        (await store.LoadRecentTurnsAsync(id, 10, default)).Should().BeNull();
        (await store.AppendTurnAsync(id, "q", "s", "e", default)).Should().BeFalse();
    }

    [Fact]
    public async Task SweepExpired_removes_only_expired_conversations()
    {
        var store = NewStore();
        var old = await store.CreateAsync(default);
        _time.Advance(TimeSpan.FromMinutes(61));
        var fresh = await store.CreateAsync(default);

        store.SweepExpired().Should().Be(1);

        store.Count.Should().Be(1);
        (await store.LoadRecentTurnsAsync(fresh, 10, default)).Should().NotBeNull();
        (await store.LoadRecentTurnsAsync(old, 10, default)).Should().BeNull();
    }

    [Fact]
    public async Task Concurrent_appends_get_unique_contiguous_sequences()
    {
        var store = NewStore();
        var id = await store.CreateAsync(default);

        await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(i => Task.Run(() => store.AppendTurnAsync(id, $"q{i}", "s", "e", default))));

        var turns = await store.LoadRecentTurnsAsync(id, 100, default);
        turns!.Select(t => t.Sequence).Should().Equal(Enumerable.Range(1, 50).Select(i => (long)i));
    }
}
