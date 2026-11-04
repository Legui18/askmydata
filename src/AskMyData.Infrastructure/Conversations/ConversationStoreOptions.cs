namespace AskMyData.Infrastructure.Conversations;

/// <summary>Settings for the in-memory Conversation store (R12.3).</summary>
public sealed class ConversationStoreOptions
{
    /// <summary>Idle time after which a Conversation expires. Default 60 minutes.</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>How often the sweeper removes expired Conversations. Default 1 minute.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);
}
