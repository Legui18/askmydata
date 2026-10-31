using System.Reflection;
using AskMyData.Application.Execution;
using AskMyData.Application.Options;
using AskMyData.Application.Validation;
using AskMyData.Domain.Results;
using FluentAssertions;

namespace AskMyData.UnitTests.Application;

public class ApplicationContractsTests
{
    [Fact]
    public void AskOptions_has_documented_defaults()
    {
        var o = new AskOptions();

        o.RequestDeadline.Should().Be(TimeSpan.FromSeconds(60));
        o.LlmTimeout.Should().Be(TimeSpan.FromSeconds(30));
        o.CommandTimeout.Should().Be(TimeSpan.FromSeconds(15));
        o.RowLimit.Should().Be(1000);
        o.HistoryTurns.Should().Be(10);
        o.MaxExplanationLength.Should().Be(500);
    }

    [Fact]
    public void ValidationResult_factories_set_fields()
    {
        ValidationResult.Pass().Should().Be(new ValidationResult(true, null, null));

        var fail = ValidationResult.Fail(ReasonCodes.MetricDefinitionMismatch, "detail") with { MetricName = "GrossMargin" };
        fail.Passed.Should().BeFalse();
        fail.ReasonCode.Should().Be(ReasonCodes.MetricDefinitionMismatch);
        fail.Detail.Should().Be("detail");
        fail.MetricName.Should().Be("GrossMargin");
    }

    [Fact]
    public void ValidatedSql_has_no_public_constructor()
    {
        typeof(ValidatedSql).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Should().BeEmpty();
        new ValidatedSql("SELECT 1").Text.Should().Be("SELECT 1");
    }

    [Fact]
    public void OneOf_holds_exactly_one_value()
    {
        var error = new ExecutionError(ExecutionErrorCategory.Timeout, TimeSpan.FromSeconds(1));
        OneOf<QueryResult, ExecutionError> value = error;

        value.IsFirst.Should().BeFalse();
        value.IsSecond.Should().BeTrue();
        value.TryGetFirst(out _).Should().BeFalse();
        value.TryGetSecond(out var got).Should().BeTrue();
        got.Should().BeSameAs(error);
        value.Match(_ => "rows", e => e.Category.ToString()).Should().Be("Timeout");
    }

    [Fact]
    public void Default_OneOf_throws_on_match()
    {
        var value = default(OneOf<QueryResult, ExecutionError>);

        value.IsFirst.Should().BeFalse();
        value.IsSecond.Should().BeFalse();
        var act = () => value.Match(_ => 1, _ => 2);
        act.Should().Throw<InvalidOperationException>();
    }
}
