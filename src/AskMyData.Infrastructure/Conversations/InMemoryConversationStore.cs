using System.Collections.Concurrent;
using AskMyData.Application.Conversations;
using AskMyData.Domain.Conversations;
using Microsoft.Extensions.Options;

namespace AskMyData.Infrastructure.Conversations;

/// <summary>
/// In-memory <see cref="IConversationStore"/> with a per-entry lock, monotonic sequence numbers and a
/// sliding TTL measured with <see cref="TimeProvider"/>. Expiry is evaluated inside the entry lock on
/// every access, so an expired Conversation is never observable even before the sweeper runs (R12.3, R12.6).
/// </summary>
public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly TimeProvider _time;
    private readonly TimeSpan _ttl;

    public InMemoryConversationStore(IOptions<ConversationStoreOptions> options, TimeProvider time)
    {
        _time = time;
        _ttl = options.Value.Ttl;
    }

    /// <inheritdoc />
    public Task<ConversationId> CreateAsync(CancellationToken ct)
    {
        var id = ConversationId.New();
        _entries[id.Value] = new Entry(_time.GetUtcNow());
        return Task.FromResult(id);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Turn>?> LoadRecentTurnsAsync(ConversationId id, int count, CancellationToken ct)
    {
        if (!_entries.TryGetValue(id.Value, out var entry))
        {
            return Task.FromResult<IReadOnlyList<Turn>?>(null);
        }

        var now = _time.GetUtcNow();
        lock (entry.Gate)
        {
            if (!IsLive(entry, now))
            {
                Remove(id.Value, entry);
                return Task.FromResult<IReadOnlyList<Turn>?>(null);
            }

            entry.LastAccess = now;
            var take = Math.Clamp(count, 0, entry.Turns.Count);
            // Turns are appended with ascending sequence, so the tail is the most recent, in order.
            IReadOnlyList<Turn> result = entry.Turns.GetRange(entry.Turns.Count - take, take);
            return Task.FromResult<IReadOnlyList<Turn>?>(result);
        }
    }

    /// <inheritdoc />
    public Task<bool> AppendTurnAsync(ConversationId id, string question, string sql, string explanation, CancellationToken ct)
    {
        if (!_entries.TryGetValue(id.Value, out var entry))
        {
            return Task.FromResult(false);
        }

        var now = _time.GetUtcNow();
        lock (entry.Gate)
        {
            if (!IsLive(entry, now))
            {
                Remove(id.Value, entry);
                return Task.FromResult(false);
            }

            entry.Turns.Add(new Turn(entry.NextSeq++, question, sql, explanation, now));
            entry.LastAccess = now;
            return Task.FromResult(true);
        }
    }

    /// <summary>Removes every expired Conversation. Called by <see cref="ConversationSweeper"/>.</summary>
    /// <returns>The number of Conversations removed.</returns>
    public int SweepExpired()
    {
        var now = _time.GetUtcNow();
        var removed = 0;
        foreach (var (key, entry) in _entries)
        {
            lock (entry.Gate)
            {
                if (!entry.Removed && !IsLive(entry, now))
                {
                    Remove(key, entry);
                    removed++;
                }
            }
        }

        return removed;
    }

    /// <summary>Number of Conversations currently held (including not-yet-swept expired ones).</summary>
    internal int Count => _entries.Count;

    private bool IsLive(Entry entry, DateTimeOffset now) => !entry.Removed && now - entry.LastAccess < _ttl;

    // Must be called while holding entry.Gate. Removes only this exact entry instance.
    private void Remove(string key, Entry entry)
    {
        entry.Removed = true;
        _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
    }

    private sealed class Entry(DateTimeOffset created)
    {
        public object Gate { get; } = new();
        public List<Turn> Turns { get; } = [];
        public long NextSeq { get; set; } = 1;
        public DateTimeOffset LastAccess { get; set; } = created;
        public bool Removed { get; set; }
    }
}
