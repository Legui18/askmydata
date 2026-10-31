using AskMyData.Application.Charts;
using AskMyData.Application.Execution;
using AskMyData.Domain.Results;
using FluentAssertions;

namespace AskMyData.UnitTests.Application;

public class ChartSuggesterTests
{
    private readonly ChartSuggester _suggester = new();

    private static ResultColumn Num(string name) => new(name, "decimal", ColumnKind.Numeric);
    private static ResultColumn Txt(string name) => new(name, "nvarchar", ColumnKind.Text);
    private static ResultColumn Tmp(string name) => new(name, "date", ColumnKind.Temporal);

    private static QueryResult Result(int rowCount, params ResultColumn[] columns) =>
        new(columns,
            Enumerable.Range(0, rowCount).Select(_ => new object?[columns.Length]).ToList(),
            Truncated: false,
            TimeSpan.Zero);

    [Fact]
    public void Text_and_measure_gives_bar()
    {
        var s = _suggester.Suggest(Result(3, Txt("Region"), Num("TotalRevenue")));

        s.Type.Should().Be(ChartType.Bar);
        s.XColumn.Should().Be("Region");
        s.YColumns.Should().Equal("TotalRevenue");
    }

    [Fact]
    public void Temporal_kind_gives_line_with_all_measures()
    {
        var s = _suggester.Suggest(Result(2, Txt("Region"), Tmp("OrderDate"), Num("Revenue"), Num("Orders")));

        s.Type.Should().Be(ChartType.Line);
        s.XColumn.Should().Be("OrderDate");
        s.YColumns.Should().Equal("Revenue", "Orders");
    }

    [Theory]
    [InlineData("OrderYear")]
    [InlineData("order_month")]
    [InlineData("Fiscal Quarter")]
    [InlineData("WEEK")]
    [InlineData("period")]
    public void Numeric_with_temporal_name_token_is_temporal_not_measure(string name)
    {
        var s = _suggester.Suggest(Result(5, Num(name), Num("Revenue")));

        s.Type.Should().Be(ChartType.Line);
        s.XColumn.Should().Be(name);
        s.YColumns.Should().Equal("Revenue");
    }

    [Theory]
    [InlineData("Weekday")]   // single token "Weekday", not "week"
    [InlineData("Updated")]   // no token match
    public void Name_tokens_must_match_whole_token(string name)
    {
        var s = _suggester.Suggest(Result(5, Txt("Region"), Num(name)));

        s.Type.Should().Be(ChartType.Bar);
        s.YColumns.Should().Equal(name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Fewer_than_two_rows_gives_table_only(int rows)
    {
        var s = _suggester.Suggest(Result(rows, Txt("Region"), Num("TotalRevenue")));

        s.Type.Should().Be(ChartType.TableOnly);
        s.XColumn.Should().BeNull();
        s.YColumns.Should().BeEmpty();
    }

    [Fact]
    public void No_measure_gives_table_only()
    {
        var s = _suggester.Suggest(Result(10, Txt("Region"), Tmp("OrderDate"), Num("OrderYear")));

        s.Type.Should().Be(ChartType.TableOnly);
    }

    [Fact]
    public void Measures_without_temporal_or_text_gives_table_only()
    {
        var s = _suggester.Suggest(Result(10, Num("Revenue"), new ResultColumn("Active", "bit", ColumnKind.Boolean)));

        s.Type.Should().Be(ChartType.TableOnly);
    }

    [Theory]
    [InlineData("OrderYear", new[] { "Order", "Year" })]
    [InlineData("order_month", new[] { "order", "month" })]
    [InlineData("Fiscal Quarter", new[] { "Fiscal", "Quarter" })]
    [InlineData("SKUDate", new[] { "SKU", "Date" })]
    public void Tokenize_splits_on_case_underscore_and_space(string name, string[] expected)
    {
        ChartSuggester.Tokenize(name).Should().Equal(expected);
    }
}
