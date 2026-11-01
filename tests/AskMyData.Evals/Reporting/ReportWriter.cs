using System.Globalization;
using System.Text;

namespace AskMyData.Evals.Reporting;

public sealed class ReportWriter
{
    public const string JsonFileName = "report.json";
    public const string MarkdownFileName = "report.md";

    public async Task WriteAsync(AccuracyReport report, string outputDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, JsonFileName), ReportJson.Serialize(report), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, MarkdownFileName), RenderMarkdown(report), cancellationToken);
    }

    public static string RenderMarkdown(AccuracyReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.Append("# AskMyData Accuracy Report\n\n");
        sb.Append(ci, $"- **Mode:** {Cell(report.Mode)}\n");
        sb.Append(ci, $"- **Label:** {Cell(report.Label)}\n");
        sb.Append(ci, $"- **Timestamp:** {report.Timestamp.ToString("O", ci)}\n");
        if (report.Model is not null)
        {
            sb.Append(ci, $"- **Model:** {Cell(report.Model)}\n");
        }
        if (report.TokenUsage is { } usage)
        {
            sb.Append(ci, $"- **Token usage:** prompt {usage.PromptTokens}, completion {usage.CompletionTokens}, total {usage.TotalTokens}\n");
        }
        if (report.PassThreshold is { } threshold)
        {
            sb.Append(ci, $"- **Pass threshold:** {threshold.ToString("0.##", ci)}%\n");
        }

        var t = report.Totals;
        sb.Append("\n## Totals\n\n");
        sb.Append("| Metric | Value |\n|---|---|\n");
        sb.Append(ci, $"| Cases | {t.Cases} |\n");
        sb.Append(ci, $"| Passed | {t.Passed} |\n");
        sb.Append(ci, $"| Overall pass percentage | {t.PassPercentage.ToString("0.##", ci)}% |\n");
        sb.Append(ci, $"| Adversarial cases | {t.AdversarialCases} |\n");
        sb.Append(ci, $"| Adversarial passed | {t.AdversarialPassed} |\n");
        sb.Append(ci, $"| Adversarial pass rate | {t.AdversarialPassRate.ToString("0.##", ci)}% |\n");

        sb.Append("\n## Cases\n\n");
        sb.Append("| Case | Category | Result | Outcome | Reason | Executed SQL | Elapsed (ms) | Tokens | SQL |\n");
        sb.Append("|---|---|---|---|---|---|---|---|---|\n");
        foreach (var r in report.Results)
        {
            sb.Append(ci, $"| {Cell(r.CaseId)} | {Cell(r.Category)} | {(r.Passed ? "PASS" : "FAIL")} | {Cell(r.Outcome)} | {Cell(r.ReasonCode)} | {r.ExecutedSqlCount} | {r.ElapsedMs} | {(r.Tokens?.ToString(ci) ?? "")} | {Cell(r.Sql)} |\n");
        }

        return sb.ToString();
    }

    private static string Cell(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("\\", "\\\\", StringComparison.Ordinal)
                   .Replace("|", "\\|", StringComparison.Ordinal)
                   .Replace("\r", " ", StringComparison.Ordinal)
                   .Replace("\n", " ", StringComparison.Ordinal);
}
