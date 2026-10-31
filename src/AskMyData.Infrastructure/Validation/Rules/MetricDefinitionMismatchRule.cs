using AskMyData.Application.Validation;
using AskMyData.Domain.Results;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.Infrastructure.Validation.Rules;

/// <summary>
/// Rule 15, METRIC_DEFINITION_MISMATCH (Requirements 3.18 to 3.21). For each Metric declared in
/// Metric_Usage, in declaration order: (a) the name is not a Metric in the Semantic_Layer, or
/// (b) the Normalized_Syntax_Tree of the Metric SQL expression is not a sub-tree of the
/// Normalized_Syntax_Tree of the query. Skipped when Metric_Usage is empty. The comparison is
/// done on <see cref="NormNode"/> trees only; no raw text is compared.
/// </summary>
internal sealed class MetricDefinitionMismatchRule : IValidationRule
{
    private static readonly SyntaxTreeNormalizer Normalizer = new();

    public int Order => 15;

    public string ReasonCode => ReasonCodes.MetricDefinitionMismatch;

    public ValidationResult? Check(ValidationContext context)
    {
        if (context.MetricUsage.Count == 0)
        {
            return null;
        }

        NormNode? query = null;
        foreach (var declared in context.MetricUsage)
        {
            var name = declared ?? string.Empty;
            if (!context.SemanticLayer.TryGetMetric(name, out var metric))
            {
                return Mismatch(name, $"Metric '{name}': case (a) not defined in Semantic_Layer");
            }

            var expression = ParseExpression(metric.SqlExpression);
            query ??= Normalizer.NormalizeQuery(context.Script, context.Scopes);
            if (expression is null || !query.ContainsSubtree(Normalizer.NormalizeExpression(expression)))
            {
                return Mismatch(metric.Name, $"Metric '{metric.Name}': case (b) expression is not a sub-tree of the query");
            }
        }

        return null;
    }

    private ValidationResult Mismatch(string metricName, string detail) =>
        ValidationResult.Fail(ReasonCode, detail) with { MetricName = metricName };

    // A new parser per call keeps the rule stateless; an unparsable definition can never match.
    private static ScalarExpression? ParseExpression(string sql)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var expression = parser.ParseExpression(reader, out var errors);
        return errors.Count == 0 ? expression : null;
    }
}
