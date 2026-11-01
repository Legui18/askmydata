using AskMyData.UnitTests.Generators;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Orchestrator;

/// <summary>Property 7: only validator-approved SQL is ever handed to IQueryExecutor.</summary>
public class ValidatorGatingProperties
{
    private static readonly string[] AdversarialSql =
    [
        "SELECT COUNT(*) AS N FROM dbo.Orders AS o; DROP TABLE dbo.Orders",
        "SELECT COUNT(*) AS N FROM dbo.Orders AS o; SELECT COUNT(*) AS N FROM dbo.Orders AS o",
        "SELECT COUNT(*) AS N FROM dbo.Orders AS o -- trailing comment",
        "SELECT COUNT(*) AS N /* hidden */ FROM dbo.Orders AS o",
        "SELECT COUNT(*) AS N FROM sys.objects AS o",
        "SELECT COUNT(*) AS N FROM dbo.Secrets AS s",
        "SELECT COUNT(*) AS N FROM dbo.Orders AS o UNION SELECT COUNT(*) FROM sys.sql_logins",
        "DELETE FROM dbo.Orders",
        "EXEC xp_cmdshell 'dir'",
        "SELECT * FROM dbo.Orders AS o",
    ];

    private static Gen<LlmStep> AdversarialStep { get; } =
        Gen.Elements(AdversarialSql).Select(sql => LlmStep.Respond(LlmResponses.Structured(sql)));

    private static Gen<IReadOnlyList<LlmStep>> AdversarialOrMixedScript { get; } =
        Gen.OneOf(
            LlmScriptGen.Script,
            from count in Gen.Choose(1, 2)
            from steps in GenHelpers.Sequence(Enumerable.Range(0, count).Select(_ =>
                Gen.Frequency((3, AdversarialStep), (1, LlmScriptGen.ValidStep))))
            select steps);

    // Feature: askmydata-conversational-analytics, Property 7: No SQL reaches IQueryExecutor without a validator pass
    // **Validates: Requirements 2.3, 2.4, 6.9, 10.5**
    [Property(MaxTest = 100)]
    public Property Every_executed_sql_was_recorded_as_a_validator_pass() =>
        Prop.ForAll(
            AdversarialOrMixedScript.ToArbitrary(),
            ExecutorScriptGen.Script.ToArbitrary(),
            (llm, exec) =>
            {
                var harness = OrchestratorHarness.Create(llm, exec);
                harness.AskAndAdvanceAsync(TimeSpan.FromSeconds(200)).GetAwaiter().GetResult();

                var passed = harness.Validator.PassedSql.ToList();
                var executed = harness.Executor.Executed.Select(e => e.Text).ToList();

                // Each execution consumes one validator pass of identical text (multiset containment).
                var remaining = new List<string>(passed);
                var allPassed = executed.All(text => remaining.Remove(text));

                var failedOnly = harness.Validator.Calls
                    .Where(c => !c.Result.Passed)
                    .Select(c => c.Sql)
                    .Except(passed)
                    .ToHashSet();
                var neverFailedSql = executed.All(text => !failedOnly.Contains(text));

                var adversarialBlocked = executed.All(text => !AdversarialSql.Contains(text));

                return allPassed && neverFailedSql && adversarialBlocked
                       && harness.Executor.CallCount <= passed.Count;
            });
}
