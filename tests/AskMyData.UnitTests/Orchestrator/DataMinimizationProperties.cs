using System.Text.Json;
using AskMyData.Application.Llm;
using AskMyData.Application.Prompting;
using AskMyData.Domain.Semantic;
using AskMyData.UnitTests.Generators;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Orchestrator;

/// <summary>Property 17: no LlmRequest carries result data or database error text; repair info is minimal.</summary>
public class DataMinimizationProperties
{
    private static readonly string[] NonValidatorCodes = ["GENERATION_INVALID", "TIMEOUT", "EXECUTION_ERROR"];

    // Feature: askmydata-conversational-analytics, Property 17: Data minimization in every LlmRequest
    // **Validates: Requirements 9.1, 9.2, 9.3, 9.4**
    [Property(MaxTest = 100)]
    public Property Llm_requests_contain_only_prompt_untrusted_section_and_repair_info() =>
        Prop.ForAll(
            LlmScriptGen.Script.ToArbitrary(),
            ExecutorScriptGen.Script.ToArbitrary(),
            (llm, exec) =>
            {
                var harness = OrchestratorHarness.Create(llm, exec);
                _ = harness.AskAndAdvanceAsync(TimeSpan.FromSeconds(200)).GetAwaiter().GetResult();

                var layer = SemanticLayerCatalog.Create();
                var requests = harness.Llm.Requests;
                var validatorCodes = harness.Validator.Calls
                    .Where(c => !c.Result.Passed)
                    .Select(c => c.Result.ReasonCode)
                    .ToHashSet();
                var validatorMetrics = harness.Validator.Calls
                    .Select(c => c.Result.MetricName)
                    .Where(m => m is not null)
                    .ToHashSet();
                var failingSqls = new[] { LlmResponses.ValidSql, LlmResponses.InvalidSql };

                foreach (var r in requests)
                {
                    // (d) no executor row marker anywhere in the request.
                    if (r.SystemPrompt.Contains(Results.MarkerPrefix, StringComparison.Ordinal)
                        || r.UntrustedSection.Contains(Results.MarkerPrefix, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    // (a)/(c) system prompt is exactly the rendered template: rules, metrics, allowlist schema,
                    // output schema and, on repair, the repair block. Nothing else.
                    if (r.SystemPrompt != SystemPromptTemplate.Render(layer, r.Repair))
                    {
                        return false;
                    }

                    // (c) schema for every Allowlist object is present (names and columns only).
                    foreach (var entry in layer.Allowlist)
                    {
                        if (!r.SystemPrompt.Contains($"{entry.Schema}.{entry.Name}", StringComparison.Ordinal)
                            || entry.Columns.Any(c => !r.SystemPrompt.Contains(c.Name, StringComparison.Ordinal)))
                        {
                            return false;
                        }
                    }

                    using var doc = ParseUntrusted(r.UntrustedSection);
                    var hasFailing = doc.RootElement.TryGetProperty("failingSql", out var failing);

                    if (r.CallType == LlmCallType.Initial)
                    {
                        if (r.Repair is not null || hasFailing)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        // (a)/(b) repair info is only a code/category and a Metric name.
                        var repair = r.Repair;
                        if (repair is null)
                        {
                            return false;
                        }

                        var codeOk = NonValidatorCodes.Contains(repair.ReasonCodeOrCategory)
                                     || validatorCodes.Contains(repair.ReasonCodeOrCategory);
                        var metricOk = repair.MetricName is null || validatorMetrics.Contains(repair.MetricName);
                        if (!codeOk || !metricOk)
                        {
                            return false;
                        }

                        // The failing SQL only ever appears inside the Untrusted_Section.
                        if (hasFailing)
                        {
                            var sql = failing.GetString();
                            if (!failingSqls.Contains(sql)
                                || r.SystemPrompt.Contains(sql!, StringComparison.Ordinal))
                            {
                                return false;
                            }
                        }
                    }
                }

                return true;
            });

    private static JsonDocument ParseUntrusted(string section)
    {
        var start = section.IndexOf('\n') + 1;
        var end = section.LastIndexOf('\n');
        return JsonDocument.Parse(section[start..end]);
    }
}
