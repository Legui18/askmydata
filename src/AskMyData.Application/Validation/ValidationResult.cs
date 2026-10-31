namespace AskMyData.Application.Validation;

/// <summary>
/// Outcome of ISqlValidator: pass or fail, the Reason_Code on failure and an optional Detail.
/// </summary>
/// <param name="Passed">True when all validation rules passed.</param>
/// <param name="ReasonCode">Reason_Code of the first failing rule; <c>null</c> on pass.</param>
/// <param name="Detail">Optional human-readable detail; names the Metric for METRIC_DEFINITION_MISMATCH.</param>
public sealed record ValidationResult(bool Passed, string? ReasonCode, string? Detail)
{
    /// <summary>Metric named by a METRIC_DEFINITION_MISMATCH failure; otherwise <c>null</c>.</summary>
    public string? MetricName { get; init; }

    /// <summary>Creates a passing result.</summary>
    public static ValidationResult Pass() => new(true, null, null);

    /// <summary>Creates a failing result with the given Reason_Code and optional Detail.</summary>
    public static ValidationResult Fail(string reasonCode, string? detail = null) => new(false, reasonCode, detail);
}
