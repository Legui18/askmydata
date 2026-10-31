using AskMyData.Domain.Semantic;
using FluentAssertions;

namespace AskMyData.UnitTests.Domain;

public sealed class SemanticLayerCatalogTests
{
    private static readonly string[] ExpectedMetrics =
        ["TotalRevenue", "UnitsSold", "OrderCount", "GrossMargin", "AverageOrderValue", "CustomerCount"];

    private static readonly string[] ExpectedAllowlist =
        ["Customers", "Products", "Stores", "Orders", "OrderLines", "Calendar"];

    private readonly SemanticLayer _layer = SemanticLayerCatalog.Create();

    [Fact]
    public void Catalog_defines_the_six_governed_metrics()
    {
        _layer.Metrics.Select(m => m.Name).Should().BeEquivalentTo(ExpectedMetrics);
    }

    [Fact]
    public void Every_metric_has_name_description_expression_and_allowlisted_dependencies()
    {
        foreach (var metric in _layer.Metrics)
        {
            metric.Name.Should().NotBeNullOrWhiteSpace();
            metric.Description.Should().NotBeNullOrWhiteSpace($"metric {metric.Name} needs a description");
            metric.SqlExpression.Should().NotBeNullOrWhiteSpace($"metric {metric.Name} needs a SQL expression");
            metric.DependsOnTables.Should().NotBeEmpty($"metric {metric.Name} must declare its tables");

            foreach (var table in metric.DependsOnTables)
            {
                var parts = table.Split('.');
                parts.Should().HaveCount(2, $"dependency '{table}' must be schema-qualified");
                _layer.TryGetObject(parts[0], parts[1], out _)
                    .Should().BeTrue($"dependency '{table}' of {metric.Name} must be an Allowlist entry");
            }
        }
    }

    [Fact]
    public void Metric_expressions_reference_only_allowlisted_columns()
    {
        var qualifiedColumns = _layer.Allowlist
            .SelectMany(e => e.Columns.Select(c => $"{e.Schema}.{e.Name}.{c.Name}"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var metric in _layer.Metrics)
        {
            var references = System.Text.RegularExpressions.Regex
                .Matches(metric.SqlExpression, @"\bdbo\.\w+\.\w+")
                .Select(m => m.Value)
                .ToList();

            references.Should().NotBeEmpty();
            references.Should().OnlyContain(
                r => qualifiedColumns.Contains(r), $"metric {metric.Name} must use Allowlist columns");
        }
    }

    [Fact]
    public void Allowlist_contains_dataset_tables_with_columns_and_types()
    {
        _layer.Allowlist.Select(e => e.Name).Should().BeEquivalentTo(ExpectedAllowlist);

        foreach (var entry in _layer.Allowlist)
        {
            entry.Schema.Should().Be("dbo");
            entry.Kind.Should().Be(AllowlistObjectKind.Table);
            entry.Columns.Should().NotBeEmpty();
            entry.Columns.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.SqlType));
            entry.Columns.Select(c => c.Name).Should().OnlyHaveUniqueItems();
        }
    }

    [Theory]
    [InlineData("Employees")]
    [InlineData("DatasetVersion")]
    public void Allowlist_excludes_non_governed_tables(string table)
    {
        _layer.TryGetObject("dbo", table, out _).Should().BeFalse();
    }

    [Fact]
    public void Allowed_table_functions_are_empty_by_default()
    {
        _layer.AllowedTableFunctions.Should().BeEmpty();
    }

    [Fact]
    public void Create_honors_a_supplied_table_function_list_case_insensitively()
    {
        var layer = SemanticLayerCatalog.Create(["STRING_SPLIT"]);

        layer.AllowedTableFunctions.Should().HaveCount(1);
        layer.AllowedTableFunctions.Contains("string_split").Should().BeTrue();
    }

    [Theory]
    [InlineData("TotalRevenue")]
    [InlineData("totalrevenue")]
    [InlineData("TOTALREVENUE")]
    public void TryGetMetric_is_case_insensitive(string name)
    {
        _layer.TryGetMetric(name, out var metric).Should().BeTrue();
        metric.Name.Should().Be("TotalRevenue");
    }

    [Fact]
    public void TryGetMetric_returns_false_for_unknown_metric()
    {
        _layer.TryGetMetric("NetProfit", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("dbo", "Orders")]
    [InlineData("DBO", "orders")]
    [InlineData("Dbo", "ORDERS")]
    public void TryGetObject_is_case_insensitive(string schema, string name)
    {
        _layer.TryGetObject(schema, name, out var entry).Should().BeTrue();
        entry.Name.Should().Be("Orders");
    }

    [Fact]
    public void TryGetObject_requires_matching_schema()
    {
        _layer.TryGetObject("sales", "Orders", out _).Should().BeFalse();
    }
}
