using AskMyData.Application.Ask;
using AskMyData.Application.Charts;
using AskMyData.Domain.Conversations;
using AskMyData.UnitTests.Generators;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Orchestrator;

/// <summary>Property 11: successful answers are transparent and consistent.</summary>
public class SuccessfulAnswerProperties
{
    private const string GovernedSql = "SELECT SUM(dbo.OrderLines.Quantity) AS U FROM dbo.OrderLines";

    private sealed record Scenario(
        bool Governed,
        bool AdHocFlag,
        string Explanation,
        bool RepairFirst,
        IReadOnlyList<ExecutorStep> Executor);

    private static Gen<Scenario> ScenarioGen { get; } =
        from governed in Gen.Elements(true, false)
        from adHoc in Gen.Elements(true, false)
        from explanation in LlmScriptGen.ExplanationText
        from repairFirst in Gen.Elements(true, false)
        from rows in ExecutorScriptGen.RowsStep
        select new Scenario(governed, adHoc, explanation, repairFirst, [rows]);

    // Feature: askmydata-conversational-analytics, Property 11: Successful answers are transparent and consistent
    // **Validates: Requirements 2.5, 2.9, 2.10, 2.12, 9.5, 9.6, 12.7**
    [Property(MaxTest = 100)]
    public Property Successful_answers_are_transparent_and_consistent() =>
        Prop.ForAll(
            ScenarioGen.ToArbitrary(),
            s =>
            {
                var sql = s.Governed ? GovernedSql : LlmResponses.ValidSql;
                var usage = s.Governed ? new[] { "UnitsSold" } : [];
                var good = LlmStep.Respond(LlmResponses.Structured(sql, usage, s.Explanation, s.AdHocFlag));
                var script = s.RepairFirst
                    ? new[] { LlmStep.Respond(LlmResponses.Structured(LlmResponses.InvalidSql)), good }
                    : new[] { good };

                var harness = OrchestratorHarness.Create(script, s.Executor);
                var outcome = harness.AskAndAdvanceAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                if (outcome is not AskSucceeded ok)
                {
                    return false.Label($"Expected AskSucceeded but got {outcome.GetType().Name}");
                }

                // R2.10: the SQL shown is the SQL the executor received.
                var sqlOk = harness.Executor.Executed.Count == 1
                            && ok.Sql == harness.Executor.Executed[0].Text
                            && ok.Sql == sql;

                // R2.5: chart suggestion equals ChartSuggester output for the result.
                var expectedChart = new ChartSuggester().Suggest(ok.Result);
                var chartOk = ok.Chart.Type == expectedChart.Type
                              && ok.Chart.XColumn == expectedChart.XColumn
                              && ok.Chart.YColumns.SequenceEqual(expectedChart.YColumns);

                // R9.5, R9.6: explanation is from the same call's Structured_Response, at most 500 chars,
                // and no extra LLM call exists to produce it.
                var expectedCalls = s.RepairFirst ? 2 : 1;
                var explanationOk = ok.Explanation.Length <= 500
                                    && s.Explanation.StartsWith(ok.Explanation, StringComparison.Ordinal)
                                    && ok.Explanation == (s.Explanation.Length <= 500 ? s.Explanation : s.Explanation[..500])
                                    && harness.Llm.CallCount == expectedCalls;

                // R2.9, R2.12: ad hoc warning iff Metric_Usage is empty or adHocCalculation is true.
                var adHoc = !s.Governed || s.AdHocFlag;
                var warned = ok.Warnings.Contains(AskQuestionHandler.AdHocWarning);
                var warningOk = warned == adHoc
                                && (s.Governed ? ok.MetricUsage.SequenceEqual(usage) : ok.MetricUsage.Count == 0);

                // R12.7: exactly one Turn with question, SQL and explanation.
                ConversationId.TryParse(ok.ConversationId, out var id);
                var turns = harness.Store.LoadRecentTurnsAsync(id, 10, CancellationToken.None).GetAwaiter().GetResult();
                var turnOk = turns is { Count: 1 }
                             && turns[0].Question == OrchestratorHarness.DefaultQuestion
                             && turns[0].Sql == ok.Sql
                             && turns[0].Explanation == ok.Explanation;

                return (sqlOk && chartOk && explanationOk && warningOk && turnOk)
                    .Label($"sql={sqlOk} chart={chartOk} explanation={explanationOk} warning={warningOk} turn={turnOk}");
            });
}
