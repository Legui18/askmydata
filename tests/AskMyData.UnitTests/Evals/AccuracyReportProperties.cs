using System.Globalization;
using AskMyData.Evals.Reporting;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Evals;

public sealed class AccuracyReportProperties
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    private static Gen<string> Word(string prefix) =>
        from n in Gen.Choose(0, 99999)
        select prefix + n.ToString(Ci);

    private static Gen<decimal> Percent() =>
        from n in Gen.Choose(0, 10000)
        select n / 100m;

    private static readonly Gen<CaseResult> CaseResultGen =
        from category in Gen.Elements("metric", "multi-turn", "adversarial")
        from passed in Gen.Elements(true, false)
        from outcome in Gen.Elements("success", "blocked", "failure")
        from reason in Gen.Elements<string?>(null, "BLOCKED_NOT_ALLOWLISTED", "BLOCKED_WRITE")
        from executed in Gen.Choose(0, 3)
        from sql in Gen.Elements("SELECT 1", "SELECT a | b", "")
        from message in Gen.Elements<string?>(null, "ok", "needs review")
        from elapsed in Gen.Choose(0, 100000)
        from tokens in Gen.Elements<long?>(null, 0L, 1234L)
        select new CaseResult("case", category, passed, outcome, reason, executed, sql, message, elapsed, tokens);

    private static Gen<AccuracyReport> ReportGen() =>
        from live in Gen.Elements(true, false)
        from caseCount in Gen.Choose(0, 12)
        from raw in Gen.ArrayOf(CaseResultGen, caseCount)
        let results = raw.Select((r, i) => r with { CaseId = $"case-{i}" }).ToArray()
        from ticks in Gen.Choose(0, 1_000_000_000)
        from offsetHours in Gen.Choose(-12, 14)
        from model in Word("gemini-")
        from prompt in Gen.Choose(0, 1_000_000)
        from completion in Gen.Choose(0, 1_000_000)
        from overall in Percent()
        from adversarial in Percent()
        select Build(live, results.ToList(), ticks, offsetHours, model, prompt, completion, overall, adversarial);

    private static AccuracyReport Build(
        bool live, List<CaseResult> results, int ticks, int offsetHours, string model,
        long prompt, long completion, decimal overall, decimal adversarial)
    {
        var timestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.FromHours(offsetHours))
            .AddTicks(ticks);
        var adversarialResults = results.Where(r => r.Category == "adversarial").ToList();
        var totals = new ReportTotals(
            results.Count,
            results.Count(r => r.Passed),
            overall,
            adversarialResults.Count,
            adversarialResults.Count(r => r.Passed),
            adversarial);

        return live
            ? new AccuracyReport(
                AccuracyReport.CurrentSchemaVersion, "live", AccuracyReport.LiveLabel, timestamp, model,
                new TokenUsage(prompt, completion, prompt + completion), null, totals, results)
            : new AccuracyReport(
                AccuracyReport.CurrentSchemaVersion, "stub", AccuracyReport.StubLabel, timestamp, null,
                null, 100m, totals, results);
    }

    private static Arbitrary<AccuracyReport> Reports() => ReportGen().ToArbitrary();

    // Feature: askmydata-conversational-analytics, Property 26: Accuracy_Report JSON round trip and Markdown content
    /// <summary>**Validates: Requirements 6.11, 6.18**</summary>
    [Property(MaxTest = 100)]
    public Property Report_json_round_trips()
    {
        return Prop.ForAll(Reports(), report =>
        {
            var json = ReportJson.Serialize(report);
            var back = ReportJson.Deserialize(json);

            ReportJson.Serialize(back).Should().Be(json);
            back.Should().BeEquivalentTo(report, o => o.ComparingRecordsByMembers().WithStrictOrdering());
            back.Timestamp.Offset.Should().Be(report.Timestamp.Offset);
        });
    }

    // Feature: askmydata-conversational-analytics, Property 26: Accuracy_Report JSON round trip and Markdown content
    /// <summary>**Validates: Requirements 6.11, 6.18**</summary>
    [Property(MaxTest = 100)]
    public Property Report_markdown_contains_required_content()
    {
        return Prop.ForAll(Reports(), report =>
        {
            var md = ReportWriter.RenderMarkdown(report);

            md.Should().Contain($"**Mode:** {report.Mode}");
            md.Should().Contain($"**Label:** {report.Label}");
            md.Should().Contain($"**Timestamp:** {report.Timestamp.ToString("O", Ci)}");
            foreach (var r in report.Results)
            {
                md.Should().Contain($"| {r.CaseId} | {r.Category} | {(r.Passed ? "PASS" : "FAIL")} |");
            }

            md.Should().Contain($"| Overall pass percentage | {report.Totals.PassPercentage.ToString("0.##", Ci)}% |");
            md.Should().Contain($"| Adversarial pass rate | {report.Totals.AdversarialPassRate.ToString("0.##", Ci)}% |");

            if (report.Mode == "stub")
            {
                md.Should().Contain("Pipeline and security check");
                md.Should().Contain("not an LLM accuracy measurement");
            }
            else
            {
                md.Should().Contain(report.Model!);
                md.Should().Contain(
                    $"prompt {report.TokenUsage!.PromptTokens}, completion {report.TokenUsage.CompletionTokens}, total {report.TokenUsage.TotalTokens}");
            }
        });
    }
}
