using AskMyData.Domain.Results;
using AskMyData.Domain.Semantic;
using AskMyData.Infrastructure.Validation;
using FluentAssertions;

namespace AskMyData.UnitTests.Infrastructure.Validation;

public sealed class ScriptDomSqlValidatorScopeRulesTests
{
    private readonly ScriptDomSqlValidator _validator = new(SemanticLayerCatalog.Create());

    [Theory]
    // Aliases, unaliased tables, default schema, quoting and case
    [InlineData("SELECT o.OrderId, o.OrderDate FROM dbo.Orders AS o")]
    [InlineData("SELECT Orders.OrderId FROM Orders")]
    [InlineData("SELECT dbo.Orders.OrderId FROM dbo.Orders")]
    [InlineData("SELECT [O].[orderid] FROM [DBO].[ORDERS] [O]")]
    [InlineData("SELECT OrderId, Status FROM dbo.Orders")]
    [InlineData("SELECT c.[Date], c.[Year] FROM dbo.Calendar AS c")]
    [InlineData("SELECT SUM(dbo.OrderLines.Quantity * dbo.OrderLines.UnitPrice * (1 - dbo.OrderLines.Discount)) AS TotalRevenue FROM dbo.OrderLines")]
    // Joins and unqualified columns that exist in one source
    [InlineData("SELECT Region, OrderDate FROM dbo.Orders AS o JOIN dbo.Stores AS s ON s.StoreId = o.StoreId")]
    // CTEs, including column lists, chained and recursive CTEs
    [InlineData("WITH r (StoreKey, Total) AS (SELECT o.StoreId, COUNT(*) FROM dbo.Orders AS o GROUP BY o.StoreId) SELECT r.StoreKey, Total FROM r ORDER BY r.Total")]
    [InlineData("WITH a AS (SELECT o.OrderId FROM dbo.Orders AS o), b AS (SELECT a.OrderId FROM a) SELECT b.OrderId FROM b")]
    [InlineData("WITH n AS (SELECT 1 AS k UNION ALL SELECT n.k + 1 FROM n WHERE n.k < 5) SELECT n.k FROM n")]
    [InlineData("WITH Orders AS (SELECT o.OrderId AS Id FROM dbo.Orders AS o) SELECT Orders.Id FROM Orders")]
    // Derived tables, with and without column lists, and VALUES
    [InlineData("SELECT d.StoreKey FROM (SELECT o.StoreId FROM dbo.Orders AS o) AS d (StoreKey)")]
    [InlineData("SELECT d.N FROM (SELECT COUNT(*) AS N FROM dbo.Orders AS o) AS d")]
    [InlineData("SELECT v.x FROM (VALUES (1), (2)) AS v (x)")]
    // UNION branches and ORDER BY on the union
    [InlineData("SELECT s.Region AS Name FROM dbo.Stores AS s UNION SELECT c.Country FROM dbo.Customers AS c ORDER BY Name")]
    // APPLY sees sources to its left
    [InlineData("SELECT c.CustomerId, x.LastOrder FROM dbo.Customers AS c CROSS APPLY (SELECT MAX(o.OrderDate) AS LastOrder FROM dbo.Orders AS o WHERE o.CustomerId = c.CustomerId) AS x")]
    [InlineData("SELECT c.CustomerId, x.OrderId FROM dbo.Customers AS c OUTER APPLY (SELECT TOP (1) o.OrderId FROM dbo.Orders AS o WHERE o.CustomerId = c.CustomerId ORDER BY o.OrderDate DESC) AS x")]
    // Correlated subqueries see outer scopes
    [InlineData("SELECT c.CustomerName FROM dbo.Customers AS c WHERE EXISTS (SELECT o.OrderId FROM dbo.Orders AS o WHERE o.CustomerId = c.CustomerId)")]
    [InlineData("SELECT c.CustomerName, (SELECT COUNT(*) FROM dbo.Orders AS o WHERE o.CustomerId = c.CustomerId) AS Orders FROM dbo.Customers AS c")]
    [InlineData("SELECT c.CustomerName FROM dbo.Customers AS c WHERE c.CustomerId IN (SELECT o.CustomerId FROM dbo.Orders AS o)")]
    // ORDER BY select-list alias
    [InlineData("SELECT s.Region, COUNT(*) AS Stores FROM dbo.Stores AS s GROUP BY s.Region ORDER BY Stores DESC")]
    // Date-part keywords are not columns
    [InlineData("SELECT DATEPART(year, o.OrderDate) AS Y, DATEADD(month, 1, o.OrderDate) AS M, DATEDIFF(day, o.OrderDate, o.OrderDate) AS D FROM dbo.Orders AS o")]
    // Window functions
    [InlineData("SELECT o.OrderId, ROW_NUMBER() OVER (PARTITION BY o.CustomerId ORDER BY o.OrderDate) AS Rn FROM dbo.Orders AS o")]
    public void Queries_over_allowlist_objects_pass(string sql)
    {
        var result = _validator.Validate(sql, []);

        result.Passed.Should().BeTrue(result.Detail);
    }

