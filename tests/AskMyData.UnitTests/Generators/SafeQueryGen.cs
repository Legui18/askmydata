using FsCheck;
using FsCheck.Fluent;

namespace AskMyData.UnitTests.Generators;

/// <summary>
/// Generates safe SELECT statements as a small AST: Allowlist columns only, joins along
/// <see cref="DatasetModel.Edges"/>, optional WHERE, GROUP BY and ORDER BY, unqualified built-in
/// functions (SUM, AVG, MIN, MAX, COUNT, ROUND, YEAR, MONTH, UPPER, LTRIM) and optional COUNT(*).
/// Every such query passes all validator rules (Requirement 3.12).
/// </summary>
public static class SafeQueryGen
{
    private static readonly string[] JoinKinds = ["JOIN", "INNER JOIN", "LEFT JOIN"];
    private static readonly string[] TextLiterals = ["West", "North", "Online", "Retail", "Shipped", "Bikes"];
    private static readonly string[] DateLiterals = ["2023-01-01", "2024-01-01", "2024-06-30"];

    /// <summary>Any safe query with up to three joins.</summary>
    public static Gen<SafeQuery> Query { get; } = Build([], extraMeasures: true, maxJoins: 3);

    /// <summary>
    /// A safe query whose sources include all <paramref name="requiredTables"/> (each connected by a
    /// direct join edge to the first), plus random joins. <paramref name="extraMeasures"/> = false
    /// generates no aggregate items, so a caller can add a measure that is the only aggregate.
    /// </summary>
    public static Gen<SafeQuery> Build(IReadOnlyList<string> requiredTables, bool extraMeasures, int maxJoins) =>
        from tables in Tables(requiredTables, maxJoins)
        from query in QueryOver(tables, extraMeasures)
        select query;

    /// <summary>The joined tables: root first, then each joined table with the edge and join kind used.</summary>
    private static Gen<IReadOnlyList<(string Table, JoinEdge? Edge, string Kind)>> Tables(
        IReadOnlyList<string> required, int maxJoins)
    {
        var start = required.Count > 0
            ? Gen.Constant(required[0])
            : Gen.Elements(DatasetModel.TableNames.ToArray());

        return from root in start
               from forced in GenHelpers.Sequence(required.Skip(1).Select(t => ForcedJoin(root, t)))
               from extraCount in Gen.Choose(0, maxJoins)
               from all in Extend([(root, null, "FROM"), .. forced], extraCount)
               select all;
    }

    private static Gen<(string Table, JoinEdge? Edge, string Kind)> ForcedJoin(string root, string table)
    {
        var edge = DatasetModel.Edges.First(e => e.Touches(root) && e.Other(root) == table);
        return Gen.Elements(JoinKinds).Select(kind => (table, (JoinEdge?)edge, kind));
    }

    private static Gen<IReadOnlyList<(string Table, JoinEdge? Edge, string Kind)>> Extend(
        IReadOnlyList<(string Table, JoinEdge? Edge, string Kind)> current, int remaining)
    {
        var present = current.Select(c => c.Table).ToHashSet(StringComparer.Ordinal);
        var candidates = DatasetModel.Edges
            .Where(e => present.Contains(e.FromTable) != present.Contains(e.ToTable))
            .ToArray();
        if (remaining == 0 || candidates.Length == 0)
        {
            return Gen.Constant(current);
        }

        return from edge in Gen.Elements(candidates)
               from kind in Gen.Elements(JoinKinds)
               let table = present.Contains(edge.FromTable) ? edge.ToTable : edge.FromTable
               from rest in Extend([.. current, (table, edge, kind)], remaining - 1)
               select rest;
    }

    private static Gen<SafeQuery> QueryOver(IReadOnlyList<(string Table, JoinEdge? Edge, string Kind)> tables, bool extraMeasures)
    {
        var columns = tables
            .SelectMany(t => DatasetModel.ColumnsOf(t.Table).Select(c => (Expr: new ColumnExpr(t.Table, c.Name), Type: DatasetModel.TypeOf(c))))
            .ToArray();

        return from aliased in GenHelpers.Sequence(tables.Select(_ => Gen.Elements(true, false)))
               from dimensionColumns in GenHelpers.Distinct(columns, 0, 3)
               from dimensionExprs in GenHelpers.Sequence(dimensionColumns.Select(Dimension))
               from measures in extraMeasures
                   ? GenHelpers.Sequence(Enumerable.Range(0, 3).Select(_ => Measure(columns))).SelectMany(all =>
                       Gen.Choose(dimensionColumns.Count == 0 ? 1 : 0, 2).Select(n => (IReadOnlyList<SqlExpr>)all.Take(n).ToArray()))
                   : Gen.Constant<IReadOnlyList<SqlExpr>>([])
               from predicates in GenHelpers.Distinct(columns, 0, 2).SelectMany(cs => GenHelpers.Sequence(cs.Select(Predicate)))
               from orderCount in Gen.Choose(0, 2)
               from directions in GenHelpers.Sequence(Enumerable.Range(0, 2).Select(_ => Gen.Elements(true, false)))
               select Assemble(tables, aliased, dimensionExprs, measures, predicates, orderCount, directions);
    }

