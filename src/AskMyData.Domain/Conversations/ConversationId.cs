namespace AskMyData.Domain.Conversations;

/// <summary>
/// Identifier of a Conversation: exactly 32 lowercase hexadecimal characters (GUID "N" format).
/// </summary>
/// <param name="Value">The 32-character lowercase hex value.</param>
public readonly record struct ConversationId(string Value)
{
    private const int Length = 32;

    /// <summary>Creates a new random Conversation_Id.</summary>
    public static ConversationId New() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Parses <paramref name="raw"/> as a Conversation_Id. Succeeds only for exactly
    /// 32 lowercase hexadecimal characters; no trimming, hyphens, braces or uppercase.
    /// </summary>
    public static bool TryParse(string? raw, out ConversationId id)
    {
        id = default;
        if (raw is null || raw.Length != Length)
        {
            return false;
        }

        foreach (var c in raw)
        {
            if (!char.IsAsciiHexDigitLower(c)) // 0-9, a-f
            {
                return false;
            }
        }

        id = new ConversationId(raw);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
