using System.Reflection;
using AskMyData.Application.Execution;
using AskMyData.Application.Validation;
using AskMyData.Evals.Cases;
using AskMyData.Evals.Reporting;
using AskMyData.Evals.Runner;
using AskMyData.Infrastructure.Llm;
using FluentAssertions;

namespace AskMyData.UnitTests.Evals;

/// <summary>Stub_Mode runner: label in report output, exit codes and the service graph (R6.6, R6.8, R6.10, R6.12).</summary>
public class StubModeRunnerTests
{
    private const string ExpectedLabel = "Pipeline and security check (stub LLM). This is not an LLM accuracy measurement.";

    private static EvalCase RowsCase(string id, string expectedRegion)
    {
        var json = $$"""
        {
          "id": "{{id}}", "category": "{{EvalCategories.Dimension}}", "question": "Revenue by region {{id}}?",
          "expected": { "kind": "rows", "columns": ["region", "TOTALREVENUE"], "rows": [["{{expectedRegion}}", 100], ["North", 50]], "orderSensitive": false },
          "stub": { "responses": ["{\"sql\":\"SELECT s.Region, s.StoreId AS TotalRevenue FROM dbo.Stores AS s\",\"metricUsage\":[],\"explanation\":\"e\",\"adHocCalculation\":true}"] }
        }
        """;
        return EvalCaseLoader.Parse(json).Single();
    }

    private static async Task<AccuracyReport> RunAsync(params EvalCase[] cases) =>
        await new EvalRunner(EvalMode.Stub, cases, EvalHostFactory.CreateStub(new FixedExecutor(), TimeProvider.System), TimeProvider.System)
            .RunAsync(CancellationToken.None);

    [Fact]
    public void StubLabel_HasTheRequiredText()
    {
        AccuracyReport.StubLabel.Should().Be(ExpectedLabel);
    }

    [Fact]
    public async Task ReportWriter_WritesStubLabelToMarkdownAndJson()
    {
        var report = await RunAsync(RowsCase("label", "West"));
        var dir = Path.Combine(Path.GetTempPath(), "askmydata-evals-" + Guid.NewGuid().ToString("N"));
        try
        {
            await new ReportWriter().WriteAsync(report, dir);

            (await File.ReadAllTextAsync(Path.Combine(dir, ReportWriter.MarkdownFileName))).Should().Contain(ExpectedLabel);
            (await File.ReadAllTextAsync(Path.Combine(dir, ReportWriter.JsonFileName))).Should().Contain(ExpectedLabel);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ExitCode_IsZeroAtOneHundredPercent()
    {
        var report = await RunAsync(RowsCase("a", "West"), RowsCase("b", "West"));

        report.Totals.PassPercentage.Should().Be(100m);
        EvalRunner.ExitCode(EvalMode.Stub, report).Should().Be(0);
    }

    [Fact]
    public async Task ExitCode_IsOneBelowThreshold()
    {
        var report = await RunAsync(RowsCase("a", "West"), RowsCase("b", "Wrong"));

        report.Totals.PassPercentage.Should().BeLessThan(EvalRunner.StubPassThreshold);
        EvalRunner.ExitCode(EvalMode.Stub, report).Should().Be(1);
    }

    [Fact]
    public async Task ExitCode_NeverGatesInLiveMode()
    {
        var report = await RunAsync(RowsCase("b", "Wrong"));

        EvalRunner.ExitCode(EvalMode.Live, report).Should().Be(0);
    }

    [Fact]
    public void StubContainer_HasOnlyStubLlmClientAndNoHttpClientOrGemini()
    {
        var host = EvalHostFactory.CreateStub(new FixedExecutor(), TimeProvider.System)(RowsCase("graph", "West"));

        var types = new List<Type>();
        Walk(host, types, new HashSet<object>(ReferenceEqualityComparer.Instance));

        types.Should().Contain(t => t == typeof(StubLlmClient));
        types.Should().NotContain(t => typeof(HttpClient).IsAssignableFrom(t) || typeof(HttpMessageHandler).IsAssignableFrom(t));
        types.Should().NotContain(t => t.Name == "GeminiLlmClient");
    }

    [Fact]
    public void EvalsAssembly_DefinesNoGeminiClient()
    {
        typeof(EvalHostFactory).Assembly.GetTypes().Should().NotContain(t => t.Name == "GeminiLlmClient");
    }

    private static void Walk(object? node, List<Type> types, HashSet<object> seen)
    {
        if (node is null || !seen.Add(node))
        {
            return;
        }

        var type = node.GetType();
        types.Add(type);

        if (node is System.Collections.IEnumerable items && node is not string)
        {
            foreach (var item in items)
            {
                Walk(item, types, seen);
            }
        }

        // Only descend into this solution's own object graph; framework types cannot hold the forbidden clients
        // other than directly, which the type check above already catches.
        if (type.Namespace?.StartsWith("AskMyData", StringComparison.Ordinal) != true && !type.Name.StartsWith("<", StringComparison.Ordinal))
        {
            return;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            foreach (var field in t.GetFields(flags | BindingFlags.DeclaredOnly))
            {
                if (!field.FieldType.IsPrimitive && field.FieldType != typeof(string))
                {
                    Walk(field.GetValue(node), types, seen);
                }
            }
        }
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
