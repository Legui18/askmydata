using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AskMyData.Application.Llm;
using Microsoft.Extensions.Options;

namespace AskMyData.Infrastructure.Llm;

/// <summary>
/// <see cref="ILlmClient"/> adapter for the Gemini <c>generateContent</c> API with schema-constrained JSON
/// output (R11.3, R11.4). The API key travels only in the <c>x-goog-api-key</c> header and is never logged
/// or included in exception messages.
/// </summary>
public sealed class GeminiLlmClient(HttpClient http, IOptions<LlmOptions> options) : ILlmClient
{
    public const string Name = "Gemini";

    private const string ApiKeyHeader = "x-goog-api-key";

    private static readonly JsonNode ResponseSchema = JsonNode.Parse(
        """
        {
          "type": "OBJECT",
          "properties": {
            "sql": { "type": "STRING" },
            "metricUsage": { "type": "ARRAY", "items": { "type": "STRING" } },
            "explanation": { "type": "STRING" },
            "adHocCalculation": { "type": "BOOLEAN" },
            "refusal": { "type": "STRING", "nullable": true }
          },
          "required": ["sql", "metricUsage", "explanation", "adHocCalculation"]
        }
        """)!;

    /// <inheritdoc />
    public string ProviderName => Name;

    /// <inheritdoc />
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var gemini = options.Value.Gemini;

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = request.SystemPrompt }),
            },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = request.UntrustedSection }),
            }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = ResponseSchema.DeepClone(),
            },
        };

        var path = $"v1beta/models/{Uri.EscapeDataString(gemini.Model)}:generateContent";
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(gemini.BaseUrl, path))
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.Add(ApiKeyHeader, gemini.ApiKey);

        try
        {
            using var response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw Unavailable($"Gemini returned HTTP {(int)response.StatusCode}.");
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return Map(document.RootElement, gemini.Model);
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable("Gemini could not be reached.", ex);
        }
        catch (JsonException ex)
        {
            throw Unavailable("Gemini returned an unreadable response.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient's own timeout, not the caller's token.
            throw Unavailable("Gemini request timed out.", ex);
        }
    }

    private static LlmResponse Map(JsonElement root, string configuredModel)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            throw Unavailable("Gemini returned no candidates.");
        }

        var text = new System.Text.StringBuilder();
        if (candidates[0].TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.Object
            && content.TryGetProperty("parts", out var parts)
            && parts.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object
                    && part.TryGetProperty("text", out var t)
                    && t.ValueKind == JsonValueKind.String)
                {
                    text.Append(t.GetString());
                }
            }
        }

        if (text.Length == 0)
        {
            throw Unavailable("Gemini returned an empty candidate.");
        }

        var promptTokens = 0;
        var completionTokens = 0;
        if (root.TryGetProperty("usageMetadata", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            promptTokens = ReadInt(usage, "promptTokenCount");
            completionTokens = ReadInt(usage, "candidatesTokenCount");
        }

        // The configured name is reported so it matches the keys of Llm:Pricing.
        return new LlmResponse(text.ToString(), promptTokens, completionTokens, configuredModel);
    }

    private static int ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n
            : 0;

    private static LlmUnavailableException Unavailable(string message, Exception? inner = null) =>
        new(Name, message, inner);
}
