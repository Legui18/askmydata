using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.Infrastructure.Validation;

/// <summary>
/// Converts ScriptDom fragments into a Normalized_Syntax_Tree of <see cref="NormNode"/>s for the
/// Metric check (Requirements 3.18, 3.21).
/// </summary>
/// <remarks>
/// Normalization rules:
/// <list type="bullet">
///   <item><description>The tree is built from fragments, not text, so comments and whitespace disappear.</description></item>
///   <item><description>Identifiers are lower-cased and their quoting (brackets, double quotes) is dropped; keywords are encoded as enum values, so their case never matters.</description></item>
///   <item><description>Object names without a schema get the default schema <c>dbo</c>.</description></item>
///   <item><description>Table aliases are dropped: a <see cref="NamedTableReference"/> becomes its base table (or CTE) name, and a column reference that the <see cref="ScopeModel"/> resolves to exactly one base table becomes <c>schema.table.column</c>, whether it was written unqualified, alias-qualified or table-qualified.</description></item>
///   <item><description><see cref="ParenthesisExpression"/> nodes are removed; the tree already encodes precedence.</description></item>
///   <item><description>Literals keep their exact text and literal type.</description></item>
///   <item><description>Nothing is reordered, so commutative rewrites such as <c>b*a</c> versus <c>a*b</c> give different trees (KD6).</description></item>
/// </list>
/// Every other fragment is encoded generically: its type name, then one child per public
/// fragment, fragment-list or scalar (enum, bool, number) property in ordinal name order. The
/// normalizer holds no per-call state; the property cache only memoizes reflection metadata.
/// </remarks>
internal sealed class SyntaxTreeNormalizer
{
    private const string ColumnKind = "column";

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

    /// <summary>Normalizes the single SELECT statement of <paramref name="script"/> using <paramref name="scopes"/>.</summary>
    public NormNode NormalizeQuery(TSqlScript script, ScopeModel scopes)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(scopes);

