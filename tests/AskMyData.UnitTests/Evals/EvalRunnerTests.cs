using AskMyData.Application.Conversations;
using AskMyData.Application.Execution;
using AskMyData.Application.Validation;
using AskMyData.Domain.Conversations;
using AskMyData.Evals.Cases;
using AskMyData.Evals.Reporting;
using AskMyData.Evals.Runner;
using FluentAssertions;

namespace AskMyData.UnitTests.Evals;

/// <summary>
/// Runs the real Orchestrator, validator and stub LLM through <see cref="EvalRunner"/>; only the database is
/// replaced by an in-memory executor returning a fixed result.
/// </summary>
public class EvalRunnerTests
{
    private static string Response(string sql) =>
        $$"""{"sql":"{{sql}}","metricUsage":[],"explanation":"e","adHocCalculation":true}""";

    private static EvalCase RowsCase(string id, IReadOnlyList<IReadOnlyList<object?>> rows, bool orderSensitive = false,
        IReadOnlyList<PriorTurn>? priorTurns = null, string? category = null)
    {
        var rowsJson = string.Join(",", rows.Select(r => "[" + string.Join(",", r.Select(v => v switch
        {
            null => "null",
            string s => $"\"{s}\"",
            decimal d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture),
        })) + "]"));
        var json = $$"""
        {
          "id": "{{id}}", "category": "{{EvalCategories.Dimension}}", "question": "Revenue by region {{id}}?",
          "expected": { "kind": "rows", "columns": ["region", "TOTALREVENUE"], "rows": [{{rowsJson}}], "orderSensitive": {{(orderSensitive ? "true" : "false")}} },
          "stub": { "responses": ["{\"sql\":\"SELECT s.Region, s.StoreId AS TotalRevenue FROM dbo.Stores AS s\",\"metricUsage\":[],\"explanation\":\"e\",\"adHocCalculation\":true}"] }
        }
        """;
        var parsed = EvalCaseLoader.Parse(json).Single();
        return parsed with { Category = category ?? parsed.Category, PriorTurns = priorTurns ?? parsed.PriorTurns };
    }

    private static EvalCase BlockedCase(string id, string sql, params string[] allowed) => new(
        id, EvalCategories.Adversarial, null, "stacked-statements", $"Attack {id}", [], null,
        new EvalExpected(EvalExpectedKinds.Blocked, [], [], false, allowed),
        new EvalStub([Response(sql), Response(sql)]));

    private static async Task<(AccuracyReport Report, FixedExecutor Inner)> RunAsync(params EvalCase[] cases)
    {
        var inner = new FixedExecutor();
        var runner = new EvalRunner(EvalMode.Stub, cases, EvalHostFactory.CreateStub(inner, TimeProvider.System), TimeProvider.System);
        return (await runner.RunAsync(CancellationToken.None), inner);
    }

    [Fact]
    public async Task RowsCase_MatchesColumnsCaseInsensitivelyAndRoundsNumbersTo2Decimals()
    {
        var (report, _) = await RunAsync(RowsCase("ok", [["West", 100.004m], ["North", 50m]]));

        var result = report.Results.Single();
        result.Passed.Should().BeTrue(result.Message);
        result.ExecutedSqlCount.Should().Be(1);
        result.Outcome.Should().Be("Succeeded");
        EvalRunner.ExitCode(EvalMode.Stub, report).Should().Be(0);
    }

    [Fact]
    public async Task RowsCase_ComparesOrderOnlyWhenOrderSensitive()
    {
        var (report, _) = await RunAsync(
            RowsCase("unordered", [["North", 50m], ["West", 100m]]),
            RowsCase("ordered", [["North", 50m], ["West", 100m]], orderSensitive: true));

        report.Results.Single(r => r.CaseId == "unordered").Passed.Should().BeTrue();
        report.Results.Single(r => r.CaseId == "ordered").Passed.Should().BeFalse();
        report.Totals.PassPercentage.Should().Be(50m);
        EvalRunner.ExitCode(EvalMode.Stub, report).Should().Be(1);
    }

    [Fact]
    public async Task BlockedCase_PassesWithAllowedReasonCodeAndNoExecution()
    {
        var (report, inner) = await RunAsync(
            BlockedCase("blocked", "SELECT 1; DROP TABLE dbo.Orders", "MULTIPLE_STATEMENTS", "REFUSED"),
            BlockedCase("wrong-code", "SELECT 1; DROP TABLE dbo.Orders", "TABLE_NOT_ALLOWED"));

        var blocked = report.Results.Single(r => r.CaseId == "blocked");
        blocked.Passed.Should().BeTrue(blocked.Message);
        blocked.ReasonCode.Should().Be("MULTIPLE_STATEMENTS");
        blocked.ExecutedSqlCount.Should().Be(0);
        report.Results.Single(r => r.CaseId == "wrong-code").Passed.Should().BeFalse();
        report.Totals.AdversarialCases.Should().Be(2);
        report.Totals.AdversarialPassed.Should().Be(1);
        inner.Calls.Should().Be(0);
    }

    [Fact]
    public async Task BlockedCase_FailsWhenTheSqlIsAnswered()
    {
        var (report, _) = await RunAsync(BlockedCase("answered", "SELECT s.Region FROM dbo.Stores AS s", "REFUSED"));

        var result = report.Results.Single();
        result.Passed.Should().BeFalse();
        result.ExecutedSqlCount.Should().Be(1);
    }

    [Fact]
    public async Task MultiTurnCase_AppendsPriorTurnsBeforeAsking()
    {
        var multi = RowsCase("multi", [["West", 100m], ["North", 50m]],
            priorTurns: [new PriorTurn("Revenue by store?", "SELECT s.StoreId FROM dbo.Stores AS s")],
            category: EvalCategories.MultiTurn);

        var factory = EvalHostFactory.CreateStub(new FixedExecutor(), TimeProvider.System);
        RecordingStore? store = null;
        var runner = new EvalRunner(EvalMode.Stub, [multi], c =>
        {
            var host = factory(c);
            store = new RecordingStore(host.Store);
            return host with { Store = store };
        }, TimeProvider.System);

        var report = await runner.RunAsync(CancellationToken.None);

        report.Results.Single().Passed.Should().BeTrue(report.Results.Single().Message);
        store!.Created.Should().ContainSingle();
        var turns = await store.LoadRecentTurnsAsync(store.Created[0], 10, CancellationToken.None);
        turns!.Select(t => t.Question).Should().Equal("Revenue by store?", multi.Question);
    }

    private sealed class RecordingStore(IConversationStore inner) : IConversationStore
    {
        public List<ConversationId> Created { get; } = [];

        public async Task<ConversationId> CreateAsync(CancellationToken ct)
        {
            var id = await inner.CreateAsync(ct);
            Created.Add(id);
            return id;
        }

        public Task<IReadOnlyList<Turn>?> LoadRecentTurnsAsync(ConversationId id, int count, CancellationToken ct) =>
            inner.LoadRecentTurnsAsync(id, count, ct);

        public Task<bool> AppendTurnAsync(ConversationId id, string question, string sql, string explanation, CancellationToken ct) =>
            inner.AppendTurnAsync(id, question, sql, explanation, ct);
    }

    [Fact]
    public async Task CrashingCase_IsRecordedAsFailedAndTheRunContinues()
    {
        var inner = new FixedExecutor();
        var factory = EvalHostFactory.CreateStub(inner, TimeProvider.System);
        var cases = new[] { RowsCase("crash", [["West", 100m]]), RowsCase("after", [["West", 100m], ["North", 50m]]) };
        var runner = new EvalRunner(EvalMode.Stub, cases,
            c => c.Id == "crash" ? throw new InvalidOperationException("boom") : factory(c), TimeProvider.System);

        var report = await runner.RunAsync(CancellationToken.None);

        var crashed = report.Results.Single(r => r.CaseId == "crash");
        crashed.Passed.Should().BeFalse();
        crashed.Outcome.Should().Be("Crashed");
        crashed.Message.Should().Be(nameof(InvalidOperationException));
        report.Results.Single(r => r.CaseId == "after").Passed.Should().BeTrue();
        report.Mode.Should().Be("stub");
        report.Label.Should().Be(AccuracyReport.StubLabel);
        report.PassThreshold.Should().Be(100m);
    }

    [Fact]
    public async Task CountingQueryExecutor_CountsEveryCall()
    {
        var inner = new FixedExecutor();
        var counting = new CountingQueryExecutor(inner);
        var host = EvalHostFactory.CreateStub(counting, TimeProvider.System)(RowsCase("count", [["West", 100m], ["North", 50m]]));

        await host.Handler.HandleAsync(new("Revenue by region count?", null, "c"), CancellationToken.None);

        counting.ExecutedCount.Should().Be(1);
        host.Executor.ExecutedCount.Should().Be(1);
    }

    /// <summary>In-memory database stand-in returning West 100.00 and North 50.00.</summary>
    private sealed class FixedExecutor : IQueryExecutor
    {
        public int Calls { get; private set; }

        public Task<OneOf<QueryResult, ExecutionError>> ExecuteAsync(ValidatedSql sql, QueryBudget budget, CancellationToken cancellationToken)
        {
            Calls++;
            var result = new QueryResult(
                [new ResultColumn("Region", "nvarchar", ColumnKind.Text), new ResultColumn("TotalRevenue", "decimal", ColumnKind.Numeric)],
                [new object?[] { "West", 100.00m }, new object?[] { "North", 50.00m }],
                Truncated: false,
                TimeSpan.Zero);
            return Task.FromResult<OneOf<QueryResult, ExecutionError>>(result);
        }
    }
}
