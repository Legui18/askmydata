using AskMyData.Application.Ask;
using AskMyData.Application.Llm;

namespace AskMyData.Application.Observability;

/// <summary>
/// Sums tokens over every ILlmClient call of one request, the initial call and the Repair_Call (R7.8).
/// Not thread-safe; one instance per request.
/// </summary>
public sealed class UsageAccumulator
{
    private int _promptTokens;
    private int _completionTokens;
    private decimal _cost;

    /// <summary>Adds the tokens of one LLM response and its estimated cost.</summary>
    public void Add(LlmResponse response, decimal cost = 0m)
    {
        ArgumentNullException.ThrowIfNull(response);
        _promptTokens += response.PromptTokens;
        _completionTokens += response.CompletionTokens;
        _cost += cost;
    }

    /// <summary>Returns the usage so far with the given elapsed time.</summary>
    public UsageSummary Snapshot(long elapsedMs) => new(_promptTokens, _completionTokens, _cost, elapsedMs);
}
