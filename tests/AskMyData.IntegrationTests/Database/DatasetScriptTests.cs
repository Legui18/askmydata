using System.Text.RegularExpressions;
using AskMyData.Domain.Semantic;
using FluentAssertions;

namespace AskMyData.IntegrationTests.Database;

/// <summary>
/// Dataset_Script behaviour against a real SQL Server 2022 container
/// (Requirements 1.6, 14.1, 14.3, 14.5, 14.6). Requires Docker.
/// </summary>
public sealed partial class DatasetScriptTests : IClassFixture<DatasetScriptTests.LoadedDataset>
{
    private readonly LoadedDataset _dataset;

    public DatasetScriptTests(LoadedDataset dataset) => _dataset = dataset;

    [Fact]
    public async Task Running_the_script_a_second_time_leaves_table_checksums_unchanged()
    {
        var before = await _dataset.Server.GetTableChecksumsAsync();

        var second = await _dataset.Server.RunScriptAsync();
        var after = await _dataset.Server.GetTableChecksumsAsync();

        second.ExitCode.Should().Be(0, second.Output);
        after.Should().BeEquivalentTo(before);
        before["Orders"].Should().NotStartWith("0:");
    }

    [Fact]
    public async Task Two_fresh_containers_produce_identical_table_checksums()
    {
        await using var other = await StartAsync();
        var run = await other.RunScriptAsync();
        run.ExitCode.Should().Be(0, run.Output);

        var first = await _dataset.Server.GetTableChecksumsAsync();
        var second = await other.GetTableChecksumsAsync();

        second.Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task A_failing_step_exits_non_zero_and_prints_the_step_label()
    {
        await using var server = await StartAsync();
        await server.ExecuteAsync("master", "CREATE DATABASE AskMyData;");
        // A view named like a table makes the OBJECT_ID(..., 'U') guard pass and CREATE TABLE fail.
        await server.ExecuteAsync("AskMyData", "CREATE VIEW dbo.Stores AS SELECT 1 AS StoreId;");

        var run = await server.RunScriptAsync();

        run.ExitCode.Should().NotBe(0);
        run.Output.Should().Contain("FAILED at step: create table dbo.Stores");
    }

    [Fact]
    public async Task Every_metric_and_allowlist_column_exists_in_information_schema()
    {
        var layer = SemanticLayerCatalog.Create();
        var columns = await _dataset.Server.GetColumnsAsync();

        var allowlisted = layer.Allowlist
            .SelectMany(e => e.Columns.Select(c => $"{e.Schema}.{e.Name}.{c.Name}"))
            .ToList();
        allowlisted.Should().NotBeEmpty();
        columns.Should().Contain(allowlisted);

        var metricColumns = layer.Metrics
            .SelectMany(m => ColumnReference().Matches(m.SqlExpression).Select(x => x.Value))
            .Distinct()
            .ToList();
        metricColumns.Should().NotBeEmpty();
        columns.Should().Contain(metricColumns);
    }

    // Matches schema-qualified column references such as dbo.OrderLines.Quantity.
    [GeneratedRegex(@"\bdbo\.\w+\.\w+\b")]
    private static partial Regex ColumnReference();

    private static async Task<DatasetSqlServer> StartAsync()
    {
        var server = new DatasetSqlServer();
        await server.InitializeAsync();
        return server;
    }

    /// <summary>Container with the Dataset_Script already applied once.</summary>
    public sealed class LoadedDataset : IAsyncLifetime
    {
        public DatasetSqlServer Server { get; } = new();

        public async Task InitializeAsync()
        {
            await Server.InitializeAsync();
            var run = await Server.RunScriptAsync();
            run.ExitCode.Should().Be(0, run.Output);
        }

        public Task DisposeAsync() => Server.DisposeAsync();
    }
}
