using AskMyData.Application.Ask;
using AskMyData.Application.Options;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Orchestrator;

/// <summary>Property 20: usage and cost are summed across every ILlmClient call, including the Repair_Call.</summary>
public class UsageAndCostProperties
{
    private sealed record Case(
        (int Prompt, int Completion)[] Calls,
        int InputPriceCents,
        int OutputPriceCents);

    private static readonly Gen<(int, int)> CallTokens =
        from p in Gen.Choose(0, 200_000)
        from c in Gen.Choose(0, 50_000)
        select (p, c);

    private static readonly Gen<Case> Cases =
        from count in Gen.Choose(1, 2)
        from calls in CallTokens.ArrayOf(count)
        from input in Gen.Choose(0, 5000)
        from output in Gen.Choose(0, 5000)
        select new Case(calls, input, output);

    // Feature: askmydata-conversational-analytics, Property 20: Usage and cost are summed across all calls
    // **Validates: Requirements 7.7, 7.8, 7.10**
    [Property(MaxTest = 100)]
    public Property Usage_and_cost_are_summed_across_all_calls() =>
        Prop.ForAll(
            Cases.ToArbitrary(),
            c =>
            {
                var inputPrice = c.InputPriceCents / 100m;
                var outputPrice = c.OutputPriceCents / 100m;
                var pricing = new LlmPricingOptions
                {
                    Models = new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["scripted"] = new ModelPrice { InputPerMillionUsd = inputPrice, OutputPerMillionUsd = outputPrice },
                    },
                };

                // Two calls means the first is rejected by the validator and the second is the Repair_Call.
                var steps = c.Calls
                    .Select((t, i) => (Step: i == 0 && c.Calls.Length == 2
                            ? LlmStep.Respond(LlmResponses.Structured(LlmResponses.InvalidSql))
                            : LlmStep.Respond(LlmResponses.Structured(LlmResponses.ValidSql)),
                        Tokens: t))
                    .Select(x => x.Step with { PromptTokens = x.Tokens.Prompt, CompletionTokens = x.Tokens.Completion })
                    .ToList();

                var harness = OrchestratorHarness.Create(steps, pricing: pricing);
                var outcome = harness.AskAsync().GetAwaiter().GetResult();

                var expectedPrompt = c.Calls.Sum(t => t.Prompt);
                var expectedCompletion = c.Calls.Sum(t => t.Completion);
                var expectedCost = c.Calls.Sum(t => ((t.Prompt * inputPrice) + (t.Completion * outputPrice)) / 1_000_000m);

                if (outcome is not AskSucceeded succeeded || harness.Audit.Records.Count != 1)
                {
                    return false;
                }

                var usage = succeeded.Usage;
                var record = harness.Audit.Records[0];

                return harness.Llm.CallCount == c.Calls.Length
                       && usage.PromptTokens == expectedPrompt
                       && usage.CompletionTokens == expectedCompletion
                       && usage.EstimatedCostUsd == expectedCost
                       && usage.ElapsedMs >= 0
                       && record.PromptTokens == expectedPrompt
                       && record.CompletionTokens == expectedCompletion
                       && record.EstimatedCostUsd == expectedCost
                       && record.RepairCallOccurred == (c.Calls.Length == 2);
            });
}
