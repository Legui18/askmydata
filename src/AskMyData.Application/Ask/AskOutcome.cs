using AskMyData.Application.Charts;
using AskMyData.Application.Execution;
using AskMyData.Domain.Results;

namespace AskMyData.Application.Ask;

/// <summary>Usage_Summary of one request: tokens over all ILlmClient calls and elapsed time (R7.10).</summary>
public sealed record UsageSummary(int PromptTokens, int CompletionTokens, decimal EstimatedCostUsd, long ElapsedMs)
{
    /// <summary>Zero usage, for outcomes that never reached the LLM.</summary>
    public static UsageSummary Zero { get; } = new(0, 0, 0m, 0);

    /// <summary>Prompt plus completion tokens.</summary>
    public int TotalTokens => PromptTokens + CompletionTokens;
}

/// <summary>
/// Closed hierarchy of Orchestrator outcomes. The constructor is <c>private protected</c>, so only types in this
/// assembly can derive from it.
/// </summary>
public abstract record AskOutcome
{
    private protected AskOutcome(string conversationId, UsageSummary usage)
    {
        ConversationId = conversationId;
        Usage = usage;
    }

    public string ConversationId { get; }

    public UsageSummary Usage { get; }
}

public sealed record AskSucceeded(
    string ConversationId,
    UsageSummary Usage,
    string Sql,
    QueryResult Result,
    ChartSuggestion Chart,
    string Explanation,
    IReadOnlyList<string> MetricUsage,
    IReadOnlyList<string> Warnings) : AskOutcome(ConversationId, Usage);

public sealed record AskValidationFailed(
    string ConversationId,
    UsageSummary Usage,
    string ReasonCode,
    string? Detail,
    string AttemptedSql) : AskOutcome(ConversationId, Usage);

public sealed record AskRefused(string ConversationId, UsageSummary Usage, string RefusalText)
    : AskOutcome(ConversationId, Usage);

public sealed record AskGenerationInvalid(string ConversationId, UsageSummary Usage)
    : AskOutcome(ConversationId, Usage);

public sealed record AskLlmUnavailable(string ConversationId, UsageSummary Usage, bool TimedOut)
    : AskOutcome(ConversationId, Usage);

public sealed record AskExecutionFailed(string ConversationId, UsageSummary Usage, ExecutionErrorCategory Category)
    : AskOutcome(ConversationId, Usage);

public sealed record AskDeadlineExceeded(string ConversationId, UsageSummary Usage)
    : AskOutcome(ConversationId, Usage);

public sealed record AskConversationNotFound(string RequestedId)
    : AskOutcome(RequestedId, UsageSummary.Zero);

public sealed record AskClientAborted(string ConversationId, UsageSummary Usage)
    : AskOutcome(ConversationId, Usage);
