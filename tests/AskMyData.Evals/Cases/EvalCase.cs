using System.Text.Json;

namespace AskMyData.Evals.Cases;

public static class EvalCategories
{
    public const string Metric = "metric";
    public const string Dimension = "dimension";
    public const string MultiTurn = "multi-turn";
    public const string Adversarial = "adversarial";

    public static readonly IReadOnlyList<string> All = [Metric, Dimension, MultiTurn, Adversarial];
}

public static class EvalAttacks
{
    public static readonly IReadOnlyList<string> All =
    [
        "instruction-override",
        "stacked-statements",
        "comment-obfuscation",
        "non-allowlist-table",
        "data-modification",
        "prompt-disclosure",
    ];
}

public static class EvalExpectedKinds
{
    public const string Rows = "rows";
    public const string Blocked = "blocked";
}

public sealed record PriorTurn(string Question, string Sql);

/// <summary>Expected outcome: either exact rows or a blocked outcome with allowed Reason_Codes.</summary>
public sealed record EvalExpected(
    string Kind,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<JsonElement>> Rows,
    bool OrderSensitive,
    IReadOnlyList<string> AllowedReasonCodes);

/// <summary>Recorded Structured_Responses (raw JSON strings) replayed by the stub LLM client in order.</summary>
public sealed record EvalStub(IReadOnlyList<string> Responses);

public sealed record EvalCase(
    string Id,
    string Category,
    string? Metric,
    string? Attack,
    string Question,
    IReadOnlyList<PriorTurn> PriorTurns,
    string? ReferenceSql,
    EvalExpected Expected,
    EvalStub Stub)
{
    public bool IsAdversarial => Category == EvalCategories.Adversarial;
}
