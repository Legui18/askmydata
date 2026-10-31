using AskMyData.Domain.Results;
using AskMyData.Domain.Semantic;
using AskMyData.Infrastructure.Validation;
using FluentAssertions;

namespace AskMyData.UnitTests.Infrastructure.Validation;

public sealed class ScriptDomSqlValidatorMetricRuleTests
{
    private const string LinesJoinOrders =
        " FROM dbo.OrderLines AS ol JOIN dbo.Orders AS o ON o.OrderId = ol.OrderId";

    private readonly ScriptDomSqlValidator _validator = new(SemanticLayerCatalog.Create());

    [Theory]
    // TotalRevenue: canonical, alias-qualified, brackets and case, extra parentheses, no schema,
    // unqualified unique columns, whitespace, inside a derived table and a CTE
    [InlineData("TotalRevenue", "SELECT SUM(dbo.OrderLines.Quantity * dbo.OrderLines.UnitPrice * (1 - dbo.OrderLines.Discount)) AS R FROM dbo.OrderLines")]
    [InlineData("TotalRevenue", "SELECT s.Region, SUM(ol.Quantity * ol.UnitPrice * (1 - ol.Discount)) AS TotalRevenue" + LinesJoinOrders + " JOIN dbo.Stores AS s ON s.StoreId = o.StoreId GROUP BY s.Region")]
    [InlineData("TotalRevenue", "select sum([OL].[quantity] * [ol].[UNITPRICE] * ((1 - [ol].discount))) as r from [DBO].[ORDERLINES] [ol]")]
    [InlineData("TotalRevenue", "SELECT SUM((ol.Quantity * ol.UnitPrice) * (1 - ol.Discount)) AS R FROM dbo.OrderLines ol")]
    [InlineData("TotalRevenue", "SELECT SUM(OrderLines.Quantity * OrderLines.UnitPrice * (1 - OrderLines.Discount)) AS R FROM OrderLines")]
    [InlineData("TotalRevenue", "SELECT SUM(Quantity * UnitPrice * (1 - Discount)) AS R FROM dbo.OrderLines")]
    [InlineData("TotalRevenue", "SELECT\n  SUM( ol.Quantity\t*  ol.UnitPrice * ( 1 - ol.Discount ) ) AS R\nFROM dbo.OrderLines AS ol")]
    [InlineData("TotalRevenue", "SELECT d.R FROM (SELECT SUM(x.Quantity * x.UnitPrice * (1 - x.Discount)) AS R FROM dbo.OrderLines AS x) AS d")]
    [InlineData("TotalRevenue", "WITH t AS (SELECT SUM(x.Quantity * x.UnitPrice * (1 - x.Discount)) AS R FROM dbo.OrderLines AS x) SELECT t.R FROM t")]
    [InlineData("totalrevenue", "SELECT SUM(ol.Quantity * ol.UnitPrice * (1 - ol.Discount)) AS R FROM dbo.OrderLines AS ol")]
    // Other catalog Metrics
    [InlineData("UnitsSold", "SELECT p.Category, SUM([ol].[Quantity]) AS Units FROM dbo.OrderLines AS ol JOIN dbo.Products AS p ON p.ProductId = ol.ProductId GROUP BY p.Category")]
    [InlineData("OrderCount", "SELECT o.Channel, count(distinct O.ORDERID) AS N FROM dbo.Orders AS o GROUP BY o.Channel")]
    [InlineData("GrossMargin", "SELECT SUM(ol.Quantity * ((ol.UnitPrice * (1 - ol.Discount)) - ol.UnitCost)) AS M FROM [dbo].[OrderLines] AS [ol]")]
    [InlineData("AverageOrderValue", "SELECT SUM(ol.Quantity * ol.UnitPrice * (1 - ol.Discount)) / COUNT(DISTINCT o.OrderId) AS Aov" + LinesJoinOrders)]
    [InlineData("CustomerCount", "SELECT YEAR(ord.OrderDate) AS Y, COUNT(DISTINCT [ord].[customerid]) AS C FROM Orders AS ord GROUP BY YEAR(ord.OrderDate)")]
    public void Declared_metrics_used_as_defined_pass_under_normalization(string metric, string sql)
    {
        var result = _validator.Validate(sql, [metric]);

        result.Passed.Should().BeTrue(result.Detail);
    }

    [Fact]
    public void Several_declared_metrics_pass_when_all_are_present()
    {
        var sql = "SELECT SUM(ol.Quantity * ol.UnitPrice * (1 - ol.Discount)) AS R, SUM(ol.Quantity) AS U, " +
                  "COUNT(DISTINCT o.OrderId) AS N" + LinesJoinOrders;

        _validator.Validate(sql, ["TotalRevenue", "UnitsSold", "OrderCount"]).Passed.Should().BeTrue();
    }