    [Theory]
    [InlineData("SELECT e.Salary FROM dbo.Employees AS e")]
    [InlineData("SELECT v.Version FROM DatasetVersion AS v")]
    [InlineData("SELECT o.OrderId FROM sales.Orders AS o")]
    [InlineData("SELECT t.x FROM #temp AS t")]
    [InlineData("WITH c AS (SELECT e.FullName FROM dbo.Employees AS e) SELECT c.FullName FROM c")]
    [InlineData("SELECT d.FullName FROM (SELECT e.FullName FROM dbo.Employees AS e) AS d")]
    [InlineData("SELECT o.OrderId FROM dbo.Orders AS o UNION ALL SELECT e.EmployeeId FROM dbo.Employees AS e")]
    [InlineData("SELECT c.CustomerId FROM dbo.Customers AS c CROSS APPLY (SELECT e.Salary FROM dbo.Employees AS e) AS x")]
    [InlineData("SELECT c.CustomerId FROM dbo.Customers AS c WHERE EXISTS (SELECT e.EmployeeId FROM dbo.Employees AS e)")]
    [InlineData("SELECT o.OrderId FROM dbo.Orders AS o JOIN dbo.Employees AS e ON e.EmployeeId = o.CustomerId")]
    // A CTE name that is schema-qualified is a base table, not the CTE
    [InlineData("WITH Employees AS (SELECT o.OrderId FROM dbo.Orders AS o) SELECT e.OrderId FROM dbo.Employees AS e")]
    // A CTE is not visible to an earlier CTE
    [InlineData("WITH a AS (SELECT b.OrderId FROM b), b AS (SELECT o.OrderId FROM dbo.Orders AS o) SELECT a.OrderId FROM a")]
    public void Non_allowlist_tables_are_rejected(string sql)
    {
        _validator.Validate(sql, []).ReasonCode.Should().Be(ReasonCodes.TableNotAllowed);
    }

