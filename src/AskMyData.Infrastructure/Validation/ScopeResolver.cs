using System.Collections.Frozen;
using AskMyData.Domain.Semantic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.Infrastructure.Validation;

/// <summary>
/// Builds the <see cref="ScopeModel"/> for one SELECT statement (Requirements 3.4, 3.15, 3.16).
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description>Every <see cref="QuerySpecification"/> gets its own scope, including those in CTEs, derived tables, subqueries, UNION/EXCEPT/INTERSECT branches and APPLY.</description></item>
///   <item><description>CTE names are visible in the CTE that defines them (recursion), later CTEs and the main query. A <see cref="NamedTableReference"/> without schema that matches a visible CTE is a <see cref="CteSource"/> and is not checked against the Allowlist; the CTE body is resolved as its own scope.</description></item>
///   <item><description>Other named references resolve with default schema <c>dbo</c> to a <see cref="BaseTableSource"/>; one outside the Allowlist is a TABLE_NOT_ALLOWED finding.</description></item>
///   <item><description>Derived-table bodies see the enclosing scope chain but not sibling FROM sources, except on the right side of CROSS/OUTER APPLY, where the sources to the left are visible.</description></item>
///   <item><description>Qualified columns (<c>alias.col</c>, <c>table.col</c>, <c>schema.table.col</c>) resolve to the nearest scope exposing that name; unqualified columns resolve to the nearest scope where at least one source exposes the column. ORDER BY may also use select-list aliases. Anything else is a COLUMN_NOT_ALLOWED finding.</description></item>
///   <item><description>Fail closed: any table or column reference the walk did not reach is reported as a violation.</description></item>
/// </list>
/// </remarks>
internal sealed class ScopeResolver
{
    private static readonly FrozenSet<string> DatePartFunctions = new[]
    {
        "DATEADD", "DATEDIFF", "DATEDIFF_BIG", "DATENAME", "DATEPART", "DATETRUNC", "DATE_BUCKET",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> DateParts = new[]
    {
        "year", "yy", "yyyy", "quarter", "qq", "q", "month", "mm", "m", "dayofyear", "dy", "y",
        "day", "dd", "d", "week", "wk", "ww", "weekday", "dw", "w", "hour", "hh", "minute", "mi",
        "n", "second", "ss", "s", "millisecond", "ms", "microsecond", "mcs", "nanosecond", "ns",
        "tzoffset", "tz", "iso_week", "isowk", "isoww",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly SemanticLayer _semanticLayer;
    private readonly List<QueryScope> _scopes = [];
    private readonly Dictionary<NamedTableReference, ScopeSource> _tables = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ColumnReferenceExpression, ColumnBinding> _columns = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<TSqlFragment> _violating = new(ReferenceEqualityComparer.Instance);
    private readonly List<ScopeViolation> _tableViolations = [];
    private readonly List<ScopeViolation> _columnViolations = [];
    private readonly List<CteSource> _ctes = [];
    private int _visibleCteCount;

    private ScopeResolver(SemanticLayer semanticLayer) => _semanticLayer = semanticLayer;

    /// <summary>
    /// Resolves <paramref name="statement"/>. <paramref name="allNodes"/> is every fragment of the
    /// script and is used for the fail-closed check.
    /// </summary>
    public static ScopeModel Resolve(
        SelectStatement statement, SemanticLayer semanticLayer, IEnumerable<TSqlFragment> allNodes)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(semanticLayer);

        var resolver = new ScopeResolver(semanticLayer);
        resolver.ResolveStatement(statement);
        resolver.FailClosed(allNodes);
        return new ScopeModel(
            resolver._scopes, resolver._tables, resolver._columns,
            resolver._tableViolations, resolver._columnViolations);
    }

    private void ResolveStatement(SelectStatement statement)
    {
        var ctes = statement.WithCtesAndXmlNamespaces?.CommonTableExpressions;
        if (ctes is not null)
        {
            // Output names are syntactic, so they are known before any body is resolved; this
            // lets a recursive CTE reference itself.
            foreach (var cte in ctes)
            {
                var name = cte.ExpressionName.Value;
                IReadOnlyList<string?> columns = cte.Columns.Count > 0
                    ? cte.Columns.Select(c => (string?)c.Value).ToArray()
                    : OutputNames(cte.QueryExpression);
                _ctes.Add(new CteSource(name, name, columns));
            }

            for (var i = 0; i < ctes.Count; i++)
            {
                _visibleCteCount = i + 1;
                ResolveQuery(ctes[i].QueryExpression, parent: null);
            }
        }

        _visibleCteCount = _ctes.Count;
        ResolveQuery(statement.QueryExpression, parent: null);
    }

    private void ResolveQuery(QueryExpression? query, QueryScope? parent)
    {
        switch (query)
        {
            case QuerySpecification spec:
                ResolveSpecification(spec, parent);
                break;
            case BinaryQueryExpression binary:
                ResolveQuery(binary.FirstQueryExpression, parent);
                ResolveQuery(binary.SecondQueryExpression, parent);
                ResolveSetTail(binary, OutputNames(binary), parent);
                break;
            case QueryParenthesisExpression parenthesis:
                ResolveQuery(parenthesis.QueryExpression, parent);
                ResolveSetTail(parenthesis, OutputNames(parenthesis.QueryExpression), parent);
                break;
        }
    }

    // ORDER BY / OFFSET / FOR on a set operation or parenthesized query: only output names
    // (and outer scopes for correlated references) are visible.
    private void ResolveSetTail(QueryExpression query, IReadOnlyList<string?> outputNames, QueryScope? parent)
    {
        if (query.OrderByClause is null && query.OffsetClause is null && query.ForClause is null)
        {
            return;
        }

        var scope = new QueryScope(parent, outputNames);
        _scopes.Add(scope);
        Walk(query.OrderByClause, scope, orderBy: true);
        Walk(query.OffsetClause, scope, orderBy: true);
        Walk(query.ForClause, scope, orderBy: false);
    }

    private void ResolveSpecification(QuerySpecification spec, QueryScope? parent)
    {
        var scope = new QueryScope(parent, OutputNames(spec));
        _scopes.Add(scope);

        if (spec.FromClause is { } from)
        {
            foreach (var reference in from.TableReferences)
            {
                AddTableReference(reference, scope, lateral: false);
            }
        }

        foreach (var element in spec.SelectElements)
        {
            Walk(element, scope, orderBy: false);
        }

        Walk(spec.TopRowFilter, scope, orderBy: false);
        Walk(spec.WhereClause, scope, orderBy: false);
        Walk(spec.GroupByClause, scope, orderBy: false);
        Walk(spec.HavingClause, scope, orderBy: false);
        Walk(spec.WindowClause, scope, orderBy: false);
        Walk(spec.OrderByClause, scope, orderBy: true);
        Walk(spec.OffsetClause, scope, orderBy: true);
        Walk(spec.ForClause, scope, orderBy: false);
    }

    private void AddTableReference(TableReference reference, QueryScope scope, bool lateral)
    {
        switch (reference)
        {
            case QualifiedJoin join:
                AddTableReference(join.FirstTableReference, scope, lateral);
                AddTableReference(join.SecondTableReference, scope, lateral);
                Walk(join.SearchCondition, scope, orderBy: false);
                break;

            case UnqualifiedJoin join:
                AddTableReference(join.FirstTableReference, scope, lateral);
                var apply = join.UnqualifiedJoinType is UnqualifiedJoinType.CrossApply or UnqualifiedJoinType.OuterApply;
                AddTableReference(join.SecondTableReference, scope, lateral || apply);
                break;

            case JoinParenthesisTableReference parenthesis:
                AddTableReference(parenthesis.Join, scope, lateral);
                break;

            case NamedTableReference named:
                AddNamedTable(named, scope);
                break;

            case QueryDerivedTable derived:
                ResolveQuery(derived.QueryExpression, BodyParent(scope, lateral));
                scope.Sources.Add(new DerivedSource(
                    derived.Alias?.Value ?? string.Empty,
                    derived.Columns.Count > 0
                        ? derived.Columns.Select(c => (string?)c.Value).ToArray()
                        : OutputNames(derived.QueryExpression)));
                break;

            case InlineDerivedTable values:
                foreach (var row in values.RowValues)
                {
                    Walk(row, BodyParent(scope, lateral) ?? new QueryScope(null, []), orderBy: false);
                }

                scope.Sources.Add(new DerivedSource(
                    values.Alias?.Value ?? string.Empty,
                    values.Columns.Select(c => (string?)c.Value).ToArray()));
                break;

            case TableReferenceWithAlias function when IsTableFunction(function):
                // Rule 10 has already rejected functions outside the Allowed_Table_Function_List.
                // Arguments may reference sources to the left (APPLY), so resolve them laterally.
                Walk(function, Snapshot(scope), orderBy: false);
                scope.Sources.Add(new TableFunctionSource(function.Alias?.Value ?? FunctionName(function)));
                break;

            default:
                AddViolation(_tableViolations, reference,
                    $"Table source '{reference.GetType().Name}' is not supported.");
                break;
        }
    }

    private void AddNamedTable(NamedTableReference named, QueryScope scope)
    {
        var name = named.SchemaObject;
        var alias = named.Alias?.Value;
        ScopeSource source;

        if (name.SchemaIdentifier is null && name.DatabaseIdentifier is null && name.ServerIdentifier is null
            && TryGetVisibleCte(name.BaseIdentifier.Value, out var cte))
        {
            source = new CteSource(alias ?? cte.CteName, cte.CteName, cte.Columns);
        }
        else
        {
            var schema = string.IsNullOrEmpty(name.SchemaIdentifier?.Value)
                ? SqlNames.DefaultSchema
                : name.SchemaIdentifier.Value;
            var baseName = name.BaseIdentifier.Value;
            var allowed = _semanticLayer.TryGetObject(schema, baseName, out var entry);
            source = new BaseTableSource(alias ?? baseName, schema, baseName, alias is not null, allowed ? entry : null);
            if (!allowed)
            {
                AddViolation(_tableViolations, named, $"Table '{schema}.{baseName}' is not in the Allowlist.");
            }
        }

        _tables[named] = source;
        scope.Sources.Add(source);
    }

    private bool TryGetVisibleCte(string name, out CteSource cte)
    {
        for (var i = 0; i < _visibleCteCount; i++)
        {
            if (string.Equals(_ctes[i].CteName, name, StringComparison.OrdinalIgnoreCase))
            {
                cte = _ctes[i];
                return true;
            }
        }

        cte = null!;
        return false;
    }

    private void ResolveColumn(ColumnReferenceExpression column, QueryScope scope, bool orderBy)
    {
        if (column.ColumnType == ColumnType.Wildcard
            || (_columns.TryGetValue(column, out var existing) && existing.Kind == ColumnBindingKind.DatePart))
        {
            // COUNT(*); SELECT * and t.* are rejected by rule 12.
            return;
        }

        var identifiers = column.MultiPartIdentifier?.Identifiers;
        if (column.ColumnType != ColumnType.Regular || identifiers is null || identifiers.Count == 0)
        {
            AddViolation(_columnViolations, column, "Pseudo-columns are not allowed.");
            return;
        }

        var name = identifiers[^1].Value;
        var display = SqlNames.Format(column.MultiPartIdentifier!);

        if (identifiers.Count == 1)
        {
            if (orderBy && scope.OutputNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            {
                _columns[column] = new ColumnBinding(ColumnBindingKind.SelectAlias, null, name);
                return;
            }

            for (var current = scope; current is not null; current = current.Parent)
            {
                var matches = current.Sources.Where(s => s.Exposes(name)).Take(2).ToArray();
                if (matches.Length > 0)
                {
                    _columns[column] = new ColumnBinding(
                        ColumnBindingKind.Source, matches.Length == 1 ? matches[0] : null, name);
                    return;
                }
            }

            AddViolation(_columnViolations, column, $"Column '{display}' does not resolve to an allowed column.");
            return;
        }

        if (identifiers.Count is 2 or 3)
        {
            for (var current = scope; current is not null; current = current.Parent)
            {
                var source = current.Sources.FirstOrDefault(s => QualifierMatches(s, identifiers));
                if (source is null)
                {
                    continue;
                }

                if (source.Exposes(name))
                {
                    _columns[column] = new ColumnBinding(ColumnBindingKind.Source, source, name);
                }
                else
                {
                    AddViolation(_columnViolations, column, $"Column '{display}' is not an allowed column of '{source.ExposedName}'.");
                }

                return;
            }
        }

        AddViolation(_columnViolations, column, $"Column '{display}' does not resolve to an allowed column.");
    }

    private static bool QualifierMatches(ScopeSource source, IList<Identifier> identifiers)
    {
        if (identifiers.Count == 2)
        {
            return string.Equals(source.ExposedName, identifiers[0].Value, StringComparison.OrdinalIgnoreCase);
        }

        // schema.table.column: only an unaliased base table is addressable this way.
        return source is BaseTableSource { HasAlias: false } table
            && string.Equals(table.Schema, identifiers[0].Value, StringComparison.OrdinalIgnoreCase)
            && string.Equals(table.Name, identifiers[1].Value, StringComparison.OrdinalIgnoreCase);
    }

    private void FailClosed(IEnumerable<TSqlFragment> allNodes)
    {
        foreach (var node in allNodes)
        {
            switch (node)
            {
                case NamedTableReference named when !_tables.ContainsKey(named) && !_violating.Contains(named):
                    AddViolation(_tableViolations, named,
                        $"Table '{SqlNames.Format(named.SchemaObject)}' could not be resolved.");
                    break;
                case ColumnReferenceExpression column
                    when column.ColumnType != ColumnType.Wildcard
                         && !_columns.ContainsKey(column) && !_violating.Contains(column):
                    AddViolation(_columnViolations, column, "Column reference could not be resolved.");
                    break;
            }
        }
    }

    private void AddViolation(List<ScopeViolation> list, TSqlFragment fragment, string detail)
    {
        if (_violating.Add(fragment))
        {
            list.Add(new ScopeViolation(fragment, detail));
        }
    }

    private void Walk(TSqlFragment? fragment, QueryScope scope, bool orderBy) =>
        fragment?.Accept(new ExpressionWalker(this, scope, orderBy));

    private static QueryScope? BodyParent(QueryScope scope, bool lateral) =>
        lateral ? Snapshot(scope) : scope.Parent;

    // The sources collected so far, for lateral (APPLY) resolution.
    private static QueryScope Snapshot(QueryScope scope) =>
        new(scope.Parent, scope.OutputNames, scope.Sources);

    private static bool IsTableFunction(TableReference reference) => reference is
        SchemaObjectFunctionTableReference or GlobalFunctionTableReference or OpenJsonTableReference
        or OpenXmlTableReference or FullTextTableReference or SemanticTableReference
        or ChangeTableChangesTableReference or ChangeTableVersionTableReference or PredictTableReference
        or VectorSearchTableReference or AIGenerateChunksTableReference;

    private static string FunctionName(TableReference reference) => reference switch
    {
        SchemaObjectFunctionTableReference f => f.SchemaObject.BaseIdentifier.Value,
        GlobalFunctionTableReference f => f.Name.Value,
        _ => string.Empty,
    };

    /// <summary>Select-list output names: alias, else the column name of a bare column reference.</summary>
    internal static IReadOnlyList<string?> OutputNames(QueryExpression? query) => query switch
    {
        QuerySpecification spec => spec.SelectElements.Select(e => e switch
        {
            SelectScalarExpression { ColumnName: { } alias } => alias.Value,
            SelectScalarExpression { Expression: ColumnReferenceExpression { MultiPartIdentifier: { Count: > 0 } id } } =>
                id.Identifiers[^1].Value,
            _ => null,
        }).ToArray(),
        BinaryQueryExpression binary => OutputNames(binary.FirstQueryExpression),
        QueryParenthesisExpression parenthesis => OutputNames(parenthesis.QueryExpression),
        _ => [],
    };

    /// <summary>
    /// Resolves column references in an expression tree; nested queries (subqueries) are resolved
    /// as child scopes of the current scope, so correlated references see outer sources.
    /// </summary>
    private sealed class ExpressionWalker(ScopeResolver resolver, QueryScope scope, bool orderBy) : TSqlFragmentVisitor
    {
        public override void ExplicitVisit(QuerySpecification node) => resolver.ResolveQuery(node, scope);

        public override void ExplicitVisit(BinaryQueryExpression node) => resolver.ResolveQuery(node, scope);

        public override void ExplicitVisit(QueryParenthesisExpression node) => resolver.ResolveQuery(node, scope);

        public override void ExplicitVisit(ColumnReferenceExpression node) => resolver.ResolveColumn(node, scope, orderBy);

        public override void ExplicitVisit(FunctionCall node)
        {
            if (node.CallTarget is null
                && DatePartFunctions.Contains(node.FunctionName.Value)
                && node.Parameters.Count > 0
                && node.Parameters[0] is ColumnReferenceExpression
                {
                    ColumnType: ColumnType.Regular,
                    MultiPartIdentifier: { Count: 1 } id,
                } datePart
                && DateParts.Contains(id.Identifiers[0].Value))
            {
                resolver._columns[datePart] = new ColumnBinding(ColumnBindingKind.DatePart, null, id.Identifiers[0].Value);
            }

            base.ExplicitVisit(node);
        }
    }
}
