namespace AskMyData.Application.Validation;

/// <summary>Port that validates generated SQL before execution.</summary>
public interface ISqlValidator
{
    /// <summary>
    /// Validates <paramref name="sql"/> against the safety rules and the declared Metric_Usage.
    /// Pure and deterministic; performs no I/O (R3.13).
    /// </summary>
    ValidationResult Validate(string sql, IReadOnlyList<string> metricUsage);
}
