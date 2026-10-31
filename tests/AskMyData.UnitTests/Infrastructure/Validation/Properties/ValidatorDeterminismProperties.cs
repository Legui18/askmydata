using AskMyData.Application.Validation;
using AskMyData.Infrastructure.Validation;
using AskMyData.UnitTests.Generators;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Infrastructure.Validation.Properties;

public sealed class ValidatorDeterminismProperties
{
    private static readonly string[] SqlFragments =
    [
        "SELECT", "FROM", "WHERE", "dbo.Orders", "o.OrderId", "*", ";", "--", "/*", "*/", "(", ")", "'", "[", "]",
        "GO", "EXEC", "DROP TABLE", "sys.objects", "OPENROWSET(", "COUNT(*)", "\n", " ",
    ];

    /// <summary>Generated safe queries, rewrites, violation cases, Metric embeddings and mutations, and arbitrary text.</summary>
    private static readonly Gen<string> SqlInput = Gen.OneOf(
        SafeQueryGen.Query.Select(q => q.ToString()),
        SafeQueryGen.Query.SelectMany(RewriteGen.For).Select(r => r.Sql),
        ViolationGen.Single.Select(c => c.Sql),
        ViolationGen.Many.Select(c => c.Sql),
        MetricQueryGen.Query.Select(m => m.Query.ToString()),
        MetricMutationGen.Mutation.Select(m => m.Query.ToString()),
        ArbMap.Default.GeneratorFor<string>().Where(s => s is not null),
        Gen.ListOf(Gen.Elements(SqlFragments)).Select(parts => string.Join(" ", parts)));

    /// <summary>Known Metric names (in any case), unknown names and arbitrary strings.</summary>
    private static readonly Gen<string> MetricName = Gen.OneOf(
        Gen.Elements(MetricQueryGen.Metrics.Select(m => m.Name).ToArray()),
        Gen.Elements(MetricQueryGen.Metrics.Select(m => m.Name.ToUpperInvariant()).ToArray()),
        Gen.Elements("GrossMargin2", "Revenue", "", " "),
        ArbMap.Default.GeneratorFor<string>().Where(s => s is not null));

    private static readonly Gen<(string Sql, string[] MetricUsage)> Input =
        from sql in SqlInput
        from count in Gen.Choose(0, 3)
        from usage in Gen.ArrayOf(MetricName, count)
        select (sql, usage);

    // Feature: askmydata-conversational-analytics, Property 1: Validator determinism
    /// <summary>**Validates: Requirements 3.13**</summary>
    [Property(MaxTest = 500)]
    public Property Validate_returns_equal_results_on_repeat_calls_and_separate_instances()
    {
        var first = new ScriptDomSqlValidator(DatasetModel.Layer);
        var second = new ScriptDomSqlValidator(DatasetModel.Layer);

        return Prop.ForAll(Input.ToArbitrary(), input =>
        {
            var a = first.Validate(input.Sql, input.MetricUsage);
            var b = first.Validate(input.Sql, input.MetricUsage);
            var c = second.Validate(input.Sql, input.MetricUsage);

            return (Same(a, b) && Same(a, c))
                .Label($"same instance: {a} vs {b}; separate instance: {c}");
        });
    }

    private static bool Same(ValidationResult x, ValidationResult y) =>
        x.Passed == y.Passed
        && x.ReasonCode == y.ReasonCode
        && x.Detail == y.Detail
        && x.MetricName == y.MetricName;
}
