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
/// One structured audit entry per completed request (R7.2). Includes token, cost, execution and provider fields.
/// </summary>
/// <param name="CorrelationId">Correlation_Id of the request.</param>
/// <param name="ConversationId">Conversation_Id, or the requested id when it was not found.</param>
/// <param name="Question">The question text.</param>
/// <param name="Sql">The last generated SQL; empty when the request failed before SQL generation (R7.9).</param>
/// <param name="ValidationOutcome">Accepted, rejected or not validated.</param>
/// <param name="ValidationReasonCode">Reason_Code when the validation outcome is rejected; otherwise <c>null</c>.</param>
/// <param name="FailureReason">Reason_Code or category of a failed request (for example DEADLINE_EXCEEDED); <c>null</c> on success.</param>
/// <param name="LatencyMs">Total request latency in milliseconds.</param>
/// <param name="PromptTokens">Prompt tokens summed over all LLM calls of the request.</param>
/// <param name="CompletionTokens">Completion tokens summed over all LLM calls of the request.</param>
/// <param name="EstimatedCostUsd">Estimated cost summed over all LLM calls; 0 when the model is not priced.</param>
/// <param name="ExecutionMs">Time spent executing SQL in milliseconds; 0 when nothing was executed.</param>
/// <param name="RowCount">Rows returned; 0 when the request did not succeed.</param>
/// <param name="RepairCallOccurred">Whether a Repair_Call was made.</param>
/// <param name="Provider">Provider_Name of the ILlmClient.</param>
/// <param name="Model">Model that produced the last response; empty when no LLM call completed.</param>
public sealed record AuditRecord(
    string CorrelationId,
    string ConversationId,
    string Question,
    string Sql,
    AuditValidationOutcome ValidationOutcome,
    string? ValidationReasonCode,
    string? FailureReason,
    long LatencyMs,
    int PromptTokens = 0,
    int CompletionTokens = 0,
    decimal EstimatedCostUsd = 0m,
    long ExecutionMs = 0,
    int RowCount = 0,
    bool RepairCallOccurred = false,
    string Provider = "",
    string Model = "");
