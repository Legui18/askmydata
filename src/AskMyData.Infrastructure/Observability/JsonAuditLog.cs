using AskMyData.Application.Llm;
using AskMyData.Application.Observability;
using Microsoft.Extensions.Logging;

namespace AskMyData.Infrastructure.Observability;

/// <summary>
/// <see cref="IAuditLog"/> adapter that writes each entry as one structured log event. With the JSON
/// console formatter every property becomes a JSON field. Only the fields of the records are logged:
/// never result row values, API keys or connection strings (R7.6).
/// </summary>
public sealed class JsonAuditLog : IAuditLog
{
    private readonly ILogger<JsonAuditLog> _logger;

    /// <summary>Creates the audit log.</summary>
    public JsonAuditLog(ILogger<JsonAuditLog> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void Write(AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        _logger.LogInformation(
            "{event} {CorrelationId} {ConversationId} {Question} {Sql} {ValidationOutcome} {ValidationReasonCode} {FailureReason} {LatencyMs} {PromptTokens} {CompletionTokens} {EstimatedCostUsd} {ExecutionMs} {RowCount} {RepairCallOccurred} {Provider} {Model}",
            "ask.completed",
            record.CorrelationId,
            record.ConversationId,
            record.Question,
            record.Sql,
            record.ValidationOutcome.ToString(),
            record.ValidationReasonCode,
            record.FailureReason,
            record.LatencyMs,
            record.PromptTokens,
            record.CompletionTokens,
            record.EstimatedCostUsd,
            record.ExecutionMs,
            record.RowCount,
            record.RepairCallOccurred,
            record.Provider,
            record.Model);
    }

    /// <inheritdoc />
    public void LlmCallFailed(string provider, LlmCallType callType, TimeSpan elapsed, string correlationId, bool timedOut)
    {
        _logger.LogWarning(
            "{event} {CorrelationId} {Provider} {CallType} {ElapsedMs} {TimedOut}",
            "llm.call.failed",
            correlationId,
            provider,
            callType.ToString(),
            (long)elapsed.TotalMilliseconds,
            timedOut);
    }

    /// <inheritdoc />
    public void ValidationFailed(string reasonCode, string correlationId)
    {
        _logger.LogWarning(
            "{event} {CorrelationId} {ReasonCode}",
            "validation.failed",
            correlationId,
            reasonCode);
    }
}
