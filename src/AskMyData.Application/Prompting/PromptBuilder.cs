using System.Text.Json;
using AskMyData.Application.Llm;
using AskMyData.Domain.Conversations;
using AskMyData.Domain.Semantic;

namespace AskMyData.Application.Prompting;

/// <summary>
/// Builds <see cref="LlmRequest"/>s: a System_Prompt of trusted text and an Untrusted_Section delimited by
/// per-request nonce markers (R2.1, R8.1, R8.2, R9.1, R10.1, KD12).
/// </summary>
public sealed class PromptBuilder(SemanticLayer semanticLayer, INonceSource nonces)
{
    private const int MaxHistoryTurns = 10;
    private const int MaxNonceAttempts = 1000;

    /// <summary>Rules portion of the System_Prompt, used by the response leak guard.</summary>
    public string SystemPromptInstructionText => SystemPromptTemplate.Rules;

    public LlmRequest Build(string question, IReadOnlyList<Turn> history, string? failingSql, RepairInfo? repair)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(history);

        var recent = history
            .OrderBy(t => t.Sequence)
            .TakeLast(MaxHistoryTurns)
            .Select(t => new HistoryItem(t.Question, t.Sql))
            .ToArray();
        var isRepair = repair is not null;
        var failing = isRepair ? failingSql : null;

        var untrustedTexts = new List<string> { question };
        foreach (var item in recent)
        {
            untrustedTexts.Add(item.Question);
            untrustedTexts.Add(item.Sql);
        }

        if (failing is not null)
        {
            untrustedTexts.Add(failing);
        }

        var nonce = DrawNonce(untrustedTexts);
        var payload = JsonSerializer.Serialize(new Payload(question, recent, failing));
        var section = $"<<<UNTRUSTED_{nonce}>>>\n{payload}\n<<<END_UNTRUSTED_{nonce}>>>";

        return new LlmRequest(
            SystemPromptTemplate.Render(semanticLayer, repair),
            section,
            isRepair ? LlmCallType.Repair : LlmCallType.Initial,
            repair);
    }

    private string DrawNonce(List<string> untrustedTexts)
    {
        for (var i = 0; i < MaxNonceAttempts; i++)
        {
            var nonce = nonces.Next();
            if (!string.IsNullOrEmpty(nonce)
                && !untrustedTexts.Any(t => t.Contains(nonce, StringComparison.Ordinal)))
            {
                return nonce;
            }
        }

        throw new InvalidOperationException("Unable to draw a nonce that does not occur in the untrusted text.");
    }

    private sealed record HistoryItem(
        [property: System.Text.Json.Serialization.JsonPropertyName("question")] string Question,
        [property: System.Text.Json.Serialization.JsonPropertyName("sql")] string Sql);

    private sealed record Payload(
        [property: System.Text.Json.Serialization.JsonPropertyName("question")] string Question,
        [property: System.Text.Json.Serialization.JsonPropertyName("history")] HistoryItem[] History,
        [property: System.Text.Json.Serialization.JsonPropertyName("failingSql"),
                   System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        string? FailingSql);
}
