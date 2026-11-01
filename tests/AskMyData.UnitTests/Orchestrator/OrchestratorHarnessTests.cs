using AskMyData.Application.Ask;
using AskMyData.Application.Observability;
using AskMyData.UnitTests.Generators;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Orchestrator;

/// <summary>Smoke tests for the orchestrator test doubles, generators and harness (R2.3, R2.8, R2.15).</summary>
public class OrchestratorHarnessTests
{
    [Fact]
    public async Task Valid_script_succeeds_and_doubles_record_calls()
    {
        var harness = OrchestratorHarness.Create([LlmStep.Respond(LlmResponses.Structured(LlmResponses.ValidSql))]);

        var outcome = await harness.AskAsync();

        outcome.Should().BeOfType<AskSucceeded>();
        harness.Llm.Requests.Should().ContainSingle();
        harness.Executor.Executed.Select(e => e.Text).Should().Equal(LlmResponses.ValidSql);
        harness.Validator.PassedSql.Should().Equal(LlmResponses.ValidSql);
        harness.Audit.Records.Should().ContainSingle().Which.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task Invalid_sql_is_repaired_once_and_never_reaches_executor()
    {
        var invalid = LlmStep.Respond(LlmResponses.Structured(LlmResponses.InvalidSql));
        var harness = OrchestratorHarness.Create([invalid]);

        var outcome = await harness.AskAsync();

        outcome.Should().BeOfType<AskValidationFailed>();
        harness.Llm.CallCount.Should().Be(2);
        harness.Executor.CallCount.Should().Be(0);
        harness.Validator.PassedSql.Should().BeEmpty();
        harness.Audit.Records.Single().ValidationOutcome.Should().Be(AuditValidationOutcome.Rejected);
    }

    [Fact]
    public async Task Slow_llm_hits_llm_timeout_on_the_fake_clock()
    {
        var slow = LlmStep.Respond(LlmResponses.Structured(LlmResponses.ValidSql), TimeSpan.FromSeconds(45));
        var harness = OrchestratorHarness.Create([slow]);

        var outcome = await harness.AskAndAdvanceAsync(TimeSpan.FromSeconds(120));

        outcome.Should().BeOfType<AskLlmUnavailable>().Which.TimedOut.Should().BeTrue();
        harness.Llm.Tokens.Should().ContainSingle().Which.IsCancellationRequested.Should().BeTrue();
    }

    // Generator sanity: scripts are non-empty and bounded.
    [Property(MaxTest = 100)]
    public Property Generated_scripts_are_well_formed() =>
        Prop.ForAll(
            LlmScriptGen.WithDelays(DelayScriptGen.Delay).ToArbitrary(),
            ExecutorScriptGen.WithDelays(DelayScriptGen.Delay).ToArbitrary(),
            (llm, exec) => llm.Count is >= 1 and <= 2
                           && exec.Count is >= 1 and <= 2
                           && exec.All(s => (s.Result is null) != (s.Error is null))
                           && llm.All(s => s.Delay >= TimeSpan.Zero));
}
