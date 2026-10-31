namespace AskMyData.Domain.Conversations;

/// <summary>
/// One answered question within a Conversation.
/// </summary>
/// <param name="Sequence">Monotonic position of the Turn within its Conversation.</param>
/// <param name="Question">The user's natural-language question.</param>
/// <param name="Sql">The validated SQL that answered the question.</param>
/// <param name="Explanation">The plain-language explanation returned to the user.</param>
/// <param name="At">When the Turn was recorded.</param>
public sealed record Turn(long Sequence, string Question, string Sql, string Explanation, DateTimeOffset At);
