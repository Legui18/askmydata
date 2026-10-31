using System.Text.Json;

namespace AskMyData.Application.Llm;

/// <summary>The parsed, schema-valid form of an LLM Structured_Response.</summary>
public sealed record StructuredResponse(
    string Sql,
    IReadOnlyList<string> MetricUsage,
    string Explanation,
    bool AdHocCalculation,
    string? Refusal);

/// <summary>
/// Strictly parses raw LLM output into a <see cref="StructuredResponse"/>. Pure and deterministic.
/// </summary>
public sealed class StructuredResponseParser
{
    private const string Fence = "```";

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

    /// <summary>Returns null when the output is not a valid Structured_Response.</summary>
    public StructuredResponse? TryParse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        var json = StripSingleFence(rawText.Trim());
        if (json is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json, DocumentOptions);
            return Read(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static StructuredResponse? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? sql = null;
        string? explanation = null;
        bool? adHoc = null;
        List<string>? metricUsage = null;
        string? refusal = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in root.EnumerateObject())
        {
            // Duplicate names are ambiguous, so they are invalid.
            if (!seen.Add(property.Name))
            {
                return null;
            }

            var value = property.Value;
            switch (property.Name)
            {
                case "sql":
                    if (value.ValueKind != JsonValueKind.String) return null;
                    sql = value.GetString();
                    break;
                case "explanation":
                    if (value.ValueKind != JsonValueKind.String) return null;
                    explanation = value.GetString();
                    break;
                case "adHocCalculation":
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
                    adHoc = value.GetBoolean();
                    break;
                case "metricUsage":
                    if (value.ValueKind != JsonValueKind.Array) return null;
                    metricUsage = new List<string>();
                    foreach (var item in value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String) return null;
                        metricUsage.Add(item.GetString()!);
                    }
                    break;
                case "refusal":
                    if (value.ValueKind == JsonValueKind.Null) break;
                    if (value.ValueKind != JsonValueKind.String) return null;
                    refusal = value.GetString();
                    break;
                default:
                    return null; // unknown property
            }
        }

        if (sql is null || explanation is null || adHoc is null || metricUsage is null)
        {
            return null;
        }

        var hasRefusal = !string.IsNullOrEmpty(refusal);
        if (!hasRefusal && sql.Length == 0)
        {
            return null;
        }

        // A non-empty refusal always wins (R2.13); callers check Refusal first.
        return new StructuredResponse(sql, metricUsage, explanation, adHoc.Value, refusal);
    }

    /// <summary>
    /// Removes one surrounding Markdown code fence (optionally with a language tag).
    /// Returns the input unchanged when unfenced, or null when the fence is malformed.
    /// </summary>
    private static string? StripSingleFence(string text)
    {
        if (!text.StartsWith(Fence, StringComparison.Ordinal))
        {
            return text;
        }

        if (text.Length < Fence.Length * 2 || !text.EndsWith(Fence, StringComparison.Ordinal))
        {
            return null;
        }

        var inner = text[Fence.Length..^Fence.Length];
        var firstNewline = inner.IndexOf('\n');
        if (firstNewline < 0)
        {
            return null;
        }

        var infoString = inner[..firstNewline].Trim();
        if (infoString.Length > 0 && !infoString.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
        {
            return null;
        }

        var body = inner[(firstNewline + 1)..].Trim();

        // Only a single fence is allowed; nested fences make the JSON invalid anyway.
        return body.Contains(Fence, StringComparison.Ordinal) ? null : body;
    }
}
