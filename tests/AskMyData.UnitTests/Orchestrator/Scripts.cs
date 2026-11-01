using AskMyData.Application.Execution;
using AskMyData.Domain.Results;

namespace AskMyData.UnitTests.Orchestrator;

/// <summary>How a scripted LLM step ends.</summary>
public enum LlmStepKind
{
    /// <summary>Returns <see cref="LlmStep.RawText"/>.</summary>
    Respond,

    /// <summary>Throws <c>LlmUnavailableException</c> after the delay.</summary>
    Unavailable,
}

/// <summary>One scripted ILlmClient call: wait <see cref="Delay"/> on the fake clock, then respond or fail.</summary>
public sealed record LlmStep(
    LlmStepKind Kind,
    string RawText,
    TimeSpan Delay,
    int PromptTokens = 10,
    int CompletionTokens = 5)
{
    public static LlmStep Respond(string rawText, TimeSpan? delay = null) =>
        new(LlmStepKind.Respond, rawText, delay ?? TimeSpan.Zero);

    public static LlmStep Fail(TimeSpan? delay = null) =>
        new(LlmStepKind.Unavailable, string.Empty, delay ?? TimeSpan.Zero);

    public LlmStep WithDelay(TimeSpan delay) => this with { Delay = delay };
}

/// <summary>One scripted IQueryExecutor call: wait <see cref="Delay"/> on the fake clock, then return a result or an error.</summary>
public sealed record ExecutorStep(QueryResult? Result, ExecutionErrorCategory? Error, TimeSpan Delay)
{
    public static ExecutorStep Rows(QueryResult result, TimeSpan? delay = null) =>
        new(result, null, delay ?? TimeSpan.Zero);

    public static ExecutorStep Fail(ExecutionErrorCategory category, TimeSpan? delay = null) =>
        new(null, category, delay ?? TimeSpan.Zero);

    public ExecutorStep WithDelay(TimeSpan delay) => this with { Delay = delay };
}
