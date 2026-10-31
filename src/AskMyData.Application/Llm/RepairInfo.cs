namespace AskMyData.Application.Llm;

/// <summary>
/// Failure information supplied on a Repair_Call. Contains only the Reason_Code or error category
/// and the Metric name from the Validation_Result Detail; never database error text or result data (R9.2).
/// </summary>
/// <param name="ReasonCodeOrCategory">Validator Reason_Code, GENERATION_INVALID, TIMEOUT or EXECUTION_ERROR.</param>
/// <param name="MetricName">Metric named by a METRIC_DEFINITION_MISMATCH, otherwise <c>null</c>.</param>
public sealed record RepairInfo(string ReasonCodeOrCategory, string? MetricName);
