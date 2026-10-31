using System.Globalization;

namespace AskMyData.UnitTests.Generators;

/// <summary>Scalar or boolean expression of the generator AST.</summary>
public abstract record SqlExpr;

/// <summary>A column of an Allowlist table; the printer qualifies it with the alias or table name in scope.</summary>
public sealed record ColumnExpr(string Table, string Column) : SqlExpr;

public sealed record IntLiteral(int Value) : SqlExpr;

/// <summary>A string literal. Values never contain spaces, so whitespace rewrites cannot alter them.</summary>
public sealed record StringLiteral(string Value) : SqlExpr;

/// <summary>An unqualified built-in function call, optionally with DISTINCT.</summary>
public sealed record FunctionExpr(string Name, bool Distinct, IReadOnlyList<SqlExpr> Args) : SqlExpr
{
    public override string ToString() =>
        $"{Name}({(Distinct ? "DISTINCT " : string.Empty)}{string.Join(", ", Args)})";
}

public sealed record CountStarExpr : SqlExpr;

/// <summary>Binary operator: arithmetic (+ - * /), comparison (= &gt; &gt;= &lt;) or AND.</summary>
public sealed record BinaryExpr(string Op, SqlExpr Left, SqlExpr Right) : SqlExpr;

/// <summary>Reference to a select-list alias (only used in ORDER BY).</summary>
public sealed record AliasRef(string Name) : SqlExpr;

/// <summary>A table source. <paramref name="IsWrapper"/> marks the CTE or derived table that wraps a query.</summary>
public sealed record TableSource(string Table, string? Alias, bool IsWrapper = false);

public sealed record JoinClause(string Kind, TableSource Source, ColumnExpr Left, ColumnExpr Right);

/// <summary>A select item. Every non-column item has an alias, so every item has an output name.</summary>
public sealed record SelectItem(SqlExpr Expr, string? Alias)
{
    public string OutputName => Alias ?? (Expr as ColumnExpr)?.Column
        ?? throw new InvalidOperationException("A non-column select item needs an alias.");
}

public sealed record OrderItem(SqlExpr Expr, bool Descending);

/// <summary>A generated single SELECT over Allowlist tables joined along <see cref="DatasetModel.Edges"/>.</summary>
public sealed record SafeQuery(
    TableSource From,
    IReadOnlyList<JoinClause> Joins,
    IReadOnlyList<SelectItem> Select,
    SqlExpr? Where,
    IReadOnlyList<SqlExpr> GroupBy,
    IReadOnlyList<OrderItem> OrderBy)
{
    public IEnumerable<TableSource> Sources => new[] { From }.Concat(Joins.Select(j => j.Source));

    public override string ToString() => SqlPrinter.Print(this, PrintStyle.Default);
}

public enum IdentifierCase
{
    AsWritten,
    Upper,
    Lower,
}

public enum KeywordCase
{
    Upper,
    Lower,
    Capitalized,
}

public enum WrapKind
{
    None,
    Cte,
    DerivedTable,
}

/// <summary>
/// Printing options that are structural rewrites: none of them changes which objects and columns
/// a query reads, or its Normalized_Syntax_Tree.
/// </summary>
/// <param name="WhitespaceSeed">0 prints single spaces; any other value replaces spaces outside literals deterministically.</param>
public sealed record PrintStyle(
    bool Brackets,
    IdentifierCase IdentifierCase,
    KeywordCase KeywordCase,
    bool IncludeDboSchema,
    bool ExtraParentheses,
    int WhitespaceSeed,
    WrapKind Wrap,
    bool AliasAsKeyword)
{
    public static PrintStyle Default { get; } =
        new(false, IdentifierCase.AsWritten, KeywordCase.Upper, true, false, 0, WrapKind.None, true);
}

/// <summary>Renders text for one injected select item, table source or predicate, given the printer and the sources in scope.</summary>
public delegate string SqlFragment(SqlPrinter printer, IReadOnlyList<TableSource> scopeSources);

