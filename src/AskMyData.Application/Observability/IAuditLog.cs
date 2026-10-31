using AskMyData.Application.Llm;

namespace AskMyData.Application.Observability;

/// <summary>Port for structured audit logging of requests.</summary>
public interface IAuditLog
{
    /// <summary>Writes the single audit record of a completed request (R7.2).</summary>
    void Write(AuditRecord record);

    /// <summary>Logs a failed or timed-out ILlmClient call (R7.4).</summary>
    void LlmCallFailed(string provider, LlmCallType callType, TimeSpan elapsed, string correlationId, bool timedOut);

    /// <summary>Logs a validator rejection with its Reason_Code (R7.5).</summary>
    void ValidationFailed(string reasonCode, string correlationId);
}
