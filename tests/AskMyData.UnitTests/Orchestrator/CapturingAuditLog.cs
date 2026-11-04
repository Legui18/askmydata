using AskMyData.Application.Llm;
using AskMyData.Application.Observability;

namespace AskMyData.UnitTests.Orchestrator;

/// <summary>Full arguments of one <see cref="IAuditLog.LlmCallFailed"/> notification.</summary>
public sealed record LlmFailureEntry(
    string Provider,
    LlmCallType CallType,
    TimeSpan Elapsed,
    string CorrelationId,
    bool TimedOut);

/// <summary>In-memory IAuditLog that captures every record and failure notification.</summary>
public sealed class CapturingAuditLog : IAuditLog
{
    private readonly List<AuditRecord> _records = [];
    private readonly List<(string Provider, LlmCallType CallType, bool TimedOut)> _llmFailures = [];
    private readonly List<LlmFailureEntry> _llmFailureDetails = [];
    private readonly List<string> _validationFailures = [];
    private readonly object _gate = new();

    public IReadOnlyList<AuditRecord> Records
    {
        get
        {
            lock (_gate)
            {
                return _records.ToArray();
            }
        }
    }

    public IReadOnlyList<(string Provider, LlmCallType CallType, bool TimedOut)> LlmFailures
    {
        get
        {
            lock (_gate)
            {
                return _llmFailures.ToArray();
            }
        }
    }

    public IReadOnlyList<string> ValidationFailures
    {
        get
        {
            lock (_gate)
            {
                return _validationFailures.ToArray();
            }
        }
    }

    public void Write(AuditRecord record)
    {
        lock (_gate)
        {
            _records.Add(record);
        }
    }

    public void LlmCallFailed(string provider, LlmCallType callType, TimeSpan elapsed, string correlationId, bool timedOut)
    {
        lock (_gate)
        {
            _llmFailures.Add((provider, callType, timedOut));
            _llmFailureDetails.Add(new LlmFailureEntry(provider, callType, elapsed, correlationId, timedOut));
        }
    }

    public IReadOnlyList<LlmFailureEntry> LlmFailureDetails
    {
        get
        {
            lock (_gate)
            {
                return _llmFailureDetails.ToArray();
            }
        }
    }

    public void ValidationFailed(string reasonCode, string correlationId)
    {
        lock (_gate)
        {
            _validationFailures.Add(reasonCode);
        }
    }
}
