namespace AskMyData.Application.Observability;

/// <summary>Validation outcome recorded in the audit log.</summary>
public enum AuditValidationOutcome
{
    /// <summary>No SQL reached the validator (for example a refusal or an LLM failure).</summary>
    NotValidated,

    /// <summary>The last validated SQL passed.</summary>
    Accepted,

    /// <summary>The last validated SQL was rejected.</summary>
    Rejected,
}

/// <summary>
/// One structured audit entry per completed request (R7.2). This is the minimal shape; token, cost,
/// execution and provider fields are added later.
/// </summary>
/// <param name="CorrelationId">Correlation_Id of the request.</param>
/// <param name="ConversationId">Conversation_Id, or the requested id when it was not found.</param>
/// <param name="Question">The question text.</param>
/// <param name="Sql">The last generated SQL; empty when the request failed before SQL generation (R7.9).</param>
/// <param name="ValidationOutcome">Accepted, rejected or not validated.</param>
/// <param name="ValidationReasonCode">Reason_Code when the validation outcome is rejected; otherwise <c>null</c>.</param>
/// <param name="FailureReason">Reason_Code or category of a failed request (for example DEADLINE_EXCEEDED); <c>null</c> on success.</param>
/// <param name="LatencyMs">Total request latency in milliseconds.</param>
public sealed record AuditRecord(
    string CorrelationId,
    string ConversationId,
    string Question,
    string Sql,
    AuditValidationOutcome ValidationOutcome,
    string? ValidationReasonCode,
    string? FailureReason,
    long LatencyMs);
