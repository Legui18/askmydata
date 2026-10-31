using AskMyData.Domain.Results;
using AskMyData.Domain.Semantic;
using AskMyData.Infrastructure.Validation;
using FluentAssertions;

namespace AskMyData.UnitTests.Infrastructure.Validation;

public sealed class ScriptDomSqlValidatorStructuralRulesTests
{
    private const string SafeQuery =
        "SELECT s.Region, SUM(ol.Quantity * ol.UnitPrice * (1 - ol.Discount)) AS TotalRevenue, COUNT(*) AS Lines " +
        "FROM dbo.OrderLines AS ol JOIN dbo.Orders AS o ON o.OrderId = ol.OrderId " +
        "JOIN dbo.Stores AS s ON s.StoreId = o.StoreId " +
        "WHERE YEAR(o.OrderDate) = 2024 GROUP BY s.Region ORDER BY TotalRevenue DESC";

    private readonly ScriptDomSqlValidator _validator = new(SemanticLayerCatalog.Create());

    [Theory]
    [InlineData(SafeQuery)]
    [InlineData("SELECT COUNT(*) AS N FROM dbo.Orders")]
    [InlineData("WITH r AS (SELECT o.StoreId, COUNT(*) AS N FROM dbo.Orders AS o GROUP BY o.StoreId) SELECT r.StoreId, r.N FROM r")]
    [InlineData("SELECT d.Region FROM (SELECT [s].[Region] FROM [dbo].[Stores] AS [s]) AS d")]
    [InlineData("SELECT ROUND(SUM(ol.UnitPrice), 2) AS P, ISNULL(MAX(ol.Discount), 0) AS D FROM dbo.OrderLines AS ol")]
    [InlineData("SELECT o.OrderId FROM dbo.Orders AS o UNION ALL SELECT o2.OrderId FROM dbo.Orders AS o2")]
    [InlineData("SELECT o.OrderId FROM dbo.Orders AS o;")]
    public void Structurally_safe_queries_pass(string sql)
    {
        _validator.Validate(sql, []).Passed.Should().BeTrue();
    }

    [Theory]
    // Rule 1
    [InlineData("", ReasonCodes.ParseError)]
    [InlineData("   \r\n\t ", ReasonCodes.ParseError)]
    [InlineData("SELEC o.OrderId FROM dbo.Orders AS o", ReasonCodes.ParseError)]
    [InlineData("SELECT o.OrderId FROM", ReasonCodes.ParseError)]
    // Rule 2
    [InlineData("SELECT 1; DROP TABLE dbo.Orders", ReasonCodes.MultipleStatements)]
    [InlineData("SELECT 1 AS A\nGO\nSELECT 2 AS B", ReasonCodes.MultipleStatements)]
    // Rule 3
    [InlineData("EXEC sp_who", ReasonCodes.NotSelectOnly)]
    [InlineData("sp_who", ReasonCodes.NotSelectOnly)]
    [InlineData("EXEC sys.sp_configure", ReasonCodes.NotSelectOnly)]
    [InlineData("DELETE FROM dbo.Orders", ReasonCodes.NotSelectOnly)]
    [InlineData("UPDATE dbo.Orders SET Status = 'x'", ReasonCodes.NotSelectOnly)]
    [InlineData("DROP TABLE dbo.Orders", ReasonCodes.NotSelectOnly)]
    [InlineData("SELECT o.OrderId INTO dbo.Copy FROM dbo.Orders AS o", ReasonCodes.NotSelectOnly)]
    [InlineData("INSERT INTO dbo.Copy (OrderId) SELECT t.OrderId FROM (DELETE FROM dbo.Orders OUTPUT deleted.OrderId) AS t(OrderId)", ReasonCodes.NotSelectOnly)]
    // Rule 4
    [InlineData("SELECT o.OrderId FROM dbo.Orders AS o -- trailing", ReasonCodes.CommentNotAllowed)]
    [InlineData("SELECT /* hidden */ o.OrderId FROM dbo.Orders AS o", ReasonCodes.CommentNotAllowed)]
    // Rule 5
    [InlineData("SELECT a.x FROM OPENROWSET('SQLNCLI', 'Server=x;Trusted_Connection=yes;', 'SELECT 1 AS x') AS a", ReasonCodes.OpenRowsetNotAllowed)]
    [InlineData("SELECT a.x FROM OPENROWSET(BULK 'c:\\f.txt', SINGLE_CLOB) AS a", ReasonCodes.OpenRowsetNotAllowed)]
    [InlineData("SELECT a.x FROM OPENDATASOURCE('SQLNCLI', 'Data Source=x').db.dbo.t AS a", ReasonCodes.OpenRowsetNotAllowed)]
    // Rule 6
    [InlineData("SELECT a.x FROM OPENQUERY(remote, 'SELECT 1 AS x') AS a", ReasonCodes.OpenQueryNotAllowed)]
    // Rule 7
    [InlineData("SELECT o.OrderId FROM srv.shop.dbo.Orders AS o", ReasonCodes.LinkedServerNotAllowed)]
    // Rule 8
    [InlineData("SELECT o.OrderId FROM other.dbo.Orders AS o", ReasonCodes.CrossDatabaseNotAllowed)]
    [InlineData("SELECT o.OrderId FROM other..Orders AS o", ReasonCodes.CrossDatabaseNotAllowed)]
    // Rule 9
    [InlineData("SELECT o.name FROM sys.objects AS o", ReasonCodes.SystemObjectNotAllowed)]
    [InlineData("SELECT t.TABLE_NAME FROM information_schema.tables AS t", ReasonCodes.SystemObjectNotAllowed)]
    [InlineData("SELECT o.name FROM [SYS].[objects] AS o", ReasonCodes.SystemObjectNotAllowed)]
    [InlineData("SELECT p.name FROM sys.fn_listextendedproperty(NULL, NULL, NULL, NULL, NULL, NULL, NULL) AS p", ReasonCodes.SystemObjectNotAllowed)]
    [InlineData("SELECT sys.fn_varbintohexstr(0x01) AS h", ReasonCodes.SystemObjectNotAllowed)]
    // Rule 10
    [InlineData("SELECT f.x FROM dbo.fnRows(1) AS f", ReasonCodes.TableFunctionNotAllowed)]
    [InlineData("SELECT v.value FROM STRING_SPLIT('a,b', ',') AS v", ReasonCodes.TableFunctionNotAllowed)]
    [InlineData("SELECT j.[key] FROM OPENJSON('{}') AS j", ReasonCodes.TableFunctionNotAllowed)]
    [InlineData("SELECT g.value FROM GENERATE_SERIES(1, 10) AS g", ReasonCodes.TableFunctionNotAllowed)]
    // Rule 11
    [InlineData("SELECT dbo.fnSecret(o.OrderId) AS s FROM dbo.Orders AS o", ReasonCodes.FunctionNotAllowed)]
    [InlineData("SELECT sales.fnAgg(o.OrderId) AS s FROM dbo.Orders AS o", ReasonCodes.FunctionNotAllowed)]
    // Rule 12
    [InlineData("SELECT * FROM dbo.Orders", ReasonCodes.SelectStar)]
    [InlineData("SELECT o.* FROM dbo.Orders AS o", ReasonCodes.SelectStar)]
    [InlineData("WITH c AS (SELECT * FROM dbo.Orders) SELECT c.OrderId FROM c", ReasonCodes.SelectStar)]
    [InlineData("SELECT d.OrderId FROM (SELECT o.* FROM dbo.Orders AS o) AS d", ReasonCodes.SelectStar)]
    public void Violations_are_rejected_with_their_reason_code(string sql, string expected)
    {
        var result = _validator.Validate(sql, []);

        result.Passed.Should().BeFalse();
        result.ReasonCode.Should().Be(expected);
    }

