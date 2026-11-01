using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using AskMyData.Application.Execution;
using AskMyData.Application.Validation;
using AskMyData.Evals.Cases;

namespace AskMyData.Evals.Runner;

/// <summary>Outcome of one generate-expected run.</summary>
/// <param name="Changed">Case files whose content differs from the regenerated content, with a short description.</param>
/// <param name="Errors">Failures (rejected referenceSql, execution errors, missing referenceSql). Any error fails the run.</param>
/// <param name="Processed">Number of non-adversarial cases regenerated.</param>
/// <param name="Skipped">Number of adversarial cases skipped.</param>
public sealed record ExpectedGenerationResult(
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Errors,
    int Processed,
    int Skipped);

/// <summary>
/// Fills <c>expected.columns</c> and <c>expected.rows</c> of every non-adversarial Eval_Case by executing its
/// <c>referenceSql</c> as the Read_Only_User through <see cref="IQueryExecutor"/> (R6.5, R14.1, R14.6). Each
/// referenceSql is validated with <see cref="ISqlValidator"/> first; a reject is a failure.
/// </summary>
public sealed class ExpectedGenerator(IQueryExecutor executor, ISqlValidator validator)
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Regenerates expected rows. When <paramref name="check"/> is false, changed files are written.</summary>
    public async Task<ExpectedGenerationResult> GenerateAsync(string casesDirectory, bool check, CancellationToken ct)
    {
        if (!Directory.Exists(casesDirectory))
        {
            return new ExpectedGenerationResult([], [$"Eval case directory not found: {casesDirectory}"], 0, 0);
        }

        // Schema-check first so malformed files fail with the loader's message.
        await EvalCaseLoader.LoadDirectoryAsync(casesDirectory, ct);

        var changed = new List<string>();
        var errors = new List<string>();
        var processed = 0;
        var skipped = 0;

        foreach (var file in Directory.EnumerateFiles(casesDirectory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var original = await File.ReadAllTextAsync(file, ct);
            var root = JsonNode.Parse(original)!;
            var caseNodes = root is JsonArray array ? array.OfType<JsonObject>().ToList() : [(JsonObject)root];
            var fileName = Path.GetFileName(file);
            var details = new List<string>();
            var processedBefore = processed;

            foreach (var node in caseNodes)
            {
                var id = node["id"]!.GetValue<string>();
                if (node["category"]!.GetValue<string>() == EvalCategories.Adversarial)
                {
                    skipped++;
                    continue;
                }

                var expected = node["expected"]!.AsObject();
                if (expected["kind"]!.GetValue<string>() != EvalExpectedKinds.Rows)
                {
                    errors.Add($"{fileName}: case '{id}' is not adversarial but does not expect rows.");
                    continue;
                }

                var referenceSql = node["referenceSql"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(referenceSql))
                {
                    errors.Add($"{fileName}: case '{id}' has no referenceSql.");
                    continue;
                }

                processed++;
                var metricUsage = node["metric"] is JsonValue metric ? new[] { metric.GetValue<string>() } : [];
                var verdict = validator.Validate(referenceSql, metricUsage);
                if (!verdict.Passed)
                {
                    errors.Add($"{fileName}: case '{id}' referenceSql rejected by validator ({verdict.ReasonCode}{(verdict.Detail is null ? "" : ": " + verdict.Detail)}).");
                    continue;
                }

                var outcome = await executor.ExecuteAsync(
                    new ValidatedSql(referenceSql), new QueryBudget(CommandTimeout), ct);
                if (outcome.TryGetSecond(out var failure))
                {
                    errors.Add($"{fileName}: case '{id}' referenceSql failed to execute ({failure.Category}).");
                    continue;
                }

                outcome.TryGetFirst(out var result);
                if (result.Truncated)
                {
                    errors.Add($"{fileName}: case '{id}' referenceSql returned more rows than the Row_Limit.");
                    continue;
                }

                var columns = new JsonArray(result.Columns.Select(c => (JsonNode?)JsonValue.Create(c.Name)).ToArray());
                var rows = new JsonArray(result.Rows
                    .Select(r => (JsonNode?)new JsonArray(r.Select(ToNode).ToArray()))
                    .ToArray());

                var before = expected.ToJsonString();
                expected["columns"] = columns;
                expected["rows"] = rows;
                if (expected.ToJsonString() != before)
                {
                    details.Add($"case '{id}' ({Describe(before, expected)})");
                }
            }

            if (processed == processedBefore)
            {
                continue; // adversarial-only files are never rewritten
            }

            var regenerated = Serialize(root);
            if (!string.Equals(Normalize(original), regenerated, StringComparison.Ordinal))
            {
                var detail = details.Count > 0 ? string.Join("; ", details) : "formatting only";
                changed.Add($"{fileName}: {detail}");
                if (!check && errors.Count == 0)
                {
                    await File.WriteAllTextAsync(file, regenerated, new UTF8Encoding(false), ct);
                }
            }
        }

        return new ExpectedGenerationResult(changed, errors, processed, skipped);
    }

    private static string Describe(string before, JsonObject after)
    {
        var old = JsonNode.Parse(before)!.AsObject();
        var oldRows = old["rows"] is JsonArray r ? r.Count : 0;
        var newRows = after["rows"]!.AsArray().Count;
        var oldColumns = old["columns"]?.ToJsonString();
        var columnsChanged = oldColumns != after["columns"]!.ToJsonString();
        return $"rows {oldRows} -> {newRows}{(columnsChanged ? ", columns changed" : "")}";
    }

    internal static string Serialize(JsonNode root) =>
        root.ToJsonString(WriteOptions).Replace("\r\n", "\n") + "\n";

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    /// <summary>Converts a cell to its stable JSON form: decimals rounded to 2 places, invariant culture.</summary>
    internal static JsonNode? ToNode(object? value) => value switch
    {
        null or DBNull => null,
        bool b => JsonValue.Create(b),
        byte or sbyte or short or ushort or int or uint or long => JsonValue.Create(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        ulong u => JsonValue.Create(u),
        decimal d => RoundedNumber(d),
        double d => RoundedNumber(Convert.ToDecimal(d, CultureInfo.InvariantCulture)),
        float f => RoundedNumber(Convert.ToDecimal(f, CultureInfo.InvariantCulture)),
        DateTime dt => JsonValue.Create(dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
        DateOnly d => JsonValue.Create(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        string s => JsonValue.Create(s),
        _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture)),
    };

    private static JsonNode RoundedNumber(decimal value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero)
            .ToString("0.00", CultureInfo.InvariantCulture);
        // Parsing "0.00" keeps scale 2, so the JSON text always has two decimals.
        return JsonValue.Create(decimal.Parse(rounded, NumberStyles.Number, CultureInfo.InvariantCulture));
    }
}
