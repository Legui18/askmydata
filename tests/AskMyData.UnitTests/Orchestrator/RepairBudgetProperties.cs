using System.Text.Json;
using AskMyData.Application.Ask;
using AskMyData.Application.Execution;
using AskMyData.Application.Llm;
using AskMyData.Domain.Results;
using AskMyData.UnitTests.Generators;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Orchestrator;

/// <summary>Property 8: at most one Repair_Call, and the last attempt decides the outcome.</summary>
public class RepairBudgetProperties
{
    private enum Kind
    {
        Valid,
        InvalidSql,
        Garbage,
        Refusal,
        Unavailable,
    }

    private static Kind Classify(LlmStep step)
    {
        if (step.Kind == LlmStepKind.Unavailable)
        {
            return Kind.Unavailable;
        }

        if (step.RawText == LlmResponses.Garbage)
        {
            return Kind.Garbage;
        }

        using var doc = JsonDocument.Parse(step.RawText);
        if (doc.RootElement.GetProperty("refusal").ValueKind == JsonValueKind.String)
        {
            return Kind.Refusal;
        }

        return doc.RootElement.GetProperty("sql").GetString() == LlmResponses.InvalidSql ? Kind.InvalidSql : Kind.Valid;
    }

    private static string ExplanationOf(LlmStep step)
    {
        using var doc = JsonDocument.Parse(step.RawText);
        return doc.RootElement.GetProperty("explanation").GetString()!;
    }

    // Feature: askmydata-conversational-analytics, Property 8: Repair budget is at most one and the last attempt decides the outcome
    // **Validates: Requirements 2.2, 2.6, 2.7, 2.8, 2.11, 2.14, 9.5**
    [Property(MaxTest = 100)]
    public Property Repair_budget_is_one_and_last_attempt_decides_outcome() =>
        Prop.ForAll(
            LlmScriptGen.Script.ToArbitrary(),
            ExecutorScriptGen.Script.ToArbitrary(),
            (llm, exec) =>
            {
                var harness = OrchestratorHarness.Create(llm, exec);
                var outcome = harness.AskAndAdvanceAsync(TimeSpan.FromSeconds(200)).GetAwaiter().GetResult();

                var requests = harness.Llm.Requests;
                var budgetOk = requests.Count is >= 1 and <= 2
                               && requests[0].CallType == LlmCallType.Initial
                               && requests.Skip(1).All(r => r.CallType == LlmCallType.Repair)
                               && requests.Count(r => r.CallType == LlmCallType.Repair) <= 1;

                // Model: replay the scripts the way the Orchestrator must consume them.
                var execIndex = 0;
                var expectedCalls = 0;
                var expectedKind = default(Type);
                ExecutionErrorCategory? expectedExecCategory = null;
                var lastStep = llm[0];
                var lastExecWasSuccess = false;

                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var step = llm[Math.Min(attempt, llm.Count - 1)];
                    lastStep = step;
                    expectedCalls++;
                    var repairable = attempt == 0;
                    switch (Classify(step))
                    {
                        case Kind.Refusal:
                            expectedKind = typeof(AskRefused);
                            break;
                        case Kind.Unavailable:
                            expectedKind = typeof(AskLlmUnavailable);
                            break;
                        case Kind.Garbage:
                            expectedKind = typeof(AskGenerationInvalid);
                            break;
                        case Kind.InvalidSql:
                            expectedKind = typeof(AskValidationFailed);
                            break;
                        default:
                            var e = exec[Math.Min(execIndex++, exec.Count - 1)];
                            if (e.Error is { } category)
                            {
                                expectedKind = typeof(AskExecutionFailed);
                                expectedExecCategory = category;
                            }
                            else
                            {
                                expectedKind = typeof(AskSucceeded);
                                lastExecWasSuccess = true;
                            }

                            break;
                    }

                    var retry = repairable
                                && (expectedKind == typeof(AskGenerationInvalid)
                                    || expectedKind == typeof(AskValidationFailed)
                                    || expectedKind == typeof(AskExecutionFailed));
                    if (!retry)
                    {
                        break;
                    }
                }

                var calls = requests.Count == expectedCalls;
                var kindOk = outcome.GetType() == expectedKind;

                var detailOk = outcome switch
                {
                    AskValidationFailed v =>
                        harness.Validator.Calls.Count > 0
                        && v.ReasonCode == harness.Validator.Calls[^1].Result.ReasonCode
                        && v.AttemptedSql == LlmResponses.InvalidSql,
                    AskExecutionFailed x => x.Category == expectedExecCategory
                                            && harness.Executor.CallCount == execIndex,
                    AskSucceeded s => lastExecWasSuccess
                                      && ExplanationOf(lastStep).StartsWith(s.Explanation, StringComparison.Ordinal),
                    _ => true,
                };

                // The explanation is never produced by an extra call: calls == attempts, nothing after the outcome.
                var noExtra = harness.Llm.CallCount == expectedCalls;

                return budgetOk && calls && kindOk && detailOk && noExtra;
            });
}
