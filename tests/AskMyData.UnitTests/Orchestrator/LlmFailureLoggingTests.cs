using AskMyData.Application.Ask;
using AskMyData.Application.Llm;
using AskMyData.Application.Options;
using FluentAssertions;
using Xunit;

namespace AskMyData.UnitTests.Orchestrator;

/// <summary>R7.4: failed or timed-out ILlmClient calls log provider, call type, elapsed and Correlation_Id.</summary>
public class LlmFailureLoggingTests
{
    private static readonly TimeSpan LlmTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    private static readonly LlmStep InvalidSqlStep = LlmStep.Respond(LlmResponses.Structured(LlmResponses.InvalidSql));
    private static readonly LlmStep GarbageStep = LlmStep.Respond(LlmResponses.Garbage);

    private static AskOptions Options() => new() { LlmTimeout = LlmTimeout };

    private static async Task<LlmFailureEntry> RunAsync(IReadOnlyList<LlmStep> script, int expectedCalls)
    {
        var harness = OrchestratorHarness.Create(script, options: Options());

        var outcome = await harness.AskAndAdvanceAsync(TimeSpan.FromSeconds(120));

        outcome.Should().BeOfType<AskLlmUnavailable>();
        harness.Llm.CallCount.Should().Be(expectedCalls);
        var entry = harness.Audit.LlmFailureDetails.Should().ContainSingle().Subject;
        entry.Provider.Should().Be(harness.Llm.ProviderName);
        entry.CorrelationId.Should().Be("corr-1");
        return entry;
    }

    [Fact]
    public async Task Provider_error_on_initial_call_is_logged()
    {
        var entry = await RunAsync([LlmStep.Fail(TimeSpan.FromSeconds(3))], expectedCalls: 1);

        entry.CallType.Should().Be(LlmCallType.Initial);
        entry.TimedOut.Should().BeFalse();
        entry.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(3)).And.BeLessThan(TimeSpan.FromSeconds(3) + Tick);
    }

    [Fact]
    public async Task Llm_timeout_on_initial_call_is_logged()
    {
        var entry = await RunAsync([LlmStep.Respond(LlmResponses.Structured(LlmResponses.ValidSql), TimeSpan.FromSeconds(45))], expectedCalls: 1);

        entry.CallType.Should().Be(LlmCallType.Initial);
        entry.TimedOut.Should().BeTrue();
        entry.Elapsed.Should().BeGreaterThanOrEqualTo(LlmTimeout).And.BeLessThan(LlmTimeout + Tick);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_error_on_repair_call_is_logged(bool invalidOutput)
    {
        var first = invalidOutput ? GarbageStep : InvalidSqlStep;

        var entry = await RunAsync([first, LlmStep.Fail(TimeSpan.FromSeconds(2))], expectedCalls: 2);

        entry.CallType.Should().Be(LlmCallType.Repair);
        entry.TimedOut.Should().BeFalse();
        entry.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2)).And.BeLessThan(TimeSpan.FromSeconds(2) + Tick);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Llm_timeout_on_repair_call_is_logged(bool invalidOutput)
    {
        var first = invalidOutput ? GarbageStep : InvalidSqlStep;
        var slow = LlmStep.Respond(LlmResponses.Structured(LlmResponses.ValidSql), TimeSpan.FromSeconds(45));

        var entry = await RunAsync([first, slow], expectedCalls: 2);

        entry.CallType.Should().Be(LlmCallType.Repair);
        entry.TimedOut.Should().BeTrue();
        entry.Elapsed.Should().BeGreaterThanOrEqualTo(LlmTimeout).And.BeLessThan(LlmTimeout + Tick);
    }
}
