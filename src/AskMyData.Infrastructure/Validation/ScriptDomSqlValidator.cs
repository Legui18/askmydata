using AskMyData.Application.Validation;
using AskMyData.Domain.Semantic;
using AskMyData.Infrastructure.Validation.Rules;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.Infrastructure.Validation;

/// <summary>
/// <see cref="ISqlValidator"/> built on the ScriptDom T-SQL parser (<see cref="TSql160Parser"/>,
/// SQL Server 2022 grammar). A new parser is created for every call and the validator holds no
/// mutable state, so the same input always gives the same <see cref="ValidationResult"/>
/// (Requirement 3.13).
/// </summary>
/// <remarks>
/// Rules run in this fixed order and the first failing rule determines the Reason_Code
/// (Requirement 3.22):
/// <list type="number">
///   <item><description>PARSE_ERROR: parse error, empty or whitespace input, or no statement.</description></item>
///   <item><description>MULTIPLE_STATEMENTS: more than one statement across all batches, including <c>GO</c> batches.</description></item>
///   <item><description>NOT_SELECT_ONLY: not a SELECT (EXEC, system procedures, DML, DDL, SET, DECLARE) or SELECT INTO.</description></item>
///   <item><description>COMMENT_NOT_ALLOWED: a <c>--</c> or <c>/* */</c> token in the token stream.</description></item>
///   <item><description>OPENROWSET_NOT_ALLOWED: any OPENROWSET form, or OPENDATASOURCE.</description></item>
///   <item><description>OPENQUERY_NOT_ALLOWED: OPENQUERY.</description></item>
///   <item><description>LINKED_SERVER_NOT_ALLOWED: a four-part name with a server part.</description></item>
///   <item><description>CROSS_DATABASE_NOT_ALLOWED: a three-part name with a database part.</description></item>
///   <item><description>SYSTEM_OBJECT_NOT_ALLOWED: an object or function in schema <c>sys</c> or <c>INFORMATION_SCHEMA</c>.</description></item>
///   <item><description>TABLE_FUNCTION_NOT_ALLOWED: a schema-object or built-in table-valued function not in the Allowed_Table_Function_List.</description></item>
///   <item><description>FUNCTION_NOT_ALLOWED: a schema-qualified scalar or aggregate function; unqualified built-ins pass.</description></item>
///   <item><description>SELECT_STAR: a <c>*</c> or <c>t.*</c> select item; <c>COUNT(*)</c> passes.</description></item>
///   <item><description>TABLE_NOT_ALLOWED: a base table or view outside the Allowlist (task 4.2).</description></item>
///   <item><description>COLUMN_NOT_ALLOWED: a column outside the Allowlist of its resolved source (task 4.2).</description></item>
///   <item><description>METRIC_DEFINITION_MISMATCH: a declared Metric that is unknown or not a sub-tree of the query (task 4.3).</description></item>
/// </list>
/// Rules 13 to 15 are added to <see cref="Rules"/> by tasks 4.2 and 4.3; each one implements
/// <see cref="IValidationRule"/> with its order number, and the list is sorted by order.
/// </remarks>
public sealed class ScriptDomSqlValidator : ISqlValidator
{
    private readonly SemanticLayer _semanticLayer;

    public ScriptDomSqlValidator(SemanticLayer semanticLayer)
    {
        ArgumentNullException.ThrowIfNull(semanticLayer);
        _semanticLayer = semanticLayer;
    }

    /// <summary>The rules in evaluation order. All rules are stateless singletons.</summary>
    internal static IReadOnlyList<IValidationRule> Rules { get; } = new IValidationRule[]
    {
        new ParseErrorRule(),
        new MultipleStatementsRule(),
        new NotSelectOnlyRule(),
        new CommentNotAllowedRule(),
        new OpenRowsetNotAllowedRule(),
        new OpenQueryNotAllowedRule(),
        new LinkedServerNotAllowedRule(),
        new CrossDatabaseNotAllowedRule(),
        new SystemObjectNotAllowedRule(),
        new TableFunctionNotAllowedRule(),
        new FunctionNotAllowedRule(),
        new SelectStarRule(),
        // Extension point: rule 13 TableNotAllowedRule and rule 14 ColumnNotAllowedRule (task 4.2),
        // rule 15 MetricDefinitionMismatchRule (task 4.3).
    }.OrderBy(r => r.Order).ToArray();

    /// <inheritdoc />
    public ValidationResult Validate(string sql, IReadOnlyList<string> metricUsage)
    {
        sql ??= string.Empty;
        metricUsage ??= [];

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        TSqlFragment? fragment;
        IList<ParseError> errors;
        using (var reader = new StringReader(sql))
        {
            fragment = parser.Parse(reader, out errors);
        }

        var script = fragment as TSqlScript ?? new TSqlScript();
        var context = new ValidationContext(
            sql, script, errors?.ToArray() ?? [], _semanticLayer, metricUsage);

        foreach (var rule in Rules)
        {
            if (rule.Check(context) is { } failure)
            {
                return failure;
            }
        }

        return ValidationResult.Pass();
    }
}
