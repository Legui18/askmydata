using AskMyData.Application.Execution;
using AskMyData.Application.Validation;
using AskMyData.Domain.Semantic;
using AskMyData.Evals.Runner;
using AskMyData.Infrastructure.Validation;
using FluentAssertions;

namespace AskMyData.UnitTests.Evals;

/// <summary>Real ScriptDomSqlValidator and file handling; only the database is an in-memory executor.</summary>
public sealed class ExpectedGeneratorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "expected-" + Guid.NewGuid().ToString("N"));

    public ExpectedGeneratorTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string RowsCaseJson = """
        {
          "id": "c1", "category": "dimension", "question": "Revenue by region?",
          "referenceSql": "SELECT s.Region, s.StoreId AS TotalRevenue FROM dbo.Stores AS s",
          "expected": { "kind": "rows", "columns": ["x"], "rows": [] },
          "stub": { "responses": ["{}"] }
        }
        """;

    private const string AdversarialJson = """
        {
          "id": "a1", "category": "adversarial", "attack": "stacked-statements", "question": "q",
          "referenceSql": "DROP TABLE dbo.Orders",
          "expected": { "kind": "blocked", "allowedReasonCodes": ["MULTIPLE_STATEMENTS"] },
          "stub": { "responses": ["{}"] }
        }
        """;

    private static ExpectedGenerator Generator() =>
        new(new FixedExecutor(), new ScriptDomSqlValidator(SemanticLayerCatalog.Create()));

    [Fact]
    public async Task Generate_WritesRoundedRowsThenCheckReportsNoDiff()
    {
        var file = Path.Combine(_dir, "c1.json");
        await File.WriteAllTextAsync(file, RowsCaseJson);
        await File.WriteAllTextAsync(Path.Combine(_dir, "a1.json"), AdversarialJson);

        var first = await Generator().GenerateAsync(_dir, check: true, CancellationToken.None);
        first.Errors.Should().BeEmpty();
        first.Changed.Should().ContainSingle().Which.Should().StartWith("c1.json");
        first.Skipped.Should().Be(1);
        (await File.ReadAllTextAsync(file)).Should().Be(RowsCaseJson, "check must not write");

        var written = await Generator().GenerateAsync(_dir, check: false, CancellationToken.None);
        written.Changed.Should().ContainSingle();
        var text = await File.ReadAllTextAsync(file);
        text.Should().Contain("\"West\"").And.Contain("100.00").And.Contain("12.35");

        var second = await Generator().GenerateAsync(_dir, check: true, CancellationToken.None);
        second.Changed.Should().BeEmpty();
        second.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Generate_FailsWhenReferenceSqlIsRejectedAndLeavesFileUntouched()
    {
        var bad = RowsCaseJson.Replace("SELECT s.Region, s.StoreId AS TotalRevenue FROM dbo.Stores AS s", "SELECT e.Salary FROM dbo.Employees AS e");
        var file = Path.Combine(_dir, "c1.json");
        await File.WriteAllTextAsync(file, bad);

        var result = await Generator().GenerateAsync(_dir, check: false, CancellationToken.None);

        result.Errors.Should().ContainSingle().Which.Should().Contain("rejected by validator");
        (await File.ReadAllTextAsync(file)).Should().Be(bad);
    }

    private sealed class FixedExecutor : IQueryExecutor
    {
        public Task<OneOf<QueryResult, ExecutionError>> ExecuteAsync(ValidatedSql sql, QueryBudget budget, CancellationToken cancellationToken)
        {
            var result = new QueryResult(
                [new ResultColumn("Region", "nvarchar", ColumnKind.Text), new ResultColumn("TotalRevenue", "decimal", ColumnKind.Numeric)],
                [new object?[] { "West", 100m }, new object?[] { "North", 12.345m }],
                Truncated: false,
                TimeSpan.Zero);
            return Task.FromResult<OneOf<QueryResult, ExecutionError>>(result);
        }
    }
}
