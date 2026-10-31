using AskMyData.Application.Execution;
using AskMyData.Domain.Results;

namespace AskMyData.Application.Charts;

/// <summary>Chart suggestion for a result: type, X column and Y (measure) columns.</summary>
public sealed record ChartSuggestion(ChartType Type, string? XColumn, IReadOnlyList<string> YColumns);

/// <summary>Pure, rule-based chart suggester. No LLM involvement (R9.7, R9.8).</summary>
public sealed class ChartSuggester
{
    private static readonly HashSet<string> TemporalTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "year", "quarter", "month", "week", "day", "date", "period",
    };

    private static readonly ChartSuggestion TableOnly = new(ChartType.TableOnly, null, Array.Empty<string>());

    public ChartSuggestion Suggest(QueryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var temporal = result.Columns.Where(IsTemporal).ToList();
        var measures = result.Columns
            .Where(c => c.Kind == ColumnKind.Numeric && !IsTemporal(c))
            .Select(c => c.Name)
            .ToList();

        if (result.Rows.Count < 2 || measures.Count == 0)
        {
            return TableOnly;
        }

        if (temporal.Count > 0)
        {
            return new ChartSuggestion(ChartType.Line, temporal[0].Name, measures);
        }

        var text = result.Columns.FirstOrDefault(c => c.Kind == ColumnKind.Text);
        if (text is not null)
        {
            return new ChartSuggestion(ChartType.Bar, text.Name, measures);
        }

        return TableOnly;
    }

    private static bool IsTemporal(ResultColumn column) =>
        column.Kind == ColumnKind.Temporal
        || (column.Kind == ColumnKind.Numeric && Tokenize(column.Name).Any(TemporalTokens.Contains));

    /// <summary>Splits on case changes, underscores, spaces and other non-alphanumerics.</summary>
    internal static IEnumerable<string> Tokenize(string name)
    {
        var start = -1;
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (!char.IsLetterOrDigit(ch))
            {
                if (start >= 0)
                {
                    yield return name[start..i];
                    start = -1;
                }

                continue;
            }

            if (start < 0)
            {
                start = i;
                continue;
            }

            var prev = name[i - 1];
            var lowerToUpper = char.IsLower(prev) && char.IsUpper(ch);
            var acronymEnd = char.IsUpper(prev) && char.IsUpper(ch)
                && i + 1 < name.Length && char.IsLower(name[i + 1]);
            if (lowerToUpper || acronymEnd)
            {
                yield return name[start..i];
                start = i;
            }
        }

        if (start >= 0)
        {
            yield return name[start..];
        }
    }
}