    [Theory]
    [InlineData("SELECT o.Salary FROM dbo.Orders AS o")]
    [InlineData("SELECT Salary FROM dbo.Orders")]
    [InlineData("SELECT x.OrderId FROM dbo.Orders AS o")]
    [InlineData("SELECT Orders.OrderId FROM dbo.Orders AS o")]
    [InlineData("SELECT dbo.Orders.OrderId FROM dbo.Orders AS o")]
    [InlineData("SELECT o.OrderId FROM dbo.Orders AS o WHERE o.Hidden = 1")]
    [InlineData("SELECT o.OrderId FROM dbo.Orders AS o ORDER BY o.Secret")]
    // CTE and derived-table output columns
    [InlineData("WITH r AS (SELECT o.StoreId FROM dbo.Orders AS o) SELECT r.OrderId FROM r")]
    [InlineData("WITH r (StoreKey) AS (SELECT o.StoreId FROM dbo.Orders AS o) SELECT r.StoreId FROM r")]
    [InlineData("SELECT d.StoreId FROM (SELECT o.StoreId FROM dbo.Orders AS o) AS d (StoreKey)")]
    [InlineData("SELECT d.Secret FROM (SELECT o.StoreId FROM dbo.Orders AS o) AS d")]
    [InlineData("SELECT d.StoreId FROM (SELECT o.Secret AS StoreId FROM dbo.Orders AS o) AS d")]
    // Derived-table bodies do not see sibling sources without APPLY
    [InlineData("SELECT c.CustomerId FROM dbo.Customers AS c CROSS JOIN (SELECT o.OrderId FROM dbo.Orders AS o WHERE o.CustomerId = c.CustomerId) AS x")]
    // UNION branches are separate scopes
    [InlineData("SELECT o.OrderId FROM dbo.Orders AS o UNION SELECT o.Secret FROM dbo.Orders AS o")]
    [InlineData("SELECT o.OrderId FROM dbo.Orders AS o UNION SELECT s.StoreId FROM dbo.Stores AS s ORDER BY Missing")]
    // Correlated subqueries
    [InlineData("SELECT c.CustomerName FROM dbo.Customers AS c WHERE EXISTS (SELECT o.OrderId FROM dbo.Orders AS o WHERE o.CustomerId = c.Secret)")]
    // Select-list aliases are only visible in ORDER BY
    [InlineData("SELECT COUNT(*) AS N FROM dbo.Orders AS o WHERE N > 1")]
    [InlineData("SELECT o.Status AS S, COUNT(*) AS N FROM dbo.Orders AS o GROUP BY S")]
    // Pseudo-columns
    [InlineData("SELECT $IDENTITY FROM dbo.Orders")]
    public void Non_allowlist_columns_are_rejected(string sql)
    {
        _validator.Validate(sql, []).ReasonCode.Should().Be(ReasonCodes.ColumnNotAllowed);
    }

    [Fact]
    public void Table_rule_runs_before_column_rule()
    {
        _validator.Validate("SELECT o.Secret, e.Salary FROM dbo.Orders AS o JOIN dbo.Employees AS e ON 1 = 1", [])
            .ReasonCode.Should().Be(ReasonCodes.TableNotAllowed);
    }

    [Fact]
    public void Failure_details_name_the_offending_object()
    {
        _validator.Validate("SELECT e.Salary FROM dbo.Employees AS e", []).Detail
            .Should().Contain("dbo.Employees");
        _validator.Validate("SELECT o.Salary FROM dbo.Orders AS o", []).Detail
            .Should().Contain("o.Salary");
    }

    [Fact]
    public void Allowed_table_functions_expose_their_columns()
    {
        var validator = new ScriptDomSqlValidator(SemanticLayerCatalog.Create(["STRING_SPLIT"]));

        validator.Validate(
                "SELECT c.CustomerId, v.value FROM dbo.Customers AS c CROSS APPLY STRING_SPLIT(c.City, ' ') AS v", [])
            .Passed.Should().BeTrue();
        validator.Validate(
                "SELECT c.CustomerId FROM dbo.Customers AS c CROSS APPLY STRING_SPLIT(c.Secret, ' ') AS v", [])
            .ReasonCode.Should().Be(ReasonCodes.ColumnNotAllowed);
    }

    [Fact]
    public void Unsupported_table_sources_fail_closed()
    {
        _validator.Validate(
                "SELECT p.[2024] FROM (SELECT YEAR(o.OrderDate) AS Y, o.OrderId FROM dbo.Orders AS o) AS s " +
                "PIVOT (COUNT(s.OrderId) FOR s.Y IN ([2024])) AS p", [])
            .ReasonCode.Should().Be(ReasonCodes.TableNotAllowed);
    }
}
