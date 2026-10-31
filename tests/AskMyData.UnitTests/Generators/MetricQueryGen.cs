using AskMyData.Domain.Semantic;
using FsCheck;
using FsCheck.Fluent;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.UnitTests.Generators;

/// <summary>A safe query that embeds the SQL expression of <see cref="Metric"/> unchanged.</summary>
public sealed record MetricQuery(Metric Metric, SqlExpr Expression, SafeQuery Query)
{
    public override string ToString() => $"{Metric.Name}: {Query}";
}

/// <summary>
/// Embeds a governed Metric expression into a <see cref="SafeQueryGen"/> query whose sources
/// include the Metric's dependency tables (Properties 5 and 6). The expression AST is parsed from
/// the catalog's SQL expression, so it is printed with the same aliases, quoting and case as the
/// rest of the query.
/// </summary>
public static class MetricQueryGen
{
    public static IReadOnlyList<Metric> Metrics => DatasetModel.Layer.Metrics;

    /// <summary>Any Metric embedded in a query that may contain other measures.</summary>
    public static Gen<MetricQuery> Query { get; } =
        Gen.Elements(Metrics.ToArray()).SelectMany(m => For(m, extraMeasures: true));

    /// <summary>The Metric expression, parsed from <see cref="Metric.SqlExpression"/>.</summary>
    public static SqlExpr ExpressionOf(Metric metric) => MetricExpressionParser.Parse(metric.SqlExpression);

    public static Gen<MetricQuery> For(Metric metric, bool extraMeasures) =>
        Embedding(metric, ExpressionOf(metric), extraMeasures)
            .Select(q => new MetricQuery(metric, ExpressionOf(metric), q));

    /// <summary>
    /// A query that selects <paramref name="expression"/> as a measure aliased with the Metric
    /// name, at a random position after the dimensions, grouped by the dimensions.
    /// </summary>
    internal static Gen<SafeQuery> Embedding(Metric metric, SqlExpr expression, bool extraMeasures) =>
        from baseQuery in SafeQueryGen.Build(TablesOf(metric), extraMeasures, maxJoins: 2)
        from position in Gen.Choose(0, 1000)
        select Embed(baseQuery, metric.Name, expression, position);

    private static SafeQuery Embed(SafeQuery query, string alias, SqlExpr expression, int position)
    {
        var dimensions = query.Select.Where(i => !IsAggregate(i.Expr)).ToArray();
        var measures = query.Select.Where(i => IsAggregate(i.Expr)).ToList();
        measures.Insert(position % (measures.Count + 1), new SelectItem(expression, alias));
        return query with
        {
            Select = [.. dimensions, .. measures],
            GroupBy = dimensions.Select(d => d.Expr).ToArray(),
        };
    }

    private static bool IsAggregate(SqlExpr expr) =>
        expr is CountStarExpr or FunctionExpr { Name: "SUM" or "AVG" or "MIN" or "MAX" or "COUNT" or "ROUND" };

    /// <summary>Dependency tables without schema, ordered so that each one has a direct join edge to the first.</summary>
    private static IReadOnlyList<string> TablesOf(Metric metric) =>
        metric.DependsOnTables.Select(t => t.Split('.')[^1]).ToArray();
}

/// <summary>Converts a catalog Metric SQL expression into the generator AST.</summary>
internal static class MetricExpressionParser
{
    public static SqlExpr Parse(string sql)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var expression = parser.ParseExpression(reader, out var errors);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Metric expression does not parse: {sql}");
        }

        return Convert(expression);
    }

    private static SqlExpr Convert(ScalarExpression expression) => expression switch
    {
        ParenthesisExpression p => Convert(p.Expression),
        IntegerLiteral i => new IntLiteral(int.Parse(i.Value, System.Globalization.CultureInfo.InvariantCulture)),
        ColumnReferenceExpression c when c.MultiPartIdentifier.Count >= 2 => new ColumnExpr(
            c.MultiPartIdentifier.Identifiers[^2].Value, c.MultiPartIdentifier.Identifiers[^1].Value),
        FunctionCall f => new FunctionExpr(
            f.FunctionName.Value.ToUpperInvariant(),
            f.UniqueRowFilter == UniqueRowFilter.Distinct,
            f.Parameters.Select(Convert).ToArray()),
        BinaryExpression b => new BinaryExpr(
            b.BinaryExpressionType switch
            {
                BinaryExpressionType.Add => "+",
                BinaryExpressionType.Subtract => "-",
                BinaryExpressionType.Multiply => "*",
                BinaryExpressionType.Divide => "/",
                _ => throw new NotSupportedException(b.BinaryExpressionType.ToString()),
            },
            Convert(b.FirstExpression),
            Convert(b.SecondExpression)),
        _ => throw new NotSupportedException($"Unsupported Metric expression node {expression.GetType().Name}."),
    };
}
