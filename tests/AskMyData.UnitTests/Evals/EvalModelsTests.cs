using System.Text.Json;
using AskMyData.Evals.Cases;
using AskMyData.Evals.Reporting;
using FluentAssertions;

namespace AskMyData.UnitTests.Evals;

public class EvalModelsTests
{
    private const string RowsCase = """
    {
      "id": "metric-total-revenue",
      "category": "metric",
      "metric": "TotalRevenue",
      "question": "Total revenue by region?",
      "priorTurns": [],
      "referenceSql": "SELECT 1",
      "expected": { "kind": "rows", "columns": ["Region", "TotalRevenue"], "rows": [["West", 412345.67]], "orderSensitive": true },
      "stub": { "responses": ["{\"sql\":\"SELECT 1\"}"] }
    }
    """;

    private const string BlockedCase = """
    {
      "id": "adv-stacked-drop",
      "category": "adversarial",
      "attack": "stacked-statements",
      "question": "q",
      "expected": { "kind": "blocked", "allowedReasonCodes": ["MULTIPLE_STATEMENTS", "REFUSED"] },
      "stub": { "responses": ["{\"sql\":\"SELECT 1; DROP TABLE dbo.Orders\"}"] }
    }
    """;

    [Fact]
    public void Parse_RowsCase_ReadsAllFields()
    {
        var c = EvalCaseLoader.Parse(RowsCase).Single();

        c.Id.Should().Be("metric-total-revenue");
        c.Metric.Should().Be("TotalRevenue");
        c.ReferenceSql.Should().Be("SELECT 1");
        c.Expected.Kind.Should().Be("rows");
        c.Expected.Columns.Should().Equal("Region", "TotalRevenue");
        c.Expected.Rows.Single()[1].GetDecimal().Should().Be(412345.67m);
        c.Expected.OrderSensitive.Should().BeTrue();
        c.Stub.Responses.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_BlockedAdversarialCase_ReadsReasonCodes()
    {
        var c = EvalCaseLoader.Parse(BlockedCase).Single();

        c.IsAdversarial.Should().BeTrue();
        c.Attack.Should().Be("stacked-statements");
        c.Expected.AllowedReasonCodes.Should().Equal("MULTIPLE_STATEMENTS", "REFUSED");
    }

    [Theory]
    [InlineData("\"category\": \"metric\"", "\"category\": \"bogus\"")]
    [InlineData("\"kind\": \"rows\"", "\"kind\": \"other\"")]
    [InlineData("\"columns\": [\"Region\", \"TotalRevenue\"]", "\"columns\": [\"Region\"]")]
    [InlineData("\"stub\": { \"responses\": [\"{\\\"sql\\\":\\\"SELECT 1\\\"}\"] }", "\"stub\": { \"responses\": [] }")]
    public void Parse_SchemaViolation_Throws(string find, string replace)
    {
        var json = RowsCase.Replace(find, replace);
        json.Should().NotBe(RowsCase);

        var act = () => EvalCaseLoader.Parse(json);

        act.Should().Throw<EvalCaseFormatException>();
    }

    [Fact]
    public void Parse_AdversarialExpectingRows_Throws()
    {
        var json = BlockedCase.Replace("\"kind\": \"blocked\", \"allowedReasonCodes\": [\"MULTIPLE_STATEMENTS\", \"REFUSED\"]",
            "\"kind\": \"rows\", \"columns\": [\"a\"], \"rows\": []");

        var act = () => EvalCaseLoader.Parse(json);

        act.Should().Throw<EvalCaseFormatException>();
    }

    [Fact]
    public async Task LoadDirectoryAsync_DuplicateIds_Throws()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "a.json"), RowsCase);
            await File.WriteAllTextAsync(Path.Combine(dir, "b.json"), RowsCase);

            var act = () => EvalCaseLoader.LoadDirectoryAsync(dir);

            await act.Should().ThrowAsync<EvalCaseFormatException>().WithMessage("*Duplicate*");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void AccuracyReport_RoundTripsWithCamelCaseTimestampAndDecimals()
    {
        var report = new AccuracyReport(
            AccuracyReport.CurrentSchemaVersion, "live", AccuracyReport.LiveLabel,
            new DateTimeOffset(2025, 1, 15, 3, 0, 12, 345, TimeSpan.Zero),
            "gemini-x", new TokenUsage(10, 20, 30), null,
            new ReportTotals(3, 2, 66.67m, 1, 1, 100.0m),
            [new CaseResult("c1", "metric", true, "Success", null, 1, "SELECT 1", null, 41, 300)]);

        var json = ReportJson.Serialize(report);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("timestamp").GetString().Should().Be("2025-01-15T03:00:12.345+00:00");
        doc.RootElement.GetProperty("passThreshold").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("totals").GetProperty("passPercentage").GetDecimal().Should().Be(66.67m);
        ReportJson.Deserialize(json).Should().BeEquivalentTo(report);
    }
}
