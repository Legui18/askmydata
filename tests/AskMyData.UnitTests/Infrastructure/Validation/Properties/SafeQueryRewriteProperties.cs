using AskMyData.Application.Validation;
using AskMyData.Infrastructure.Validation;
using AskMyData.UnitTests.Generators;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Infrastructure.Validation.Properties;

public sealed class SafeQueryRewriteProperties
{
    /// <summary>A generated safe query paired with one structural rewrite of it.</summary>
    private static readonly Gen<(SafeQuery Query, RewrittenQuery Rewrite)> Input =
        from query in SafeQueryGen.Query
        from rewrite in RewriteGen.For(query)
        select (query, rewrite);

    // Feature: askmydata-conversational-analytics, Property 2: Safe queries pass under structural rewrites
    /// <summary>**Validates: Requirements 3.12, 3.14, 3.15, 3.24**</summary>
    [Property(MaxTest = 500)]
    public Property Safe_queries_pass_before_and_after_structural_rewrites()
    {
        var validator = new ScriptDomSqlValidator(DatasetModel.Layer);

        return Prop.ForAll(Input.ToArbitrary(), input =>
        {
            var originalSql = input.Query.ToString();
            var rewrittenSql = input.Rewrite.Sql;
            var original = validator.Validate(originalSql, []);
            var rewritten = validator.Validate(rewrittenSql, []);

            return (original.Passed && rewritten.Passed)
                .Label($"original: {Describe(original)}: {originalSql}")
                .Label($"rewritten ({input.Rewrite.Aliases}, {input.Rewrite.Style}): {Describe(rewritten)}: {rewrittenSql}");
        });
    }

    private static string Describe(ValidationResult result) =>
        result.Passed ? "PASS" : $"{result.ReasonCode} ({result.Detail})";
}
