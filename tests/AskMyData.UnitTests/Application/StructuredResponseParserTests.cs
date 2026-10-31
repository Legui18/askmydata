using AskMyData.Application.Llm;
using FluentAssertions;

namespace AskMyData.UnitTests.Application;

public class StructuredResponseParserTests
{
    private readonly StructuredResponseParser _parser = new();

    private const string Valid =
        """{"sql":"SELECT 1","metricUsage":["TotalRevenue"],"explanation":"e","adHocCalculation":false}""";

    [Fact]
    public void Parses_valid_object()
    {
        var r = _parser.TryParse(Valid);

        r.Should().NotBeNull();
        r!.Sql.Should().Be("SELECT 1");
        r.MetricUsage.Should().Equal("TotalRevenue");
        r.Explanation.Should().Be("e");
        r.AdHocCalculation.Should().BeFalse();
        r.Refusal.Should().BeNull();
    }

    [Theory]
    [InlineData("```json\n{0}\n```")]
    [InlineData("```\n{0}\n```")]
    public void Strips_single_markdown_fence(string template)
    {
        _parser.TryParse(template.Replace("{0}", Valid)).Should().NotBeNull();
    }

    [Fact]
    public void Rejects_double_fence_and_unterminated_fence()
    {
        _parser.TryParse("```json\n```json\n" + Valid + "\n```\n```").Should().BeNull();
        _parser.TryParse("```json\n" + Valid).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"sql":"SELECT 1","metricUsage":[],"explanation":"e"}""")]
    [InlineData("""{"sql":1,"metricUsage":[],"explanation":"e","adHocCalculation":true}""")]
    [InlineData("""{"sql":"SELECT 1","metricUsage":[1],"explanation":"e","adHocCalculation":true}""")]
    [InlineData("""{"sql":"SELECT 1","metricUsage":[],"explanation":"e","adHocCalculation":"true"}""")]
    [InlineData("""{"sql":"SELECT 1","metricUsage":[],"explanation":"e","adHocCalculation":true,"extra":1}""")]
    [InlineData("""{"sql":"","metricUsage":[],"explanation":"e","adHocCalculation":true}""")]
    [InlineData("""{"sql":"","metricUsage":[],"explanation":"e","adHocCalculation":true,"refusal":""}""")]
    [InlineData("""{"sql":"SELECT 1","metricUsage":[],"explanation":"e","adHocCalculation":true,"refusal":5}""")]
    [InlineData("""{"sql":"SELECT 1","metricUsage":[],"explanation":"e","adHocCalculation":true} trailing""")]
    public void Rejects_invalid_output(string raw)
    {
        _parser.TryParse(raw).Should().BeNull();
    }

    [Fact]
    public void Accepts_refusal_with_empty_sql_and_null_refusal_is_ignored()
    {
        var refused = _parser.TryParse(
            """{"sql":"","metricUsage":[],"explanation":"","adHocCalculation":false,"refusal":"No."}""");
        refused!.Refusal.Should().Be("No.");

        _parser.TryParse(
            """{"sql":"SELECT 1","metricUsage":[],"explanation":"e","adHocCalculation":true,"refusal":null}""")
            .Should().NotBeNull();
    }

    [Fact]
    public void Refusal_wins_when_both_present()
    {
        var r = _parser.TryParse(
            """{"sql":"SELECT 1","metricUsage":[],"explanation":"e","adHocCalculation":false,"refusal":"No."}""");

        r.Should().NotBeNull();
        r!.Refusal.Should().Be("No.");
    }
}
