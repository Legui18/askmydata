using AskMyData.Domain.Semantic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.Infrastructure.Validation;

/// <summary>
/// A row source visible inside a <see cref="QueryScope"/>, keyed by its exposed name (the alias,
/// or the table or CTE name when there is no alias).
/// </summary>
internal abstract record ScopeSource(string ExposedName)
{
    /// <summary>True when the source exposes a column with this name (case-insensitive).</summary>
    public abstract bool Exposes(string column);

    protected static bool ContainsName(IEnumerable<string?> names, string column) =>
        names.Any(n => string.Equals(n, column, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A base table or view, resolved with default schema <c>dbo</c>. <see cref="Entry"/> is
/// <c>null</c> when the object is not in the Allowlist; rule 13 reports that case, so such a
/// source accepts every column to avoid a second, less specific failure.
/// </summary>
internal sealed record BaseTableSource(
    string ExposedName, string Schema, string Name, bool HasAlias, AllowlistEntry? Entry)
    : ScopeSource(ExposedName)
{
    public override bool Exposes(string column) =>
        Entry is null || ContainsName(Entry.Columns.Select(c => c.Name), column);
}

/// <summary>A reference to a common table expression; exposes the CTE's output columns.</summary>
internal sealed record CteSource(string ExposedName, string CteName, IReadOnlyList<string?> Columns)
    : ScopeSource(ExposedName)
{
    public override bool Exposes(string column) => ContainsName(Columns, column);
}

/// <summary>A derived table or <c>VALUES</c> constructor; exposes its output columns.</summary>
internal sealed record DerivedSource(string ExposedName, IReadOnlyList<string?> Columns)
    : ScopeSource(ExposedName)
{
    public override bool Exposes(string column) => ContainsName(Columns, column);
}

/// <summary>
/// A table-valued function that rule 10 already allowed through the
/// Allowed_Table_Function_List. Its output shape is not known statically, so it accepts any
/// column name.
/// </summary>
internal sealed record TableFunctionSource(string ExposedName) : ScopeSource(ExposedName)
{
    public override bool Exposes(string column) => true;
}

/// <summary>How a column reference was resolved.</summary>
internal enum ColumnBindingKind
{
    /// <summary>Resolved to a column of a <see cref="ScopeSource"/>.</summary>
    Source,

    /// <summary>A select-list alias used in ORDER BY.</summary>
    SelectAlias,

    /// <summary>A date-part keyword such as <c>year</c> in <c>DATEPART(year, x)</c>.</summary>
    DatePart,
}

/// <summary>
/// The resolution of one column reference. <see cref="Source"/> is set only when the reference
/// resolves to exactly one source; it is <c>null</c> for aliases, date parts and unqualified
/// names exposed by several sources.
/// </summary>
internal sealed record ColumnBinding(ColumnBindingKind Kind, ScopeSource? Source, string Column);

/// <summary>A rule 13 or rule 14 violation found during resolution.</summary>
internal sealed record ScopeViolation(TSqlFragment Fragment, string Detail);

/// <summary>
/// One query scope: a <see cref="QuerySpecification"/>, or the ORDER BY of a set operation.
/// Correlated subqueries resolve through <see cref="Parent"/>.
/// </summary>
internal sealed class QueryScope
{
    public QueryScope(QueryScope? parent, IReadOnlyList<string?> outputNames, IEnumerable<ScopeSource>? sources = null)
    {
        Parent = parent;
        OutputNames = outputNames;
        Sources = sources?.ToList() ?? [];
    }

    public QueryScope? Parent { get; }

    /// <summary>Sources in FROM-clause order.</summary>
    public List<ScopeSource> Sources { get; }

    /// <summary>Select-list output names (alias or column name; <c>null</c> when unnamed).</summary>
    public IReadOnlyList<string?> OutputNames { get; }
}

/// <summary>
/// The result of <see cref="ScopeResolver"/> over one SELECT statement: every scope, the source
/// of every <see cref="NamedTableReference"/>, the binding of every column reference, and the
/// table and column violations in deterministic tree order.
/// </summary>
internal sealed class ScopeModel
{
    public ScopeModel(
        IReadOnlyList<QueryScope> scopes,
        IReadOnlyDictionary<NamedTableReference, ScopeSource> tableBindings,
        IReadOnlyDictionary<ColumnReferenceExpression, ColumnBinding> columnBindings,
        IReadOnlyList<ScopeViolation> tableViolations,
        IReadOnlyList<ScopeViolation> columnViolations)
    {
        Scopes = scopes;
        TableBindings = tableBindings;
        ColumnBindings = columnBindings;
        TableViolations = tableViolations;
        ColumnViolations = columnViolations;
    }

    public static ScopeModel Empty { get; } = new(
        [],
        new Dictionary<NamedTableReference, ScopeSource>(),
        new Dictionary<ColumnReferenceExpression, ColumnBinding>(),
        [],
        []);

    public IReadOnlyList<QueryScope> Scopes { get; }

    /// <summary>Keyed by reference identity.</summary>
    public IReadOnlyDictionary<NamedTableReference, ScopeSource> TableBindings { get; }

    /// <summary>Keyed by reference identity.</summary>
    public IReadOnlyDictionary<ColumnReferenceExpression, ColumnBinding> ColumnBindings { get; }

    /// <summary>TABLE_NOT_ALLOWED findings (rule 13).</summary>
    public IReadOnlyList<ScopeViolation> TableViolations { get; }

    /// <summary>COLUMN_NOT_ALLOWED findings (rule 14).</summary>
    public IReadOnlyList<ScopeViolation> ColumnViolations { get; }
}
