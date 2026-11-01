using System.Text.Json;
using AskMyData.Application.Observability;
using AskMyData.Domain.Results;
using AskMyData.Infrastructure.Observability;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;

namespace AskMyData.UnitTests.Orchestrator;

/// <summary>Property 21: exactly one complete audit record per request, without sensitive data.</summary>
public class AuditRecordProperties
{
    private enum Scenario
    {
        Success,
        Refusal,
        GarbageTwice,
        RejectedTwice,
        LlmUnavailable,
        ExecutionError,
        ExecutionTimeout,
    }

    private sealed class CapturingLogger : ILogger<JsonAuditLog>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            if (state is IEnumerable<KeyValuePair<string, object?>> kvs)
            {
                text += " " + string.Join(" ", kvs.Select(kv => $"{kv.Key}={kv.Value}"));
            }

            Entries.Add(text);
        }
    }

    private static readonly Gen<(Scenario Scenario, int Rows, int Seed)> Cases =
        from scenario in Gen.Elements(Enum.GetValues<Scenario>())
        from rows in Gen.Choose(1, 20)
        from seed in Gen.Choose(0, 1_000_000)
        select (scenario, rows, seed);

    // Feature: askmydata-conversational-analytics, Property 21: Exactly one complete audit record per request, without sensitive data
    // **Validates: Requirements 7.2, 7.3, 7.5, 7.6, 7.9**
    [Property(MaxTest = 100)]
    public Property Exactly_one_complete_audit_record_without_sensitive_data() =>
        Prop.ForAll(
            Cases.ToArbitrary(),
            c =>
            {
                var valid = LlmStep.Respond(LlmResponses.Structured(LlmResponses.ValidSql));
                var invalid = LlmStep.Respond(LlmResponses.Structured(LlmResponses.InvalidSql));
                var garbage = LlmStep.Respond(LlmResponses.Garbage);

                var rowResult = ExecutorStep.Rows(Results.Sample(c.Rows, seed: c.Seed));
                (LlmStep[] llm, ExecutorStep exec) = c.Scenario switch
                {
                    Scenario.Success => (new[] { valid }, rowResult),
                    Scenario.Refusal => (new[] { LlmStep.Respond(LlmResponses.Refusal("no")) }, rowResult),
                    Scenario.GarbageTwice => (new[] { garbage, garbage }, rowResult),
                    Scenario.RejectedTwice => (new[] { invalid, invalid }, rowResult),
                    Scenario.LlmUnavailable => (new[] { LlmStep.Fail() }, rowResult),
                    Scenario.ExecutionError => (new[] { valid, valid }, ExecutorStep.Fail(ExecutionErrorCategory.ExecutionError)),
                    _ => (new[] { valid, valid }, ExecutorStep.Fail(ExecutionErrorCategory.Timeout)),
                };

                var harness = OrchestratorHarness.Create(llm, [exec]);
                _ = harness.AskAsync().GetAwaiter().GetResult();

                if (harness.Audit.Records.Count != 1)
                {
                    return false;
                }

                var r = harness.Audit.Records[0];
                var complete =
                    r.CorrelationId == "corr-1"
                    && !string.IsNullOrEmpty(r.ConversationId)
                    && r.Question == OrchestratorHarness.DefaultQuestion
                    && r.LatencyMs >= 0
                    && r.PromptTokens >= 0
                    && r.CompletionTokens >= 0
                    && r.EstimatedCostUsd >= 0
                    && r.ExecutionMs >= 0;

                var scenarioOk = c.Scenario switch
                {
                    Scenario.Success => r.Sql == LlmResponses.ValidSql
                        && r.ValidationOutcome == AuditValidationOutcome.Accepted
                        && r.ValidationReasonCode is null
                        && r.FailureReason is null
                        && r.RowCount == c.Rows
                        && !r.RepairCallOccurred,
                    Scenario.Refusal => r.Sql.Length == 0
                        && r.ValidationOutcome == AuditValidationOutcome.NotValidated
                        && r.FailureReason is not null
                        && !r.RepairCallOccurred,
                    Scenario.GarbageTwice => r.Sql.Length == 0
                        && r.ValidationOutcome == AuditValidationOutcome.NotValidated
                        && r.FailureReason is not null
                        && r.RepairCallOccurred,
                    Scenario.RejectedTwice => r.Sql == LlmResponses.InvalidSql
                        && r.ValidationOutcome == AuditValidationOutcome.Rejected
                        && r.ValidationReasonCode is { Length: > 0 }
                        && harness.Audit.ValidationFailures.Count == 2
                        && harness.Audit.ValidationFailures.All(x => x == r.ValidationReasonCode)
                        && r.RepairCallOccurred
                        && r.RowCount == 0,
                    Scenario.LlmUnavailable => r.Sql.Length == 0
                        && r.ValidationOutcome == AuditValidationOutcome.NotValidated
                        && r.FailureReason is not null
                        && harness.Audit.LlmFailures.Count == 1,
                    _ => r.Sql == LlmResponses.ValidSql
                        && r.ValidationOutcome == AuditValidationOutcome.Accepted
                        && r.FailureReason is not null
                        && r.RepairCallOccurred
                        && r.RowCount == 0,
                };

                // Sensitive data: marker strings in cell values, API key and connection string must never be logged.
                var apiKeyMarker = $"APIKEY-{c.Seed}";
                var connectionMarker = $"Password=CONNMARK-{c.Seed}";
                var logger = new CapturingLogger();
                var log = new JsonAuditLog(logger);
                log.Write(r);
                foreach (var reason in harness.Audit.ValidationFailures)
                {
                    log.ValidationFailed(reason, r.CorrelationId);
                }

                foreach (var failure in harness.Audit.LlmFailures)
                {
                    log.LlmCallFailed(failure.Provider, failure.CallType, TimeSpan.Zero, r.CorrelationId, failure.TimedOut);
                }

                var captured = string.Join("\n", logger.Entries) + JsonSerializer.Serialize(r);
                var clean = !captured.Contains(Results.MarkerPrefix, StringComparison.Ordinal)
                    && !captured.Contains(apiKeyMarker, StringComparison.Ordinal)
                    && !captured.Contains(connectionMarker, StringComparison.Ordinal)
                    && logger.Entries.Count >= 1;

                return complete && scenarioOk && clean;
            });
}