    [Theory]
    [InlineData("SELECT * FROM sys.objects; SELECT 1", ReasonCodes.MultipleStatements)]
    [InlineData("SELECT * FROM sys.objects -- x", ReasonCodes.CommentNotAllowed)]
    [InlineData("SELECT * FROM sys.objects", ReasonCodes.SystemObjectNotAllowed)]
    [InlineData("SELECT dbo.f(x.a) AS b FROM other.dbo.T AS x", ReasonCodes.CrossDatabaseNotAllowed)]
    [InlineData("SELECT * INTO dbo.Copy FROM dbo.Orders /* c */", ReasonCodes.NotSelectOnly)]
    public void First_failing_rule_in_order_wins(string sql, string expected)
    {
        _validator.Validate(sql, []).ReasonCode.Should().Be(expected);
    }

    [Fact]
    public void Listed_table_functions_pass()
    {
        var validator = new ScriptDomSqlValidator(SemanticLayerCatalog.Create(["string_split", "dbo.fnRows"]));

        validator.Validate("SELECT v.value FROM STRING_SPLIT('a,b', ',') AS v", []).Passed.Should().BeTrue();
        validator.Validate("SELECT f.x FROM dbo.fnRows(1) AS f", []).Passed.Should().BeTrue();
        validator.Validate("SELECT f.x FROM fnRows(1) AS f", []).Passed.Should().BeTrue();
        validator.Validate("SELECT f.x FROM sales.fnRows(1) AS f", []).ReasonCode
            .Should().Be(ReasonCodes.TableFunctionNotAllowed);
    }

    [Fact]
    public void Rules_are_in_strictly_ascending_documented_order()
    {
        var rules = ScriptDomSqlValidator.Rules;

        rules.Select(r => r.Order).Should().Equal(Enumerable.Range(1, rules.Count));
        rules.Select(r => r.ReasonCode).Should().Equal(
            ReasonCodes.ParseError,
            ReasonCodes.MultipleStatements,
            ReasonCodes.NotSelectOnly,
            ReasonCodes.CommentNotAllowed,
            ReasonCodes.OpenRowsetNotAllowed,
            ReasonCodes.OpenQueryNotAllowed,
            ReasonCodes.LinkedServerNotAllowed,
            ReasonCodes.CrossDatabaseNotAllowed,
            ReasonCodes.SystemObjectNotAllowed,
            ReasonCodes.TableFunctionNotAllowed,
            ReasonCodes.FunctionNotAllowed,
            ReasonCodes.SelectStar,
            ReasonCodes.TableNotAllowed,
            ReasonCodes.ColumnNotAllowed,
            ReasonCodes.MetricDefinitionMismatch);
    }

    [Theory]
    [InlineData(SafeQuery)]
    [InlineData("SELECT * FROM sys.objects")]
    [InlineData("not sql at all")]
    public void Validation_is_deterministic_across_calls_and_instances(string sql)
    {
        var other = new ScriptDomSqlValidator(SemanticLayerCatalog.Create());

        var first = _validator.Validate(sql, []);
        _validator.Validate(sql, []).Should().Be(first);
        other.Validate(sql, []).Should().Be(first);
    }
}
