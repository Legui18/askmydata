using AskMyData.Domain.Conversations;

namespace AskMyData.Application.Conversations;

/// <summary>Port for creating Conversations, loading recent Turns and appending a Turn (R12.1).</summary>
public interface IConversationStore
{
    /// <summary>Creates a new Conversation and returns its server-generated Conversation_Id.</summary>
    Task<ConversationId> CreateAsync(CancellationToken ct);

    /// <summary>
    /// Returns the most recent <paramref name="count"/> Turns in chronological order, or <c>null</c> when the
    /// Conversation is unknown or expired (R12.4, R12.6). Resets the idle time on success.
    /// </summary>
    Task<IReadOnlyList<Turn>?> LoadRecentTurnsAsync(ConversationId id, int count, CancellationToken ct);

    /// <summary>
    /// Appends a Turn. Returns <c>false</c> when the Conversation is unknown or expired. Resets the idle time
    /// on success.
    /// </summary>
    Task<bool> AppendTurnAsync(ConversationId id, string question, string sql, string explanation, CancellationToken ct);
}
