using FsCheck;
using FsCheck.Fluent;

namespace AskMyData.UnitTests.Generators;

/// <summary>How table aliases are rewritten.</summary>
public enum AliasRewrite
{
    Keep,
    RemoveAll,
    RenameAll,
    AliasAll,
}

/// <summary>A query after a structural rewrite, with its printed text.</summary>
public sealed record RewrittenQuery(SafeQuery Query, AliasRewrite Aliases, PrintStyle Style)
{
    public string Sql => SqlPrinter.Print(Query, Style);

    public override string ToString() => $"{Aliases}, {Style}: {Sql}";
}

/// <summary>
/// Structural rewrites that must not change a validator outcome (Properties 2 and 5): adding,
/// removing or renaming table aliases, bracket quoting, identifier and keyword case, adding or
/// removing the <c>dbo.</c> schema, extra parentheses, whitespace, and wrapping the query in a CTE
/// or a derived table that projects its named output columns.
/// </summary>
public static class RewriteGen
{
    private static readonly string[] AliasPrefixes = ["t", "x", "src", "a"];

    /// <summary>Any print style, including wrapping.</summary>
    public static Gen<PrintStyle> Style { get; } =
        from brackets in Gen.Elements(true, false)
        from identifierCase in Gen.Elements(IdentifierCase.AsWritten, IdentifierCase.Upper, IdentifierCase.Lower)
        from keywordCase in Gen.Elements(KeywordCase.Upper, KeywordCase.Lower, KeywordCase.Capitalized)
        from dbo in Gen.Elements(true, false)
        from parentheses in Gen.Elements(true, false)
        from whitespace in Gen.Frequency((1, Gen.Constant(0)), (2, Gen.Choose(1, int.MaxValue)))
        from wrap in Gen.Elements(WrapKind.None, WrapKind.Cte, WrapKind.DerivedTable)
        from asKeyword in Gen.Elements(true, false)
        select new PrintStyle(brackets, identifierCase, keywordCase, dbo, parentheses, whitespace, wrap, asKeyword);

    /// <summary>Rewrites table aliases; select-list aliases are unchanged.</summary>
    public static Gen<(SafeQuery Query, AliasRewrite Rewrite)> Aliases(SafeQuery query) =>
        from rewrite in Gen.Elements(AliasRewrite.Keep, AliasRewrite.RemoveAll, AliasRewrite.RenameAll, AliasRewrite.AliasAll)
        from prefix in Gen.Elements(AliasPrefixes)
        select (Apply(query, rewrite, prefix), rewrite);

    /// <summary>An alias rewrite combined with a print style.</summary>
    public static Gen<RewrittenQuery> For(SafeQuery query) =>
        from aliased in Aliases(query)
        from style in Style
        select new RewrittenQuery(aliased.Query, aliased.Rewrite, style);

    public static SafeQuery Apply(SafeQuery query, AliasRewrite rewrite, string prefix)
    {
        if (rewrite == AliasRewrite.Keep)
        {
            return query;
        }

        var index = 0;
        TableSource Map(TableSource source) => rewrite switch
        {
            AliasRewrite.RemoveAll => source with { Alias = null },
            AliasRewrite.RenameAll => source with { Alias = $"{prefix}{index++}" },
            _ => source with { Alias = source.Alias ?? DatasetModel.DefaultAlias(source.Table) },
        };

        var from = Map(query.From);
        var joins = query.Joins.Select(j => j with { Source = Map(j.Source) }).ToArray();
        return query with { From = from, Joins = joins };
    }
}
