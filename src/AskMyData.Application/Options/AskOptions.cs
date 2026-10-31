namespace AskMyData.Application.Options;

/// <summary>Limits applied by the Orchestrator to one Question_Request.</summary>
public sealed class AskOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ask";

    /// <summary>Request_Deadline for the whole Orchestrator run (R2.15). Default 60 s.</summary>
    public TimeSpan RequestDeadline { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>LLM_Timeout for a single ILlmClient call. Default 30 s.</summary>
    public TimeSpan LlmTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>SQL command timeout (R4.2). Default 15 s.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Row_Limit for query results (R4.7). Default 1000.</summary>
    public int RowLimit { get; set; } = 1000;

    /// <summary>Number of most recent Turns supplied as Conversation_History (R8.2). Default 10.</summary>
    public int HistoryTurns { get; set; } = 10;

    /// <summary>Maximum explanation length in characters (R9.6). Default 500.</summary>
    public int MaxExplanationLength { get; set; } = 500;
}