/// <summary>Where an AST-level violation is injected, or <see cref="Text"/> for transforms of the printed text.</summary>
public enum InjectionPosition
{
    TopLevel,
    Cte,
    DerivedTable,
    Subquery,
    Text,
}

/// <summary>Extra select items, table sources or an INTO target added to the query spec at <see cref="Position"/>.</summary>
public sealed record QueryInjection(
    InjectionPosition Position,
    IReadOnlyList<SqlFragment> ExtraSelectItems,
    IReadOnlyList<SqlFragment> ExtraSources,
    string? IntoTarget = null);

/// <summary>Prints the generator AST to T-SQL under a <see cref="PrintStyle"/>.</summary>
public sealed class SqlPrinter(PrintStyle style)
{
    public const string WrapperName = "w";

    private static readonly string[] WhitespacePool = [" ", "  ", "\n", "\t", "\r\n  ", " \t "];

    public PrintStyle Style { get; } = style;

    public static string Print(SafeQuery query, PrintStyle style, IReadOnlyList<QueryInjection>? injections = null) =>
        new SqlPrinter(style).Print(query, injections);

    public string Print(SafeQuery query, IReadOnlyList<QueryInjection>? injections = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = injections ?? [];
        var top = all.Where(i => i.Position == InjectionPosition.TopLevel).ToArray();
        var inner = all.Where(i => i.Position is InjectionPosition.Cte or InjectionPosition.DerivedTable).ToArray();
        var sub = all.Where(i => i.Position == InjectionPosition.Subquery).ToArray();
        var into = all.Select(i => i.IntoTarget).FirstOrDefault(t => t is not null);

        string text;
        if (Style.Wrap == WrapKind.None)
        {
            text = Spec(query, [.. top, .. inner], sub, into, includeOrderBy: true);
        }
        else
        {
            var body = Spec(query, inner, sub, into: null, includeOrderBy: false);
            var outer = OuterSpec(query, top, into, body);
            text = Style.Wrap == WrapKind.Cte
                ? $"{Keyword("WITH")} {Ident(WrapperName)} {Keyword("AS")} ({body}) {outer}"
                : outer;
        }

        return ApplyWhitespace(text);
    }

