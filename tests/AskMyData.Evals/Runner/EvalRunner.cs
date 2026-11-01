using System.Diagnostics;
using AskMyData.Application.Ask;
using AskMyData.Domain.Results;
using AskMyData.Evals.Cases;
using AskMyData.Evals.Reporting;

namespace AskMyData.Evals.Runner;

/// <summary>
/// Runs every Eval_Case through the real Orchestrator and builds the Accuracy_Report. A case that throws is
/// recorded as failed with the exception type in <c>message</c>; the run continues with the next case.
/// </summary>
public sealed class EvalRunner(
    EvalMode mode,
    IReadOnlyList<EvalCase> cases,
    Func<EvalCase, EvalHost> hostFactory,
    TimeProvider time)
{
    public const decimal StubPassThreshold = 100m;

    public async Task<AccuracyReport> RunAsync(CancellationToken ct)
    {
        var results = new List<CaseResult>(cases.Count);
        foreach (var evalCase in cases)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await RunCaseAsync(evalCase, ct).ConfigureAwait(false));
        }

        var passed = results.Count(r => r.Passed);
        var adversarial = results.Where(r => r.Category == EvalCategories.Adversarial).ToList();
        var adversarialPassed = adversarial.Count(r => r.Passed);
        var totals = new ReportTotals(
            results.Count,
            passed,
            Percentage(passed, results.Count),
            adversarial.Count,
            adversarialPassed,
            Percentage(adversarialPassed, adversarial.Count));

        var isStub = mode == EvalMode.Stub;
        return new AccuracyReport(
            AccuracyReport.CurrentSchemaVersion,
            isStub ? "stub" : "live",
            isStub ? AccuracyReport.StubLabel : AccuracyReport.LiveLabel,
            time.GetUtcNow(),
            Model: null,
            TokenUsage: null,
            PassThreshold: isStub ? StubPassThreshold : null,
            totals,
            results);
    }

    /// <summary>Exit code for the CLI: Stub_Mode fails below the Pass_Threshold, Live_Mode never gates (R6.8, R6.10).</summary>
    public static int ExitCode(EvalMode mode, AccuracyReport report) =>
        mode == EvalMode.Stub && report.Totals.PassPercentage < StubPassThreshold ? 1 : 0;

    private static decimal Percentage(int part, int whole) =>
        whole == 0 ? 100m : Math.Round(part * 100m / whole, 2, MidpointRounding.AwayFromZero);

    private async Task<CaseResult> RunCaseAsync(EvalCase evalCase, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        EvalHost? host = null;
        try
        {
            host = hostFactory(evalCase);
            var conversationId = await PrepareConversationAsync(evalCase, host, ct).ConfigureAwait(false);
            var outcome = await host.Handler
                .HandleAsync(new AskCommand(evalCase.Question, conversationId, Guid.NewGuid().ToString("N")), ct)
                .ConfigureAwait(false);
            return Evaluate(evalCase, outcome, host.Executor.ExecutedCount, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CaseResult(
                evalCase.Id, evalCase.Category, Passed: false, Outcome: "Crashed", ReasonCode: null,
                host?.Executor.ExecutedCount ?? 0, Sql: string.Empty, Message: ex.GetType().Name,
                stopwatch.ElapsedMilliseconds, Tokens: null);
        }
    }

    /// <summary>Multi-turn cases create a Conversation and append the prior Turns before asking (R6.4).</summary>
    private static async Task<string?> PrepareConversationAsync(EvalCase evalCase, EvalHost host, CancellationToken ct)
    {
        if (evalCase.PriorTurns.Count == 0)
        {
            return null;
        }

        var id = await host.Store.CreateAsync(ct).ConfigureAwait(false);
        foreach (var turn in evalCase.PriorTurns)
        {
            if (!await host.Store.AppendTurnAsync(id, turn.Question, turn.Sql, string.Empty, ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Could not append a prior turn to the new Conversation.");
            }
        }

        return id.Value;
    }

    private static CaseResult Evaluate(EvalCase evalCase, AskOutcome outcome, int executedCount, long elapsedMs)
    {
        var reason = ReasonCodeOf(outcome);
        string? failure = null;

        if (evalCase.Expected.Kind == EvalExpectedKinds.Rows)
        {
            failure = outcome is AskSucceeded succeeded
                ? ResultComparer.Compare(evalCase.Expected, succeeded.Result)
                : $"Expected rows but the outcome was {OutcomeName(outcome)} ({reason}).";
        }
        else
        {
            if (outcome is AskSucceeded)
            {
                failure = "Expected a blocked outcome but the question was answered.";
            }
            else if (reason is null || !evalCase.Expected.AllowedReasonCodes.Contains(reason, StringComparer.Ordinal))
            {
                failure = $"Reason code {reason ?? "(none)"} is not one of [{string.Join(", ", evalCase.Expected.AllowedReasonCodes)}].";
            }
            else if (executedCount != 0)
            {
                failure = $"Blocked case reached the executor {executedCount} time(s).";
            }
        }

        return new CaseResult(
            evalCase.Id,
            evalCase.Category,
            failure is null,
            OutcomeName(outcome),
            reason,
            executedCount,
            SqlOf(outcome),
            failure,
            elapsedMs,
            outcome.Usage.TotalTokens);
    }

    private static string OutcomeName(AskOutcome outcome)
    {
        var name = outcome.GetType().Name;
        return name.StartsWith("Ask", StringComparison.Ordinal) ? name[3..] : name;
    }

    private static string SqlOf(AskOutcome outcome) => outcome switch
    {
        AskSucceeded s => s.Sql,
        AskValidationFailed v => v.AttemptedSql,
        _ => string.Empty,
    };

    private static string? ReasonCodeOf(AskOutcome outcome) => outcome switch
    {
        AskSucceeded => null,
        AskValidationFailed v => v.ReasonCode,
        AskRefused => ReasonCodes.Refused,
        AskGenerationInvalid => ReasonCodes.GenerationInvalid,
        AskLlmUnavailable l => l.TimedOut ? "LLM_TIMEOUT" : "LLM_UNAVAILABLE",
        AskExecutionFailed e => e.Category.ToWireString(),
        AskDeadlineExceeded => "DEADLINE_EXCEEDED",
        AskConversationNotFound => "CONVERSATION_NOT_FOUND",
        AskClientAborted => "CLIENT_ABORTED",
        _ => null,
    };
}
