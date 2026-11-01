using AskMyData.Application.Ask;
using AskMyData.Application.Execution;
using AskMyData.Application.Llm;
using AskMyData.Domain.Results;
using AskMyData.UnitTests.Generators;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Orchestrator;

/// <summary>Property 9: a refusal ends the request without validation, execution or repair.</summary>
public class RefusalProperties
{
    private enum Before
    {
        Nothing,
        InvalidSql,
        Garbage,
        ExecutionFailure,
    }

    private static readonly Gen<string> RefusalText =
        Gen.Elements("I cannot modify data.", "That data is not available.", "x", new string('r', 600));

    /// <summary>The refusal's sql field: empty, SQL the validator would pass, or SQL it would reject.</summary>
    private static readonly Gen<string> RefusalSql =
        Gen.Elements(string.Empty, LlmResponses.ValidSql, LlmResponses.InvalidSql, "DROP TABLE dbo.Orders");

    private static readonly Gen<(Before Before, string Text, string Sql)> Case =
        from before in Gen.Elements(Before.Nothing, Before.InvalidSql, Before.Garbage, Before.ExecutionFailure)
        from text in RefusalText
        from sql in RefusalSql
        select (before, text, sql);

    // Feature: askmydata-conversational-analytics, Property 9: Refusal ends the request without validation, execution or repair
    // **Validates: Requirements 2.11, 2.13, 5.12**
    [Property(MaxTest = 100)]
    public Property Refusal_ends_the_request_without_validation_execution_or_repair() =>
        Prop.ForAll(
            Case.ToArbitrary(),
            c =>
            {
                var refusal = LlmStep.Respond(LlmResponses.Structured(c.Sql, refusal: c.Text));
                IReadOnlyList<LlmStep> llm = c.Before switch
                {
                    Before.Nothing => [refusal],
                    Before.InvalidSql => [LlmStep.Respond(LlmResponses.Structured(LlmResponses.InvalidSql)), refusal],
                    Before.Garbage => [LlmStep.Respond(LlmResponses.Garbage), refusal],
                    _ => [LlmStep.Respond(LlmResponses.Structured(LlmResponses.ValidSql)), refusal],
                };

                IReadOnlyList<ExecutorStep> exec =
                [
                    ExecutorStep.Fail(ExecutionErrorCategory.ExecutionError),
                    ExecutorStep.Rows(Results.Sample(1, false, 1)),
                ];

                var harness = OrchestratorHarness.Create(llm, exec);
                var outcome = harness.AskAndAdvanceAsync(TimeSpan.FromSeconds(200)).GetAwaiter().GetResult();

                var refusalIndex = llm.Count - 1;
                var expectedValidated = c.Before is Before.InvalidSql or Before.ExecutionFailure ? 1 : 0;
                var expectedExecuted = c.Before == Before.ExecutionFailure ? 1 : 0;

                var refused = outcome is AskRefused r && r.RefusalText == c.Text;
                var noRepairAfter = harness.Llm.CallCount == refusalIndex + 1;
                var repairOnlyAtIndexOne = harness.Llm.Requests.Skip(1).All(q => q.CallType == LlmCallType.Repair);
                var noValidation = harness.Validator.Calls.Count == expectedValidated;
                var noExecution = harness.Executor.CallCount == expectedExecuted;

                return refused && noRepairAfter && repairOnlyAtIndexOne && noValidation && noExecution;
            });
}