    [Theory]
    // Commutative reordering is not normalized away (b*a versus a*b)
    [InlineData("SELECT SUM(ol.UnitPrice * ol.Quantity * (1 - ol.Discount)) AS R FROM dbo.OrderLines AS ol")]
    // Different grouping of the same operands
    [InlineData("SELECT SUM(ol.Quantity * (ol.UnitPrice * (1 - ol.Discount))) AS R FROM dbo.OrderLines AS ol")]
    // Operator, literal and column changes
    [InlineData("SELECT SUM(ol.Quantity * ol.UnitPrice * (1 + ol.Discount)) AS R FROM dbo.OrderLines AS ol")]
    [InlineData("SELECT SUM(ol.Quantity * ol.UnitPrice * (2 - ol.Discount)) AS R FROM dbo.OrderLines AS ol")]
    [InlineData("SELECT SUM(ol.Quantity * ol.UnitCost * (1 - ol.Discount)) AS R FROM dbo.OrderLines AS ol")]
    // Different aggregate
    [InlineData("SELECT AVG(ol.Quantity * ol.UnitPrice * (1 - ol.Discount)) AS R FROM dbo.OrderLines AS ol")]
    // Metric not used at all
    [InlineData("SELECT COUNT(*) AS N FROM dbo.OrderLines AS ol")]
    // Same column names from a CTE, not the governed base table
    [InlineData("WITH OrderLines AS (SELECT x.Quantity, x.UnitPrice, x.Discount FROM dbo.OrderLines AS x) SELECT SUM(OrderLines.Quantity * OrderLines.UnitPrice * (1 - OrderLines.Discount)) AS R FROM OrderLines")]
    public void Altered_metric_expressions_fail_with_case_b(string sql)
    {
        var result = _validator.Validate(sql, ["TotalRevenue"]);

        result.Passed.Should().BeFalse();
        result.ReasonCode.Should().Be(ReasonCodes.MetricDefinitionMismatch);
        result.MetricName.Should().Be("TotalRevenue");
        result.Detail.Should().Be("Metric 'TotalRevenue': case (b) expression is not a sub-tree of the query");
    }

    [Fact]
    public void Unknown_metric_names_fail_with_case_a()
    {
        var result = _validator.Validate("SELECT SUM(ol.Quantity) AS U FROM dbo.OrderLines AS ol", ["NetProfit"]);

        result.ReasonCode.Should().Be(ReasonCodes.MetricDefinitionMismatch);
        result.MetricName.Should().Be("NetProfit");
        result.Detail.Should().Be("Metric 'NetProfit': case (a) not defined in Semantic_Layer");
    }

    [Fact]
    public void The_first_mismatching_declared_metric_is_reported()
    {
        var result = _validator.Validate(
            "SELECT SUM(ol.Quantity) AS U FROM dbo.OrderLines AS ol", ["UnitsSold", "GrossMargin", "NetProfit"]);

        result.MetricName.Should().Be("GrossMargin");
        result.Detail.Should().Contain("case (b)");
    }

    [Theory]
    [InlineData("SELECT o.OrderId FROM dbo.Orders AS o")]
    [InlineData("SELECT SUM(ol.UnitPrice * ol.Quantity * (1 - ol.Discount)) AS R FROM dbo.OrderLines AS ol")]
    public void Empty_metric_usage_skips_the_metric_check(string sql)
    {
        _validator.Validate(sql, []).Passed.Should().BeTrue();
    }

    [Fact]
    public void Earlier_rules_win_over_the_metric_check()
    {
        _validator.Validate("SELECT e.Salary FROM dbo.Employees AS e", ["NetProfit"])
            .ReasonCode.Should().Be(ReasonCodes.TableNotAllowed);
    }

    [Fact]
    public void Normalized_trees_compare_structurally()
    {
        var normalizer = new SyntaxTreeNormalizer();
        var a = normalizer.NormalizeExpression(Parse("SUM([dbo].[OrderLines].[Quantity] * (2))"));
        var b = normalizer.NormalizeExpression(Parse("sum(OrderLines.quantity * 2)"));
        var c = normalizer.NormalizeExpression(Parse("SUM(2 * dbo.OrderLines.Quantity)"));
        var literal = normalizer.NormalizeExpression(Parse("2"));

        a.StructurallyEquals(b).Should().BeTrue();
        a.StructurallyEquals(c).Should().BeFalse();
        a.ContainsSubtree(literal).Should().BeTrue();
        literal.ContainsSubtree(a).Should().BeFalse();
    }

    private static Microsoft.SqlServer.TransactSql.ScriptDom.ScalarExpression Parse(string sql)
    {
        var parser = new Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var expression = parser.ParseExpression(reader, out var errors);
        errors.Should().BeEmpty();
        return expression;
    }
}
