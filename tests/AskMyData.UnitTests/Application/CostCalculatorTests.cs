using AskMyData.Application.Ask;
using AskMyData.Application.Llm;
using AskMyData.Application.Observability;
using AskMyData.Application.Options;
using AskMyData.UnitTests.Orchestrator;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AskMyData.UnitTests.Application;

/// <summary>Cost estimation, usage accumulation and the full audit record (R7.2, R7.3, R7.7, R7.8, R7.10).</summary>
public class CostCalculatorTests
{
    private static LlmPricingOptions Pricing(string model, decimal input, decimal output) => new()
    {
        Models = new(StringComparer.OrdinalIgnoreCase)
        {
            [model] = new ModelPrice { InputPerMillionUsd = input, OutputPerMillionUsd = output },
        },
    };

    [Fact]
    public void Estimate_uses_per_million_prices()
    {
        var calculator = new CostCalculator(Options.Create(Pricing("gemini-2.0-flash", 0.10m, 0.40m)));

        calculator.Estimate("gemini-2.0-flash", 1_000_000, 500_000).Should().Be(0.30m);
        calculator.Estimate("GEMINI-2.0-FLASH", 1000, 0).Should().Be(0.0001m);
    }

    [Theory]
    [InlineData("unknown-model")]
    [InlineData("")]
    public void Estimate_is_zero_when_model_is_not_priced(string model)
    {
        var calculator = new CostCalculator(Options.Create(Pricing("gemini-2.0-flash", 0.10m, 0.40m)));

        calculator.Estimate(model, 1000, 1000).Should().Be(0m);
    }

    [Fact]
    public void Accumulator_sums_tokens_and_cost_across_calls()
    {
        var usage = new UsageAccumulator();

        usage.Add(new LlmResponse("{}", 100, 50, "m"), 0.01m);
        usage.Add(new LlmResponse("{}", 30, 20, "m"), 0.02m);

        usage.Snapshot(7).Should().Be(new UsageSummary(130, 70, 0.03m, 7));
    }

    [Fact]
    public async Task Audit_record_contains_usage_cost_execution_and_provider_fields_after_repair()
    {
        var harness = OrchestratorHarness.Create(
            [
                LlmStep.Respond(LlmResponses.Structured(LlmResponses.InvalidSql)),
                LlmStep.Respond(LlmResponses.Structured(LlmResponses.ValidSql)),
            ],
            pricing: Pricing("scripted", 1_000_000m, 2_000_000m));

        var outcome = await harness.AskAsync();

        var succeeded = outcome.Should().BeOfType<AskSucceeded>().Subject;
        var record = harness.Audit.Records.Should().ContainSingle().Subject;
        record.PromptTokens.Should().Be(20);
        record.CompletionTokens.Should().Be(10);
        record.EstimatedCostUsd.Should().Be(40m);
        record.RepairCallOccurred.Should().BeTrue();
        record.RowCount.Should().Be(succeeded.Result.Rows.Count);
        record.ExecutionMs.Should().BeGreaterThanOrEqualTo(0);
        record.Provider.Should().Be("Scripted");
        record.Model.Should().Be("scripted");
        succeeded.Usage.EstimatedCostUsd.Should().Be(record.EstimatedCostUsd);
    }

    [Fact]
    public async Task Audit_record_without_repair_reports_no_repair_call()
    {
        var harness = OrchestratorHarness.Create([LlmStep.Respond(LlmResponses.Structured(LlmResponses.ValidSql))]);

        await harness.AskAsync();

        var record = harness.Audit.Records.Should().ContainSingle().Subject;
        record.RepairCallOccurred.Should().BeFalse();
        record.PromptTokens.Should().Be(10);
        record.EstimatedCostUsd.Should().Be(0m);
    }
}