    private static SafeQuery Assemble(
        IReadOnlyList<(string Table, JoinEdge? Edge, string Kind)> tables,
        IReadOnlyList<bool> aliased,
        IReadOnlyList<SqlExpr> dimensions,
        IReadOnlyList<SqlExpr> measures,
        IReadOnlyList<SqlExpr> predicates,
        int orderCount,
        IReadOnlyList<bool> descending)
    {
        var sources = tables
            .Select((t, i) => new TableSource(t.Table, aliased[i] ? DatasetModel.DefaultAlias(t.Table) : null))
            .ToArray();

        var joins = tables.Skip(1).Select((t, i) =>
        {
            var edge = t.Edge!;
            var joined = sources[i + 1];
            var left = new ColumnExpr(edge.FromTable, edge.FromColumn);
            var right = new ColumnExpr(edge.ToTable, edge.ToColumn);
            return edge.ToTable == joined.Table
                ? new JoinClause(t.Kind, joined, right, left)
                : new JoinClause(t.Kind, joined, left, right);
        }).ToArray();

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<SelectItem>();
        for (var i = 0; i < dimensions.Count; i++)
        {
            var expr = dimensions[i];
            string? alias = expr switch
            {
                ColumnExpr c when names.Contains(c.Column) => c.Table + c.Column,
                ColumnExpr => null,
                _ => $"D{i}",
            };
            var item = new SelectItem(expr, alias);
            names.Add(item.OutputName);
            items.Add(item);
        }

        for (var i = 0; i < measures.Count; i++)
        {
            items.Add(new SelectItem(measures[i], $"M{i}"));
        }

        var groupBy = measures.Count > 0 ? dimensions : [];
        SqlExpr? where = predicates.Count == 0
            ? null
            : predicates.Skip(1).Aggregate(predicates[0], (acc, p) => new BinaryExpr("AND", acc, p));

        var order = items.Take(orderCount).Select((item, i) => new OrderItem(
            item.Alias is null ? item.Expr : new AliasRef(item.Alias), descending[i])).ToArray();

        return new SafeQuery(sources[0], joins, items, where, groupBy, order);
    }

    private static Gen<SqlExpr> Dimension((ColumnExpr Expr, ColumnType Type) column) => column.Type switch
    {
        ColumnType.Date => Gen.Elements<SqlExpr>(
            column.Expr,
            new FunctionExpr("YEAR", false, [column.Expr]),
            new FunctionExpr("MONTH", false, [column.Expr])),
        ColumnType.Text => Gen.Elements<SqlExpr>(
            column.Expr,
            new FunctionExpr("UPPER", false, [column.Expr]),
            new FunctionExpr("LTRIM", false, [column.Expr])),
        _ => Gen.Constant<SqlExpr>(column.Expr),
    };

    private static Gen<SqlExpr> Measure(IReadOnlyList<(ColumnExpr Expr, ColumnType Type)> columns)
    {
        var numeric = columns.Where(c => c.Type == ColumnType.Numeric).Select(c => c.Expr).ToArray();
        var any = columns.Select(c => c.Expr).ToArray();
        var options = new List<Gen<SqlExpr>>
        {
            Gen.Constant<SqlExpr>(new CountStarExpr()),
            Gen.Elements(any).Select(c => (SqlExpr)new FunctionExpr("COUNT", false, [c])),
            Gen.Elements(any).Select(c => (SqlExpr)new FunctionExpr("COUNT", true, [c])),
        };
        if (numeric.Length > 0)
        {
            options.Add(
                from c in Gen.Elements(numeric)
                from f in Gen.Elements("SUM", "AVG", "MIN", "MAX")
                select (SqlExpr)new FunctionExpr(f, false, [c]));
            options.Add(
                from c in Gen.Elements(numeric)
                from k in Gen.Choose(1, 9)
                select (SqlExpr)new FunctionExpr("SUM", false, [new BinaryExpr("*", c, new IntLiteral(k))]));
            options.Add(Gen.Elements(numeric).Select(c => (SqlExpr)new FunctionExpr(
                "ROUND", false, [new FunctionExpr("SUM", false, [c]), new IntLiteral(2)])));
        }

        return Gen.OneOf(options);
    }

    private static Gen<SqlExpr> Predicate((ColumnExpr Expr, ColumnType Type) column) => column.Type switch
    {
        ColumnType.Date => Gen.OneOf(
            Gen.Elements(DateLiterals).Select(d => (SqlExpr)new BinaryExpr(">=", column.Expr, new StringLiteral(d))),
            Gen.Choose(2023, 2024).Select(y => (SqlExpr)new BinaryExpr(
                "=", new FunctionExpr("YEAR", false, [column.Expr]), new IntLiteral(y)))),
        ColumnType.Text => Gen.Elements(TextLiterals).Select(v => (SqlExpr)new BinaryExpr("=", column.Expr, new StringLiteral(v))),
        _ => from op in Gen.Elements(">", ">=", "<")
             from n in Gen.Choose(0, 100)
             select (SqlExpr)new BinaryExpr(op, column.Expr, new IntLiteral(n)),
    };
}
