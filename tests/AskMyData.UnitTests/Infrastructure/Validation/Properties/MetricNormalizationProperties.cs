using AskMyData.Application.Validation;
using AskMyData.Domain.Results;
using AskMyData.Domain.Semantic;
using AskMyData.Infrastructure.Validation;
using AskMyData.UnitTests.Generators;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Infrastructure.Validation.Properties;

public sealed class MetricNormalizationProperties
{
    private static readonly string[] WhitespacePool = [" ", "  ", "\n", "\t", "\r\n  ", " \t "];

    /// <summary>
    /// Print styles for the catalog Metric expression itself: brackets, identifier and keyword case,
    /// explicit or omitted <c>dbo</c>, extra parentheses and whitespace. Wrapping and alias options
    /// do not apply to a bare expression.
    /// </summary>
    private static readonly Gen<(PrintStyle Style, bool OuterParentheses)> ExpressionStyle =
        from style in RewriteGen.Style
        from outer in Gen.Elements(true, false)
        select (style with { Wrap = WrapKind.None }, outer);

    /// <summary>A Metric embedded in a safe query, one structural rewrite of that query, and one rewrite of the Metric expression.</summary>
    private static readonly Gen<Input> Inputs =
        from metricQuery in MetricQueryGen.Query
        from rewrite in RewriteGen.For(metricQuery.Query)
        from expressionStyle in ExpressionStyle
        from safeSql in Gen.OneOf(
            SafeQueryGen.Query.Select(q => q.ToString()),
            SafeQueryGen.Query.SelectMany(RewriteGen.For).Select(r => r.Sql),
            MetricMutationGen.Mutation.Select(m => m.Query.ToString()))
        select new Input(
            metricQuery,
            rewrite,
            PrintExpression(metricQuery.Metric, metricQuery.Expression, expressionStyle.Style, expressionStyle.OuterParentheses),
            safeSql);

    // Feature: askmydata-conversational-analytics, Property 5: Metric check is invariant to normalization-only differences
    /// <summary>**Validates: Requirements 3.18, 3.20, 3.21**</summary>
    [Property(MaxTest = 500)]
    public Property Metric_check_is_invariant_to_normalization_only_differences()
    {
        var catalogValidator = new ScriptDomSqlValidator(DatasetModel.Layer);

        return Prop.ForAll(Inputs.ToArbitrary(), input =>
        {
            var metric = input.MetricQuery.Metric;
            string[] usage = [metric.Name];
            var originalSql = input.MetricQuery.Query.ToString();
            var rewrittenSql = input.Rewrite.Sql;

            // The catalog expression text rewritten; the governed tree is unchanged.
            var rewrittenLayer = WithExpression(DatasetModel.Layer, metric, input.RewrittenExpression);
            var rewrittenExpressionValidator = new ScriptDomSqlValidator(rewrittenLayer);

            var original = catalogValidator.Validate(originalSql, usage);
            var rewritten = catalogValidator.Validate(rewrittenSql, usage);
            var expressionOnly = rewrittenExpressionValidator.Validate(originalSql, usage);
            var both = rewrittenExpressionValidator.Validate(rewrittenSql, usage);
            var emptyUsage = catalogValidator.Validate(input.SafeSql, []);

            var metricPasses = original.Passed && rewritten.Passed && expressionOnly.Passed && both.Passed;
            var emptyUsageSkipsCheck = emptyUsage.ReasonCode != ReasonCodes.MetricDefinitionMismatch;

            return (metricPasses && emptyUsageSkipsCheck)
                .Label($"metric {metric.Name}, expression rewritten as: {input.RewrittenExpression}")
                .Label($"original query: {Describe(original)}: {originalSql}")
                .Label($"rewritten query ({input.Rewrite.Aliases}, {input.Rewrite.Style}): {Describe(rewritten)}: {rewrittenSql}")
                .Label($"rewritten expression, original query: {Describe(expressionOnly)}")
                .Label($"rewritten expression, rewritten query: {Describe(both)}")
                .Label($"empty usage: {Describe(emptyUsage)}: {input.SafeSql}")
                .Collect(metric.Name);
        });
    }

    /// <summary>Prints the Metric expression with every column qualified by its base table (no aliases).</summary>
    private static string PrintExpression(Metric metric, SqlExpr expression, PrintStyle style, bool outerParentheses)
    {
        var scope = MetricMutationGen.Nodes(expression)
            .OfType<ColumnExpr>()
            .Select(c => c.Table)
            .Distinct(StringComparer.Ordinal)
            .Select(t => new TableSource(t, null))
            .ToArray();

        var text = new SqlPrinter(style).Expr(expression, scope);
        if (outerParentheses)
        {
            text = $"({text})";
        }

        return ApplyWhitespace(text, style.WhitespaceSeed, metric.Name);
    }

    private static string ApplyWhitespace(string text, int seed, string metricName)
    {
        if (seed == 0)
        {
            return text;
        }

        var random = new Random(seed ^ metricName.Length);
        var sb = new System.Text.StringBuilder(WhitespacePool[random.Next(WhitespacePool.Length)]);
        foreach (var (ch, inLiteral) in SqlText.Scan(text))
        {
            sb.Append(ch == ' ' && !inLiteral ? WhitespacePool[random.Next(WhitespacePool.Length)] : ch.ToString());
        }

        return sb.Append(WhitespacePool[random.Next(WhitespacePool.Length)]).ToString();
    }

    private static SemanticLayer WithExpression(SemanticLayer layer, Metric metric, string sqlExpression) =>
        new(
            layer.Metrics.Select(m => ReferenceEquals(m, metric) || m.Name == metric.Name
                ? m with { SqlExpression = sqlExpression }
                : m),
            layer.Allowlist,
            layer.AllowedTableFunctions);

    private static string Describe(ValidationResult result) =>
        result.Passed ? "PASS" : $"{result.ReasonCode} ({result.Detail})";

    private sealed record Input(MetricQuery MetricQuery, RewrittenQuery Rewrite, string RewrittenExpression, string SafeSql)
    {
        public override string ToString() =>
            $"{MetricQuery} | rewrite: {Rewrite} | expression: {RewrittenExpression} | empty-usage query: {SafeSql}";
    }
}
