using AskMyData.Application.Llm;
using AskMyData.Evals.Reporting;
using AskMyData.Infrastructure.Llm;
using Microsoft.Extensions.Options;

namespace AskMyData.Evals.Runner;

/// <summary>Accumulates model name and token usage over every ILlmClient call of a Live_Mode run (R6.14).</summary>
public sealed class LlmUsageRecorder(string? configuredModel = null)
{
    private readonly object _gate = new();
    private long _prompt;
    private long _completion;
    private string? _model = configuredModel;

    public string? Model
    {
        get
        {
            lock (_gate)
            {
                return _model;
            }
        }
    }

    public TokenUsage Usage
    {
        get
        {
            lock (_gate)
            {
                return new TokenUsage(_prompt, _completion, _prompt + _completion);
            }
        }
    }

    internal void Record(LlmResponse response)
    {
        lock (_gate)
        {
            _prompt += response.PromptTokens;
            _completion += response.CompletionTokens;
            if (!string.IsNullOrEmpty(response.ModelName))
            {
                _model = response.ModelName;
            }
        }
    }
}

/// <summary>Decorator that reports each response to a <see cref="LlmUsageRecorder"/>; behavior is otherwise unchanged.</summary>
public sealed class RecordingLlmClient(ILlmClient inner, LlmUsageRecorder recorder) : ILlmClient
{
    public string ProviderName => inner.ProviderName;

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var response = await inner.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
        recorder.Record(response);
        return response;
    }
}

public static class LiveLlmFactory
{
    /// <summary>
    /// Builds <see cref="LlmOptions"/> from environment variables (<c>Llm__Gemini__Model</c>, <c>Llm__Gemini__BaseUrl</c>,
    /// <c>Llm__Gemini__ApiKey</c>, falling back to <c>GEMINI_API_KEY</c>). Returns null when no API key is configured.
    /// </summary>
    public static LlmOptions? LoadOptions(Func<string, string?> getEnv)
    {
        ArgumentNullException.ThrowIfNull(getEnv);
        var apiKey = NonEmpty(getEnv("Llm__Gemini__ApiKey")) ?? NonEmpty(getEnv("GEMINI_API_KEY"));
        if (apiKey is null)
        {
            return null;
        }

        var gemini = new GeminiOptions { ApiKey = apiKey };
        if (NonEmpty(getEnv("Llm__Gemini__Model")) is { } model)
        {
            gemini.Model = model;
        }

        if (NonEmpty(getEnv("Llm__Gemini__BaseUrl")) is { } baseUrl)
        {
            gemini.BaseUrl = new Uri(baseUrl, UriKind.Absolute);
        }

        return new LlmOptions { Gemini = gemini };
    }

    public static GeminiLlmClient CreateGemini(HttpClient http, LlmOptions options) =>
        new(http, Options.Create(options));

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
