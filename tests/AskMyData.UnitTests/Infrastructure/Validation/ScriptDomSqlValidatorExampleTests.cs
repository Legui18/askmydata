using AskMyData.Domain.Results;
using AskMyData.Domain.Semantic;
using AskMyData.Infrastructure.Validation;
using FluentAssertions;

namespace AskMyData.UnitTests.Infrastructure.Validation;

/// <summary>
/// One readable example per Reason_Code. Expected codes follow the documented rule order
/// (see <see cref="ScriptDomSqlValidator.Rules"/>).
/// Validates: Requirements 3.1, 3.2, 3.3, 3.5, 3.6, 3.17, 3.19, 3.23
/// </summary>
public sealed class ScriptDomSqlValidatorExampleTests
{
    private readonly ScriptDomSqlValidator _validator = new(SemanticLayerCatalog.Create());

    [Theory]
    [InlineData("SELECT 1; DROP TABLE dbo.Orders", ReasonCodes.MultipleStatements)]
    [InlineData("EXEC sp_who", ReasonCodes.NotSelectOnly)]
    [InlineData("SELECT o.OrderId INTO dbo.Copy FROM dbo.Orders AS o", ReasonCodes.NotSelectOnly)]
    [InlineData("SELECT o.OrderId FROM dbo.Orders AS o /* block */", ReasonCodes.CommentNotAllowed)]
    [InlineData("SELECT o.OrderId FROM dbo.Orders AS o -- line", ReasonCodes.CommentNotAllowed)]
    [InlineData("SELECT a.x FROM OPENDATASOURCE('SQLNCLI', 'Data Source=x').db.dbo.t AS a", ReasonCodes.OpenRowsetNotAllowed)]
    [InlineData("SELECT p.name FROM sys.fn_listextendedproperty(NULL, NULL, NULL, NULL, NULL, NULL, NULL) AS p", ReasonCodes.SystemObjectNotAllowed)]
    [InlineData("SELECT dbo.fnSecret(o.OrderId) AS s FROM dbo.Orders AS o", ReasonCodes.FunctionNotAllowed)]
    public void Example_query_fails_with_expected_reason_code(string sql, string expected)
    {
        var result = _validator.Validate(sql, []);

        result.Passed.Should().BeFalse();
        result.ReasonCode.Should().Be(expected);
    }

    [Fact]
    public void Count_star_over_allowlisted_table_passes()
    {
        _validator.Validate("SELECT COUNT(*) AS N FROM dbo.Orders AS o", []).Passed.Should().BeTrue();
    }

    [Fact]
    public void Reordered_metric_operands_fail_with_metric_mismatch_case_b()
    {
        const string sql =
            "SELECT SUM(ol.UnitPrice * ol.Quantity * (1 - ol.Discount)) AS R FROM dbo.OrderLines AS ol";

        var result = _validator.Validate(sql, ["TotalRevenue"]);

        result.Passed.Should().BeFalse();
        result.ReasonCode.Should().Be(ReasonCodes.MetricDefinitionMismatch);
        result.MetricName.Should().Be("TotalRevenue");
        result.Detail.Should().Contain("case (b)");
    }
}
