using AskMyData.Application.Execution;
using AskMyData.Application.Llm;
using AskMyData.Application.Validation;
using AskMyData.Evals.Cases;
using AskMyData.Evals.Reporting;
using AskMyData.Evals.Runner;
using FluentAssertions;

namespace AskMyData.UnitTests.Evals;

/// <summary>Live_Mode reporting: label, model, token usage, null threshold and exit code (R6.14, R6.16).</summary>
public class LiveModeRunnerTests
{
    private const string ModelName = "gemini-test-model";
    private const string GeneratedJson =
        "{\"sql\":\"SELECT s.Region, s.StoreId AS TotalRevenue FROM dbo.Stores AS s\",\"metricUsage\":[],\"explanation\":\"e\",\"adHocCalculation\":true}";

    // The case loader requires every stub response to be valid JSON, even though Live_Mode never reads it.
    private const string UnusedStubResponse =
        "{\\\"sql\\\":\\\"SELECT 1\\\",\\\"metricUsage\\\":[],\\\"explanation\\\":\\\"e\\\",\\\"adHocCalculation\\\":true}";

    private static EvalCase RowsCase(string id, string expectedRegion)
    {
        var json = $$"""
        {
          "id": "{{id}}", "category": "{{EvalCategories.Dimension}}", "question": "Revenue by region {{id}}?",
          "expected": { "kind": "rows", "columns": ["region", "TOTALREVENUE"], "rows": [["{{expectedRegion}}", 100], ["North", 50]], "orderSensitive": false },
          "stub": { "responses": ["{{UnusedStubResponse}}"] }
        }
        """;
        return EvalCaseLoader.Parse(json).Single();
    }

    private static async Task<AccuracyReport> RunLiveAsync(params EvalCase[] cases)
    {
        var recorder = new LlmUsageRecorder("configured-model");
        ILlmClient llm = new RecordingLlmClient(new FakeLlm(), recorder);
        var factory = EvalHostFactory.CreateLive(new FixedExecutor(), TimeProvider.System, llm);
        return await new EvalRunner(EvalMode.Live, cases, factory, TimeProvider.System, recorder).RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LiveReport_HasLabelModelTokenUsageAndNoThreshold()
    {
        var report = await RunLiveAsync(RowsCase("a", "West"), RowsCase("b", "West"));

        report.Mode.Should().Be("live");
        report.Label.Should().Be("LLM accuracy");
        report.PassThreshold.Should().BeNull();
        report.Model.Should().Be(ModelName);
        report.TokenUsage.Should().Be(new TokenUsage(200, 60, 260));
    }

    [Fact]
    public async Task LiveReport_WritesModelTokensAndLabelToJsonAndMarkdown()
    {
        var report = await RunLiveAsync(RowsCase("a", "West"));
        var dir = Path.Combine(Path.GetTempPath(), "askmydata-evals-" + Guid.NewGuid().ToString("N"));
        try
        {
            await new ReportWriter().WriteAsync(report, dir);

            var json = ReportJson.Deserialize(await File.ReadAllTextAsync(Path.Combine(dir, ReportWriter.JsonFileName)));
            json.Model.Should().Be(ModelName);
            json.TokenUsage.Should().Be(new TokenUsage(100, 30, 130));
            json.Label.Should().Be("LLM accuracy");

            var markdown = await File.ReadAllTextAsync(Path.Combine(dir, ReportWriter.MarkdownFileName));
            markdown.Should().Contain("LLM accuracy").And.Contain(ModelName).And.Contain("total 130");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Runner_ExitsZeroEvenWhenEveryCaseFails()
    {
        var report = await RunLiveAsync(RowsCase("a", "Wrong"), RowsCase("b", "Wrong"));

        report.Totals.PassPercentage.Should().Be(0m);
        EvalRunner.ExitCode(EvalMode.Live, report).Should().Be(0);
    }

    private sealed class FakeLlm : ILlmClient
    {
        public string ProviderName => "Fake";

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new LlmResponse(GeneratedJson, 100, 30, ModelName));
    }

    private sealed class FixedExecutor : IQueryExecutor
    {
        public Task<OneOf<QueryResult, ExecutionError>> ExecuteAsync(ValidatedSql sql, QueryBudget budget, CancellationToken cancellationToken)
        {
            var result = new QueryResult(
                [new ResultColumn("Region", "nvarchar", ColumnKind.Text), new ResultColumn("TotalRevenue", "decimal", ColumnKind.Numeric)],
                [new object?[] { "West", 100.00m }, new object?[] { "North", 50.00m }],
                Truncated: false,
                TimeSpan.Zero);
            return Task.FromResult<OneOf<QueryResult, ExecutionError>>(result);
        }
    }
}