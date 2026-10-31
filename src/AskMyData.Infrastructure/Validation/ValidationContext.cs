using AskMyData.Domain.Semantic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.Infrastructure.Validation;

/// <summary>
/// Per-call input shared by every <see cref="IValidationRule"/>. Built once by
/// <see cref="ScriptDomSqlValidator.Validate"/> and discarded afterwards, so the validator itself
/// holds no mutable state.
/// </summary>
/// <remarks>
/// Extension point: task 4.2 adds the <c>ScopeModel</c> used by rules 13 and 14, and task 4.3
/// uses <see cref="SemanticLayer"/> and <see cref="MetricUsage"/> for rule 15. Rules after rule 1
/// may assume <see cref="ParseErrors"/> is empty and the script contains exactly one statement
/// from rule 3 on.
/// </remarks>
internal sealed class ValidationContext
{
    private IReadOnlyList<TSqlFragment>? _nodes;

    public ValidationContext(
        string sql,
        TSqlScript script,
        IReadOnlyList<ParseError> parseErrors,
        SemanticLayer semanticLayer,
        IReadOnlyList<string> metricUsage)
    {
        Sql = sql;
        Script = script;
        ParseErrors = parseErrors;
        Tokens = script.ScriptTokenStream is { } tokens ? tokens.ToArray() : [];
        Statements = script.Batches.SelectMany(b => b.Statements).ToArray();
        SemanticLayer = semanticLayer;
        MetricUsage = metricUsage;
    }

    /// <summary>The raw SQL text as submitted.</summary>
    public string Sql { get; }

    /// <summary>The parsed script (possibly partial when <see cref="ParseErrors"/> is non-empty).</summary>
    public TSqlScript Script { get; }

    public IReadOnlyList<ParseError> ParseErrors { get; }

    /// <summary>The full token stream, including comment and whitespace tokens.</summary>
    public IReadOnlyList<TSqlParserToken> Tokens { get; }

    /// <summary>Every statement across all batches (GO-separated batches included).</summary>
    public IReadOnlyList<TSqlStatement> Statements { get; }

    public SemanticLayer SemanticLayer { get; }

    /// <summary>Metric names declared in the Structured_Response.</summary>
    public IReadOnlyList<string> MetricUsage { get; }

    /// <summary>
    /// Every fragment of the script in deterministic depth-first visitor order. Computed on first
    /// use so a PARSE_ERROR exits without walking the tree.
    /// </summary>
    public IReadOnlyList<TSqlFragment> Nodes => _nodes ??= FragmentCollector.Collect(Script);

    private sealed class FragmentCollector : TSqlFragmentVisitor
    {
        private readonly List<TSqlFragment> _nodes = [];

        public static IReadOnlyList<TSqlFragment> Collect(TSqlFragment root)
        {
            var collector = new FragmentCollector();
            root.Accept(collector);
            return collector._nodes;
        }

        public override void Visit(TSqlFragment fragment) => _nodes.Add(fragment);
    }
}
