using AskMyData.Domain.Results;
using AskMyData.Infrastructure.Validation;
using AskMyData.UnitTests.Generators;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Infrastructure.Validation.Properties;

public sealed class MetricMismatchProperties
{
    private readonly ScriptDomSqlValidator _validator = new(DatasetModel.Layer);

    /// <summary>Names that cannot collide with a Semantic_Layer Metric.</summary>
    private static readonly Gen<string> UnknownName =
        Gen.Choose(0, 9999).Select(n => $"NoSuchMetric{n}");

    // Feature: askmydata-conversational-analytics, Property 6: Metric mismatches are detected and named
    /// <summary>**Validates: Requirements 3.19, 3.21**</summary>
    [Property(MaxTest = 500)]
    public Property Unknown_metric_name_fails_with_case_a_naming_the_metric()
    {
        var input = from query in MetricQueryGen.Query
                    from name in UnknownName
                    select (query, name);

        return Prop.ForAll(input.ToArbitrary(), x =>
        {
            var result = _validator.Validate(x.query.Query.ToString(), [x.name]);
            return (!result.Passed
                    && result.ReasonCode == ReasonCodes.MetricDefinitionMismatch
                    && result.MetricName == x.name
                    && result.Detail is not null
                    && result.Detail.Contains("case (a)", StringComparison.Ordinal))
                .Label($"{x.name}: {result.ReasonCode} ({result.Detail}): {x.query.Query}");
        });
    }

    // Feature: askmydata-conversational-analytics, Property 6: Metric mismatches are detected and named
    /// <summary>**Validates: Requirements 3.19, 3.21**</summary>
    [Property(MaxTest = 500)]
    public Property Altered_metric_expression_fails_with_case_b_naming_the_metric()
    {
        return Prop.ForAll(MetricMutationGen.Mutation.ToArbitrary(), m =>
        {
            var result = _validator.Validate(m.Query.ToString(), [m.Metric.Name]);
            return (!result.Passed
                    && result.ReasonCode == ReasonCodes.MetricDefinitionMismatch
                    && result.MetricName == m.Metric.Name
                    && result.Detail is not null
                    && result.Detail.Contains("case (b)", StringComparison.Ordinal))
                .Label($"{m.Metric.Name} {m.Kind}: {result.ReasonCode} ({result.Detail}): {m.Query}");
        });
    }
}
