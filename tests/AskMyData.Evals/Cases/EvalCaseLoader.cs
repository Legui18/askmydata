using System.Text.Json;

namespace AskMyData.Evals.Cases;

public sealed class EvalCaseFormatException(string message) : Exception(message);

/// <summary>Loads and schema-checks Eval_Cases from <c>cases/*.json</c> (one case object per file, or an array of cases).</summary>
public static class EvalCaseLoader
{
    public static async Task<IReadOnlyList<EvalCase>> LoadDirectoryAsync(string directory, CancellationToken ct = default)
    {
        if (!Directory.Exists(directory))
            throw new EvalCaseFormatException($"Eval case directory not found: {directory}");

        var cases = new List<EvalCase>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var json = await File.ReadAllTextAsync(file, ct);
            try
            {
                cases.AddRange(Parse(json));
            }
            catch (EvalCaseFormatException ex)
            {
                throw new EvalCaseFormatException($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        var duplicate = cases.GroupBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new EvalCaseFormatException($"Duplicate case id '{duplicate.Key}'.");

        return cases;
    }

    public static IReadOnlyList<EvalCase> Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new EvalCaseFormatException($"Invalid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            return root.ValueKind switch
            {
                JsonValueKind.Object => [ParseCase(root)],
                JsonValueKind.Array => root.EnumerateArray().Select(ParseCase).ToList(),
                _ => throw new EvalCaseFormatException("Root must be an object or an array of objects."),
            };
        }
    }

    private static EvalCase ParseCase(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new EvalCaseFormatException("Case must be an object.");

        var id = RequiredString(e, "id", "case");
        var where = $"case '{id}'";
        var category = RequiredString(e, "category", where);
        if (!EvalCategories.All.Contains(category))
            throw new EvalCaseFormatException($"{where}: unknown category '{category}'.");

        var metric = OptionalString(e, "metric", where);
        var attack = OptionalString(e, "attack", where);
        var isAdversarial = category == EvalCategories.Adversarial;
        if (isAdversarial)
        {
            if (attack is null || !EvalAttacks.All.Contains(attack))
                throw new EvalCaseFormatException($"{where}: adversarial case needs a valid 'attack'.");
        }
        else if (attack is not null)
        {
            throw new EvalCaseFormatException($"{where}: 'attack' is only allowed on adversarial cases.");
        }

        var question = RequiredString(e, "question", where);
        var priorTurns = ParsePriorTurns(e, where);
        if (category == EvalCategories.MultiTurn && priorTurns.Count == 0)
            throw new EvalCaseFormatException($"{where}: multi-turn case needs at least one prior turn.");

        var referenceSql = OptionalString(e, "referenceSql", where);
        var expected = ParseExpected(Required(e, "expected", where, JsonValueKind.Object), where);
        if (isAdversarial && expected.Kind != EvalExpectedKinds.Blocked)
            throw new EvalCaseFormatException($"{where}: adversarial case must expect 'blocked'.");

        var stub = ParseStub(Required(e, "stub", where, JsonValueKind.Object), where);
        return new EvalCase(id, category, metric, attack, question, priorTurns, referenceSql, expected, stub);
    }

    private static IReadOnlyList<PriorTurn> ParsePriorTurns(JsonElement e, string where)
    {
        if (!e.TryGetProperty("priorTurns", out var turns))
            return [];
        if (turns.ValueKind != JsonValueKind.Array)
            throw new EvalCaseFormatException($"{where}: 'priorTurns' must be an array.");

        return turns.EnumerateArray()
            .Select(t => t.ValueKind == JsonValueKind.Object
                ? new PriorTurn(RequiredString(t, "question", $"{where} priorTurn"), RequiredString(t, "sql", $"{where} priorTurn"))
                : throw new EvalCaseFormatException($"{where}: prior turn must be an object."))
            .ToList();
    }

    private static EvalExpected ParseExpected(JsonElement e, string where)
    {
        var kind = RequiredString(e, "kind", $"{where} expected");
        switch (kind)
        {
            case EvalExpectedKinds.Rows:
            {
                var columns = StringArray(Required(e, "columns", $"{where} expected", JsonValueKind.Array), $"{where} expected.columns");
                if (columns.Count == 0)
                    throw new EvalCaseFormatException($"{where}: expected.columns must not be empty.");

                var rows = new List<IReadOnlyList<JsonElement>>();
                foreach (var row in Required(e, "rows", $"{where} expected", JsonValueKind.Array).EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Array)
                        throw new EvalCaseFormatException($"{where}: each expected row must be an array.");
                    var cells = row.EnumerateArray().Select(c => c.Clone()).ToList();
                    if (cells.Count != columns.Count)
                        throw new EvalCaseFormatException($"{where}: expected row width {cells.Count} differs from {columns.Count} columns.");
                    rows.Add(cells);
                }

                var orderSensitive = false;
                if (e.TryGetProperty("orderSensitive", out var os))
                {
                    if (os.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new EvalCaseFormatException($"{where}: expected.orderSensitive must be a boolean.");
                    orderSensitive = os.GetBoolean();
                }

                return new EvalExpected(kind, columns, rows, orderSensitive, []);
            }
            case EvalExpectedKinds.Blocked:
            {
                var codes = StringArray(Required(e, "allowedReasonCodes", $"{where} expected", JsonValueKind.Array), $"{where} expected.allowedReasonCodes");
                if (codes.Count == 0)
                    throw new EvalCaseFormatException($"{where}: expected.allowedReasonCodes must not be empty.");
                return new EvalExpected(kind, [], [], false, codes);
            }
            default:
                throw new EvalCaseFormatException($"{where}: unknown expected kind '{kind}'.");
        }
    }

    private static EvalStub ParseStub(JsonElement e, string where)
    {
        var responses = StringArray(Required(e, "responses", $"{where} stub", JsonValueKind.Array), $"{where} stub.responses");
        if (responses.Count == 0)
            throw new EvalCaseFormatException($"{where}: stub.responses must not be empty.");

        foreach (var r in responses)
        {
            try
            {
                using var _ = JsonDocument.Parse(r);
            }
            catch (JsonException)
            {
                throw new EvalCaseFormatException($"{where}: stub response is not valid JSON.");
            }
        }

        return new EvalStub(responses);
    }

    private static JsonElement Required(JsonElement e, string name, string where, JsonValueKind kind)
    {
        if (!e.TryGetProperty(name, out var value))
            throw new EvalCaseFormatException($"{where}: missing '{name}'.");
        if (value.ValueKind != kind)
            throw new EvalCaseFormatException($"{where}: '{name}' must be of kind {kind}.");
        return value;
    }

    private static string RequiredString(JsonElement e, string name, string where)
    {
        var value = Required(e, name, where, JsonValueKind.String).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new EvalCaseFormatException($"{where}: '{name}' must not be empty.");
        return value;
    }

    private static string? OptionalString(JsonElement e, string name, string where)
    {
        if (!e.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new EvalCaseFormatException($"{where}: '{name}' must be a string.");
        return value.GetString();
    }

    private static List<string> StringArray(JsonElement array, string where) =>
        array.EnumerateArray()
            .Select(x => x.ValueKind == JsonValueKind.String
                ? x.GetString()!
                : throw new EvalCaseFormatException($"{where}: items must be strings."))
            .ToList();
}
