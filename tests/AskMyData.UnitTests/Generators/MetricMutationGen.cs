using AskMyData.Domain.Semantic;
using FsCheck;
using FsCheck.Fluent;

namespace AskMyData.UnitTests.Generators;

public enum MetricMutationKind
{
    /// <summary>Swap the operands of a binary operator (for * and + this is a commutative reordering).</summary>
    SwapOperands,

    /// <summary>Re-group <c>(a op b) op c</c> as <c>a op (b op c)</c>.</summary>
    Reassociate,

    ChangeOperator,
    ReplaceColumn,
    ChangeLiteral,

    /// <summary>SUM to AVG, MIN or MAX; COUNT(DISTINCT x) to COUNT(x).</summary>
    ChangeAggregate,
}

/// <summary>
/// A query that embeds an altered copy of <see cref="Metric"/>'s expression and no other aggregate,
/// so the governed expression tree is not a sub-tree of the query (Property 6, case (b)).
/// </summary>
public sealed record MetricMutation(Metric Metric, MetricMutationKind Kind, SqlExpr Mutated, SafeQuery Query)
{
    public override string ToString() => $"{Metric.Name} {Kind}: {Query}";
}

/// <summary>Generates altered Metric expressions embedded in safe queries.</summary>
public static class MetricMutationGen
{
    public static Gen<MetricMutation> Mutation { get; } =
        Gen.Elements(MetricQueryGen.Metrics.ToArray()).SelectMany(For);

    public static Gen<MetricMutation> For(Metric metric)
    {
        var original = MetricQueryGen.ExpressionOf(metric);
        return from mutation in Mutate(original)
               from query in MetricQueryGen.Embedding(metric, mutation.Mutated, extraMeasures: false)
               select new MetricMutation(metric, mutation.Kind, mutation.Mutated, query);
    }

    /// <summary>All mutations of <paramref name="expression"/> that change its tree, as a generator.</summary>
    public static Gen<(MetricMutationKind Kind, SqlExpr Mutated)> Mutate(SqlExpr expression)
    {
        var nodes = Nodes(expression).ToArray();
        var options = new List<Gen<(MetricMutationKind, SqlExpr)>>();

        var binaries = Indexes(nodes, n => n is BinaryExpr);
        if (binaries.Length > 0)
        {
            options.Add(Gen.Elements(binaries).Select(i =>
            {
                var b = (BinaryExpr)nodes[i];
                return (MetricMutationKind.SwapOperands, ReplaceAt(expression, i, b with { Left = b.Right, Right = b.Left }));
            }));
            options.Add(
                from i in Gen.Elements(binaries)
                let b = (BinaryExpr)nodes[i]
                from op in Gen.Elements(new[] { "+", "-", "*", "/" }.Where(o => o != b.Op).ToArray())
                select (MetricMutationKind.ChangeOperator, ReplaceAt(expression, i, b with { Op = op })));
        }

        var associative = Indexes(nodes, n => n is BinaryExpr { Left: BinaryExpr inner } outer && inner.Op == outer.Op);
        if (associative.Length > 0)
        {
            options.Add(Gen.Elements(associative).Select(i =>
            {
                var outer = (BinaryExpr)nodes[i];
                var inner = (BinaryExpr)outer.Left;
                var regrouped = new BinaryExpr(outer.Op, inner.Left, new BinaryExpr(outer.Op, inner.Right, outer.Right));
                return (MetricMutationKind.Reassociate, ReplaceAt(expression, i, regrouped));
            }));
        }

        var columns = Indexes(nodes, n => n is ColumnExpr);
        options.Add(
            from i in Gen.Elements(columns)
            let c = (ColumnExpr)nodes[i]
            from replacement in Gen.Elements(NumericColumns(c.Table).Where(n => n != c.Column).ToArray())
            select (MetricMutationKind.ReplaceColumn, ReplaceAt(expression, i, c with { Column = replacement })));

        var literals = Indexes(nodes, n => n is IntLiteral);
        if (literals.Length > 0)
        {
            options.Add(
                from i in Gen.Elements(literals)
                from delta in Gen.Choose(1, 9)
                select (MetricMutationKind.ChangeLiteral,
                    ReplaceAt(expression, i, new IntLiteral(((IntLiteral)nodes[i]).Value + delta))));
        }

        var aggregates = Indexes(nodes, n => n is FunctionExpr { Name: "SUM" } or FunctionExpr { Name: "COUNT", Distinct: true });
        if (aggregates.Length > 0)
        {
            options.Add(
                from i in Gen.Elements(aggregates)
                let f = (FunctionExpr)nodes[i]
                from name in Gen.Elements("AVG", "MIN", "MAX")
                select (MetricMutationKind.ChangeAggregate,
                    ReplaceAt(expression, i, f.Name == "SUM" ? f with { Name = name } : f with { Distinct = false })));
        }

        return Gen.OneOf(options);
    }

    private static string[] NumericColumns(string table) => DatasetModel.ColumnsOf(table)
        .Where(c => DatasetModel.TypeOf(c) == ColumnType.Numeric)
        .Select(c => c.Name)
        .ToArray();

    private static int[] Indexes(SqlExpr[] nodes, Func<SqlExpr, bool> predicate) =>
        Enumerable.Range(0, nodes.Length).Where(i => predicate(nodes[i])).ToArray();

    /// <summary>Pre-order traversal.</summary>
    public static IEnumerable<SqlExpr> Nodes(SqlExpr expression)
    {
        yield return expression;
        var children = expression switch
        {
            BinaryExpr b => [b.Left, b.Right],
            FunctionExpr f => f.Args,
            _ => (IReadOnlyList<SqlExpr>)[],
        };
        foreach (var child in children)
        {
            foreach (var node in Nodes(child))
            {
                yield return node;
            }
        }
    }

    /// <summary>Replaces the node at pre-order <paramref name="index"/>.</summary>
    public static SqlExpr ReplaceAt(SqlExpr expression, int index, SqlExpr replacement)
    {
        var counter = 0;
        return Walk(expression);

        SqlExpr Walk(SqlExpr node)
        {
            if (counter++ == index)
            {
                return replacement;
            }

            return node switch
            {
                BinaryExpr b => WalkBinary(b),
                FunctionExpr f => f with { Args = f.Args.Select(Walk).ToArray() },
                _ => node,
            };
        }

        SqlExpr WalkBinary(BinaryExpr b)
        {
            var left = Walk(b.Left);
            var right = Walk(b.Right);
            return b with { Left = left, Right = right };
        }
    }
}
