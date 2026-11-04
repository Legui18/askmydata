using System.Diagnostics;
using AskMyData.Application.Execution;
using AskMyData.Application.Validation;
using AskMyData.Domain.Results;
using AskMyData.Infrastructure.Execution;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskMyData.IntegrationTests.Database;

/// <summary>
/// SqlServerQueryExecutor and Read_Only_User behaviour against a real SQL Server 2022 container
/// (Requirements 4.1, 4.3, 4.5, 4.8, 4.10, 14.4). Requires Docker.
/// </summary>
public sealed class SqlServerQueryExecutorTests : IClassFixture<DatasetScriptTests.LoadedDataset>
{
    private static readonly QueryBudget DefaultBudget = new(TimeSpan.FromSeconds(15));

    private readonly DatasetScriptTests.LoadedDataset _dataset;

    public SqlServerQueryExecutorTests(DatasetScriptTests.LoadedDataset dataset) => _dataset = dataset;

    [Theory]
    [InlineData("Customers")]
    [InlineData("Products")]
    [InlineData("Stores")]
    [InlineData("Orders")]
    [InlineData("OrderLines")]
    [InlineData("Calendar")]
    public async Task Select_on_allowlist_objects_succeeds(string table)
    {
        var (executor, _) = CreateExecutor();

        var outcome = await executor.ExecuteAsync(Sql($"SELECT TOP (5) 1 AS One FROM dbo.{table}"), DefaultBudget, default);

        outcome.TryGetFirst(out var result).Should().BeTrue();
        result.Rows.Should().HaveCount(5);
        result.Truncated.Should().BeFalse();
    }

    [Theory]
    [InlineData("SELECT TOP (1) 1 AS One FROM dbo.Employees")]
    [InlineData("INSERT INTO dbo.Stores DEFAULT VALUES")]
    [InlineData("UPDATE dbo.Stores SET Region = 'x'")]
    [InlineData("DELETE FROM dbo.Stores")]
    [InlineData("CREATE TABLE dbo.Evil (Id int)")]
    [InlineData("DROP TABLE dbo.Stores")]
    public async Task Non_select_and_non_allowlist_statements_fail_with_permission_errors(string sql)
    {
        var (executor, log) = CreateExecutor();

        var outcome = await executor.ExecuteAsync(Sql(sql), DefaultBudget, default);

        outcome.TryGetSecond(out var error).Should().BeTrue();
        error.Category.Should().Be(ExecutionErrorCategory.ExecutionError);
        log.Messages.Should().ContainSingle().Which.Should().ContainEquivalentOf("permission");
    }

    [Fact]
    public async Task Failed_writes_leave_the_data_unchanged()
    {
        var before = await _dataset.Server.GetTableChecksumsAsync();
        var (executor, _) = CreateExecutor();

        await executor.ExecuteAsync(Sql("DELETE FROM dbo.Stores"), DefaultBudget, default);

        (await _dataset.Server.GetTableChecksumsAsync()).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Slow_cross_join_with_one_second_budget_returns_timeout()
    {
        var (executor, _) = CreateExecutor();

        var outcome = await executor.ExecuteAsync(SlowQuery(), new QueryBudget(TimeSpan.FromSeconds(1)), default);

        outcome.TryGetSecond(out var error).Should().BeTrue();
        error.Category.Should().Be(ExecutionErrorCategory.Timeout);
    }

    [Fact]
    public async Task Divide_by_zero_returns_execution_error_and_logs_details()
    {
        var (executor, log) = CreateExecutor();

        var outcome = await executor.ExecuteAsync(Sql("SELECT 1 / 0 AS Boom"), DefaultBudget, default);

        outcome.TryGetSecond(out var error).Should().BeTrue();
        error.Category.Should().Be(ExecutionErrorCategory.ExecutionError);
        var entry = log.Messages.Should().ContainSingle().Subject;
        entry.Should().Contain("Number=8134").And.Contain("Severity=16").And.Contain("Divide by zero");
    }

    [Fact]
    public async Task Cancelled_token_stops_the_running_command()
    {
        var (executor, _) = CreateExecutor();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var watch = Stopwatch.StartNew();

        var act = () => executor.ExecuteAsync(SlowQuery(), DefaultBudget, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Query_with_1500_rows_and_row_limit_1000_returns_1000_rows_and_truncated()
    {
        var (executor, _) = CreateExecutor(rowLimit: 1000);

        var outcome = await executor.ExecuteAsync(Sql("SELECT TOP (1500) 1 AS N FROM dbo.Orders"), DefaultBudget, default);

        outcome.TryGetFirst(out var result).Should().BeTrue();
        result.Rows.Should().HaveCount(1000);
        result.Truncated.Should().BeTrue();
    }

    private static ValidatedSql Sql(string text) => new(text);

    // Three-way cross join: far too large to finish within any test budget.
    private static ValidatedSql SlowQuery() => Sql(
        "SELECT COUNT_BIG(*) AS N FROM dbo.OrderLines a CROSS JOIN dbo.OrderLines b CROSS JOIN dbo.OrderLines c");

    private (SqlServerQueryExecutor Executor, CapturingLogger Log) CreateExecutor(int rowLimit = 1000)
    {
        var log = new CapturingLogger();
        var options = Options.Create(new QueryExecutorOptions
        {
            ConnectionString = _dataset.Server.ReadOnlyConnectionString,
            RowLimit = rowLimit,
        });
        return (new SqlServerQueryExecutor(options, log, TimeProvider.System), log);
    }

    private sealed class CapturingLogger : ILogger<SqlServerQueryExecutor>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }
}
