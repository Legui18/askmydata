using System.Text.Json;
using AskMyData.Application.Llm;

namespace AskMyData.Infrastructure.Llm;

/// <summary>
/// Scripted <see cref="ILlmClient"/> that needs no network access (R6.6, R11.4). Returns the recorded raw
/// output for the question and call index (0 initial, 1 repair); unscripted questions get a fixed refusal.
/// Token counts are fixed so usage arithmetic is testable.
/// </summary>
public sealed class StubLlmClient(IStubScriptSource scripts) : ILlmClient
{
    public const string Name = "Stub";
    public const string Model = "stub";
    public const int FixedPromptTokens = 100;
    public const int FixedCompletionTokens = 50;

    /// <summary>Raw output returned when no scripted response exists.</summary>
    public const string RefusalRawText =
        """{"sql":"","metricUsage":[],"explanation":"","adHocCalculation":false,"refusal":"No scripted response is available for this question."}""";

    /// <inheritdoc />
    public string ProviderName => Name;

    /// <inheritdoc />
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var raw = RefusalRawText;
        var question = ExtractQuestion(request.UntrustedSection);
        var index = request.CallType == LlmCallType.Repair ? 1 : 0;
        if (question is not null
            && scripts.TryGetScript(question, out var responses)
            && index < responses.Count)
        {
            raw = responses[index];
        }

        return Task.FromResult(new LlmResponse(raw, FixedPromptTokens, FixedCompletionTokens, Model));
    }

    /// <summary>Reads the question from the nonce-delimited JSON payload built by PromptBuilder.</summary>
    private static string? ExtractQuestion(string untrustedSection)
    {
        var start = untrustedSection.IndexOf('{');
        var end = untrustedSection.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(untrustedSection[start..(end + 1)]);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("question", out var q)
                   && q.ValueKind == JsonValueKind.String
                ? q.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