    public string Ident(string name)
    {
        var cased = Style.IdentifierCase switch
        {
            IdentifierCase.Upper => name.ToUpperInvariant(),
            IdentifierCase.Lower => name.ToLowerInvariant(),
            _ => name,
        };
        return Style.Brackets ? $"[{cased.Replace("]", "]]", StringComparison.Ordinal)}]" : cased;
    }

    public string Keyword(string words) => string.Join(" ", words.Split(' ').Select(w => Style.KeywordCase switch
    {
        KeywordCase.Lower => w.ToLowerInvariant(),
        KeywordCase.Capitalized => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant(),
        _ => w.ToUpperInvariant(),
    }));

    public string TableName(string table) =>
        Style.IncludeDboSchema ? $"{Ident("dbo")}.{Ident(table)}" : Ident(table);

    public string Qualifier(TableSource source) => source switch
    {
        { IsWrapper: true } => Ident(source.Table),
        { Alias: { } alias } => Ident(alias),
        _ => TableName(source.Table),
    };

    public string Expr(SqlExpr expr, IReadOnlyList<TableSource> scope) => expr switch
    {
        ColumnExpr c => $"{Qualifier(Resolve(c, scope))}.{Ident(c.Column)}",
        IntLiteral i => i.Value.ToString(CultureInfo.InvariantCulture),
        StringLiteral s => $"'{s.Value.Replace("'", "''", StringComparison.Ordinal)}'",
        CountStarExpr => $"{Keyword("COUNT")}(*)",
        FunctionExpr f =>
            $"{Keyword(f.Name)}({(f.Distinct ? Keyword("DISTINCT") + " " : string.Empty)}{string.Join(", ", f.Args.Select(a => Expr(a, scope)))})",
        AliasRef a => Ident(a.Name),
        BinaryExpr b => Binary(b, scope),
        _ => throw new NotSupportedException(expr.GetType().Name),
    };

    private string Binary(BinaryExpr b, IReadOnlyList<TableSource> scope)
    {
        var precedence = Precedence(b.Op);
        var left = Child(b.Left, precedence, isRight: false, scope);
        var right = Child(b.Right, precedence, isRight: true, scope);
        var op = b.Op == "AND" ? Keyword("AND") : b.Op;
        var text = $"{left} {op} {right}";
        return Style.ExtraParentheses && precedence >= 3 ? $"({text})" : text;
    }

    private string Child(SqlExpr child, int parentPrecedence, bool isRight, IReadOnlyList<TableSource> scope)
    {
        var text = Expr(child, scope);
        if (child is BinaryExpr cb)
        {
            var p = Precedence(cb.Op);
            if (p < parentPrecedence || (isRight && p == parentPrecedence))
            {
                return $"({text})";
            }
        }

        return text;
    }

    private static int Precedence(string op) => op switch
    {
        "AND" => 1,
        "=" or ">" or ">=" or "<" or "<=" or "<>" => 2,
        "+" or "-" => 3,
        "*" or "/" => 4,
        _ => throw new NotSupportedException(op),
    };

    private static TableSource Resolve(ColumnExpr column, IReadOnlyList<TableSource> scope) =>
        scope.FirstOrDefault(s => !s.IsWrapper && string.Equals(s.Table, column.Table, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Table '{column.Table}' is not in scope for column '{column.Column}'.");

    private string Source(TableSource source)
    {
        var name = source.IsWrapper ? Ident(source.Table) : TableName(source.Table);
        return source.Alias is null ? name : $"{name} {AliasText(source.Alias)}";
    }

    private string AliasText(string alias) =>
        Style.AliasAsKeyword ? $"{Keyword("AS")} {Ident(alias)}" : Ident(alias);

    private string Item(SelectItem item, IReadOnlyList<TableSource> scope)
    {
        var expr = Expr(item.Expr, scope);
        if (item.Alias is null)
        {
            return expr;
        }

        if (Style.ExtraParentheses)
        {
            expr = $"({expr})";
        }

        return $"{expr} {AliasText(item.Alias)}";
    }

    private string Spec(
        SafeQuery query,
        IReadOnlyList<QueryInjection> injections,
        IReadOnlyList<QueryInjection> subqueryInjections,
        string? into,
        bool includeOrderBy)
    {
        var scope = query.Sources.ToArray();
        var items = query.Select.Select(i => Item(i, scope))
            .Concat(injections.SelectMany(i => i.ExtraSelectItems).Select(f => f(this, scope)));

        var sb = new System.Text.StringBuilder();
        sb.Append(Keyword("SELECT")).Append(' ').Append(string.Join(", ", items));
        if (into is not null)
        {
            sb.Append(' ').Append(Keyword("INTO")).Append(' ').Append(into);
        }

        sb.Append(' ').Append(Keyword("FROM")).Append(' ').Append(Source(query.From));
        foreach (var join in query.Joins)
        {
            sb.Append(' ').Append(Keyword(join.Kind)).Append(' ').Append(Source(join.Source))
                .Append(' ').Append(Keyword("ON")).Append(' ')
                .Append(Expr(join.Left, scope)).Append(" = ").Append(Expr(join.Right, scope));
        }

        foreach (var fragment in injections.SelectMany(i => i.ExtraSources))
        {
            sb.Append(' ').Append(fragment(this, scope));
        }

        var predicates = new List<string>();
        if (query.Where is not null)
        {
            predicates.Add(Expr(query.Where, scope));
        }

        if (subqueryInjections.Count > 0)
        {
            predicates.Add(Subquery(subqueryInjections));
        }

        if (predicates.Count > 0)
        {
            sb.Append(' ').Append(Keyword("WHERE")).Append(' ')
                .Append(string.Join($" {Keyword("AND")} ", predicates));
        }

        if (query.GroupBy.Count > 0)
        {
            sb.Append(' ').Append(Keyword("GROUP BY")).Append(' ')
                .Append(string.Join(", ", query.GroupBy.Select(g => Expr(g, scope))));
        }

        if (includeOrderBy && query.OrderBy.Count > 0)
        {
            sb.Append(' ').Append(Keyword("ORDER BY")).Append(' ')
                .Append(string.Join(", ", query.OrderBy.Select(o => Order(Expr(o.Expr, scope), o.Descending))));
        }

        return sb.ToString();
    }

    private string Subquery(IReadOnlyList<QueryInjection> injections)
    {
        var source = new TableSource("Stores", "sq");
        TableSource[] scope = [source];
        var items = new[] { Expr(new ColumnExpr("Stores", "StoreId"), scope) }
            .Concat(injections.SelectMany(i => i.ExtraSelectItems).Select(f => f(this, scope)));
        var sources = string.Concat(injections.SelectMany(i => i.ExtraSources).Select(f => " " + f(this, scope)));
        return $"{Keyword("EXISTS")} ({Keyword("SELECT")} {string.Join(", ", items)} {Keyword("FROM")} {Source(source)}{sources})";
    }

    private string OuterSpec(SafeQuery query, IReadOnlyList<QueryInjection> injections, string? into, string body)
    {
        var wrapper = new TableSource(WrapperName, null, IsWrapper: true);
        TableSource[] scope = [wrapper];
        var w = Ident(WrapperName);
        var items = query.Select.Select(i => $"{w}.{Ident(i.OutputName)}")
            .Concat(injections.SelectMany(i => i.ExtraSelectItems).Select(f => f(this, scope)));

        var sb = new System.Text.StringBuilder();
        sb.Append(Keyword("SELECT")).Append(' ').Append(string.Join(", ", items));
        if (into is not null)
        {
            sb.Append(' ').Append(Keyword("INTO")).Append(' ').Append(into);
        }

        sb.Append(' ').Append(Keyword("FROM")).Append(' ');
        sb.Append(Style.Wrap == WrapKind.Cte ? w : $"({body}) {AliasText(WrapperName)}");
        foreach (var fragment in injections.SelectMany(i => i.ExtraSources))
        {
            sb.Append(' ').Append(fragment(this, scope));
        }

        if (query.OrderBy.Count > 0)
        {
            sb.Append(' ').Append(Keyword("ORDER BY")).Append(' ')
                .Append(Order($"{w}.{Ident(query.Select[0].OutputName)}", query.OrderBy[0].Descending));
        }

        return sb.ToString();
    }

    private string Order(string expr, bool descending) => descending ? $"{expr} {Keyword("DESC")}" : expr;

    private string ApplyWhitespace(string text)
    {
        if (Style.WhitespaceSeed == 0)
        {
            return text;
        }

        var random = new Random(Style.WhitespaceSeed);
        var sb = new System.Text.StringBuilder(text.Length * 2);
        foreach (var (ch, inLiteral) in SqlText.Scan(text))
        {
            sb.Append(ch == ' ' && !inLiteral ? WhitespacePool[random.Next(WhitespacePool.Length)] : ch.ToString());
        }

        return sb.ToString();
    }
}

/// <summary>Helpers over printed SQL text.</summary>
public static class SqlText
{
    /// <summary>Yields each character with a flag that is true inside a single-quoted literal (quotes included).</summary>
    public static IEnumerable<(char Char, bool InLiteral)> Scan(string text)
    {
        var inLiteral = false;
        foreach (var ch in text)
        {
            if (ch == '\'')
            {
                inLiteral = !inLiteral;
                yield return (ch, true);
                continue;
            }

            yield return (ch, inLiteral);
        }
    }

    /// <summary>Indexes of space characters outside string literals.</summary>
    public static IReadOnlyList<int> SpacesOutsideLiterals(string text)
    {
        var result = new List<int>();
        var index = 0;
        foreach (var (ch, inLiteral) in Scan(text))
        {
            if (ch == ' ' && !inLiteral)
            {
                result.Add(index);
            }

            index++;
        }

        return result;
    }
}
