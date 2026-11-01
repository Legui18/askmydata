using AskMyData.Application.Ask;
using AskMyData.UnitTests.Generators;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Orchestrator;

/// <summary>Property 12: the Request_Deadline bounds every step.</summary>
public class RequestDeadlineProperties
{
    private const string DeadlineReason = "DEADLINE_EXCEEDED";
    private static readonly TimeSpan Epsilon = TimeSpan.FromMilliseconds(1);

    // Feature: askmydata-conversational-analytics, Property 12: Request_Deadline bounds every step
    // **Validates: Requirements 2.15, 2.16, 2.17, 2.18, 4.9, 4.10, 7.12**
    [Property(MaxTest = 100)]
    public Property Request_deadline_bounds_every_step() =>
        Prop.ForAll(
            LlmScriptGen.WithDelays(DelayScriptGen.Delay).ToArbitrary(),
            ExecutorScriptGen.WithDelays(DelayScriptGen.Delay).ToArbitrary(),
            (llm, exec) =>
            {
                var harness = OrchestratorHarness.Create(llm, exec);
                var deadline = harness.Options.RequestDeadline;
                var llmTimeout = harness.Options.LlmTimeout;
                var commandTimeout = harness.Options.CommandTimeout;
                var t0 = harness.Time.GetUtcNow();

                var outcome = harness.AskAndAdvanceAsync(TimeSpan.FromSeconds(300)).GetAwaiter().GetResult();
                var total = harness.Time.GetUtcNow() - t0;
                var isDeadline = outcome is AskDeadlineExceeded;
                var ok = true;

                // R2.15/R2.18: at most one Repair_Call, and no LLM call starts at or after expiry.
                var starts = harness.Llm.StartTimes;
                ok &= starts.Count is >= 1 and <= 2;
                ok &= starts.All(s => s - t0 < deadline);

                // R2.16/R2.17: each LLM call is bounded by min(LLM_Timeout, remaining deadline).
                for (var i = 0; i < starts.Count; i++)
                {
                    var step = llm[Math.Min(i, llm.Count - 1)];
                    var remaining = deadline - (starts[i] - t0);
                    var bound = remaining < llmTimeout ? remaining : llmTimeout;
                    if (step.Delay > bound)
                    {
                        // Cancelled in flight: the call is the last one and the request ends accordingly.
                        ok &= harness.Llm.Tokens[i].IsCancellationRequested;
                        ok &= starts.Count == i + 1;
                        ok &= remaining <= llmTimeout
                            ? isDeadline
                            : outcome is AskLlmUnavailable { TimedOut: true };
                    }
                    else if (step.Delay < bound && outcome is AskLlmUnavailable { TimedOut: true })
                    {
                        // A call that finishes within its bound is never reported as a timeout.
                        ok &= i != starts.Count - 1;
                    }
                }

                // R4.9/R4.10: each SQL command's budget is min(command timeout, remaining); expiry cancels it.
                var execStarts = harness.Executor.StartTimes;
                var budgets = harness.Executor.Budgets;
                for (var k = 0; k < execStarts.Count; k++)
                {
                    var remaining = deadline - (execStarts[k] - t0);
                    var expected = remaining < commandTimeout ? remaining : commandTimeout;
                    ok &= remaining > TimeSpan.Zero;
                    ok &= (budgets[k].CommandTimeout - expected).Duration() <= Epsilon;

                    var delay = exec[Math.Min(k, exec.Count - 1)].Delay;
                    if (delay > remaining)
                    {
                        ok &= isDeadline && execStarts.Count == k + 1;
                    }
                }

                // R7.12: exactly one audit record, DEADLINE_EXCEEDED iff the request hit the deadline.
                var records = harness.Audit.Records;
                ok &= records.Count == 1;
                if (records.Count == 1)
                {
                    ok &= isDeadline == (records[0].FailureReason == DeadlineReason);
                }

                if (isDeadline)
                {
                    ok &= total <= deadline + TimeSpan.FromSeconds(1);
                }

                return ok;
            });
}
