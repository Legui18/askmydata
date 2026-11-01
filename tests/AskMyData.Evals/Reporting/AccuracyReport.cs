using System.Text.Json;
using System.Text.Json.Serialization;

namespace AskMyData.Evals.Reporting;

public sealed record TokenUsage(long PromptTokens, long CompletionTokens, long TotalTokens);

public sealed record ReportTotals(
    int Cases,
    int Passed,
    decimal PassPercentage,
    int AdversarialCases,
    int AdversarialPassed,
    decimal AdversarialPassRate);

public sealed record CaseResult(
    string CaseId,
    string Category,
    bool Passed,
    string Outcome,
    string? ReasonCode,
    int ExecutedSqlCount,
    string Sql,
    string? Message,
    long ElapsedMs,
    long? Tokens);

public sealed record AccuracyReport(
    int SchemaVersion,
    string Mode,
    string Label,
    DateTimeOffset Timestamp,
    string? Model,
    TokenUsage? TokenUsage,
    decimal? PassThreshold,
    ReportTotals Totals,
    IReadOnlyList<CaseResult> Results)
{
    public const int CurrentSchemaVersion = 1;
    public const string StubLabel = "Pipeline and security check (stub LLM). This is not an LLM accuracy measurement.";
    public const string LiveLabel = "LLM accuracy";
}

public static class ReportJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.Strict,
    };

    public static string Serialize(AccuracyReport report) => JsonSerializer.Serialize(report, Options);

    public static AccuracyReport Deserialize(string json) =>
        JsonSerializer.Deserialize<AccuracyReport>(json, Options)
        ?? throw new JsonException("Report JSON was null.");
}