        var statements = script.Batches.SelectMany(b => b.Statements).ToArray();
        return new Walker(scopes).Normalize(statements is [var single] ? single : script);
    }

    /// <summary>
    /// Normalizes a standalone expression, such as a Metric SQL expression parsed with
    /// <see cref="TSqlParser.ParseExpression(TextReader, out IList{ParseError})"/>. Columns are
    /// qualified from their written parts only.
    /// </summary>
    public NormNode NormalizeExpression(ScalarExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return new Walker(ScopeModel.Empty).Normalize(expression);
    }

    private sealed class Walker(ScopeModel scopes)
    {
        public NormNode Normalize(TSqlFragment fragment) => fragment switch
        {
            ParenthesisExpression parenthesis => Normalize(parenthesis.Expression),
            Identifier identifier => Leaf(nameof(Identifier), Lower(identifier.Value)),
            SchemaObjectName name => NormalizeObjectName(name),
            ColumnReferenceExpression { ColumnType: ColumnType.Regular, MultiPartIdentifier.Count: > 0 } column =>
                NormalizeColumn(column),
            NamedTableReference table => Generic(table, NormalizeTable(table), skip: [nameof(NamedTableReference.SchemaObject), nameof(NamedTableReference.Alias)]),
            _ => Generic(fragment, null, skip: []),
        };

        private NormNode NormalizeColumn(ColumnReferenceExpression column)
        {
            if (scopes.ColumnBindings.TryGetValue(column, out var binding))
            {
                var name = Lower(binding.Column);
                switch (binding)
                {
                    case { Kind: ColumnBindingKind.DatePart }:
                        return Leaf("datepart", name);
                    case { Kind: ColumnBindingKind.Source, Source: BaseTableSource table }:
                        return Leaf(ColumnKind, $"{Lower(table.Schema)}.{Lower(table.Name)}.{name}");
                    case { Kind: ColumnBindingKind.Source, Source: CteSource cte }:
                        return Leaf("cte-column", $"{Lower(cte.CteName)}.{name}");
                    case { Kind: ColumnBindingKind.Source, Source: { } source }:
                        return Leaf("source-column", $"{Lower(source.ExposedName)}.{name}");
                }
            }

            // Not uniquely resolved (standalone expression, select-list alias, or ambiguous):
            // qualify from the written parts only.
            var parts = column.MultiPartIdentifier.Identifiers.Select(i => Lower(i.Value)).ToArray();
            return parts.Length switch
            {
                1 => Leaf("column-ref", parts[0]),
                2 => Leaf(ColumnKind, $"{SqlNames.DefaultSchema}.{parts[0]}.{parts[1]}"),
                _ => Leaf(ColumnKind, string.Join(".", parts)),
            };
        }

        private NormNode NormalizeTable(NamedTableReference table)
        {
            if (scopes.TableBindings.TryGetValue(table, out var source))
            {
                switch (source)
                {
                    case BaseTableSource baseTable:
                        return Leaf("table", $"{Lower(baseTable.Schema)}.{Lower(baseTable.Name)}");
                    case CteSource cte:
                        return Leaf("cte", Lower(cte.CteName));
                }
            }

            return NormalizeObjectName(table.SchemaObject);
        }

        private static NormNode NormalizeObjectName(SchemaObjectName name)
        {
            var parts = new List<string>();
            if (name.ServerIdentifier is { } server)
            {
                parts.Add(Lower(server.Value));
            }

            if (name.DatabaseIdentifier is { } database)
            {
                parts.Add(Lower(database.Value));
            }

            parts.Add(string.IsNullOrEmpty(name.SchemaIdentifier?.Value)
                ? SqlNames.DefaultSchema
                : Lower(name.SchemaIdentifier.Value));
            parts.Add(Lower(name.BaseIdentifier?.Value ?? string.Empty));
            return Leaf("object", string.Join(".", parts));
        }

        private NormNode Generic(TSqlFragment fragment, NormNode? head, IReadOnlyCollection<string> skip)
        {
            var children = ImmutableArray.CreateBuilder<NormNode>();
            if (head is not null)
            {
                children.Add(head);
            }

            foreach (var property in PropertiesOf(fragment.GetType()))
            {
                if (skip.Contains(property.Name))
                {
                    continue;
                }

                var value = property.GetValue(fragment);
                switch (value)
                {
                    case TSqlFragment child:
                        children.Add(new NormNode(property.Name, null, [Normalize(child)]));
                        break;
                    case IEnumerable<TSqlFragment> list:
                        children.Add(new NormNode(property.Name, null, [.. list.Select(Normalize)]));
                        break;
                    case null:
                        children.Add(Leaf(property.Name, null));
                        break;
                    case string text:
                        // Only literal text is kept, exactly as written.
                        children.Add(Leaf(property.Name, text));
                        break;
                    default:
                        children.Add(Leaf(property.Name, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)));
                        break;
                }
            }

            return new NormNode(fragment.GetType().Name, null, children.ToImmutable());
        }

        private static PropertyInfo[] PropertiesOf(Type type) => PropertyCache.GetOrAdd(type, static t => t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0
                        && p.DeclaringType != typeof(TSqlFragment)
                        && IsEncoded(t, p.PropertyType))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray());

        private static bool IsEncoded(Type owner, Type propertyType)
        {
            if (typeof(TSqlFragment).IsAssignableFrom(propertyType)
                || typeof(IEnumerable<TSqlFragment>).IsAssignableFrom(propertyType))
            {
                return true;
            }

            var scalar = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (scalar.IsEnum || scalar.IsPrimitive)
            {
                return true;
            }

            // Literal text is semantic; other string properties are derived from child identifiers.
            return scalar == typeof(string) && typeof(Literal).IsAssignableFrom(owner);
        }

        private static NormNode Leaf(string kind, string? value) => new(kind, value, []);

        private static string Lower(string value) => value.ToLowerInvariant();
    }
}
