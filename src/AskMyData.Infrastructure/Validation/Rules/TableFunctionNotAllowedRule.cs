using AskMyData.Application.Validation;
using AskMyData.Domain.Results;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.Infrastructure.Validation.Rules;

/// <summary>
/// Rule 10, TABLE_FUNCTION_NOT_ALLOWED (Requirements 3.10, 3.11): a table-valued function that
/// is not in the Allowed_Table_Function_List. Covers schema-object functions
/// (<c>FROM dbo.fn(...)</c>) and built-in rowset functions such as OPENJSON, OPENXML,
/// STRING_SPLIT, GENERATE_SERIES, CONTAINSTABLE, CHANGETABLE and PREDICT.
/// </summary>
/// <remarks>
/// List matching is case-insensitive. Built-ins match by bare name. Schema-object functions
/// match by <c>schema.name</c> (default schema <c>dbo</c>); a bare list entry also matches a
/// function in <c>dbo</c>. Variable method calls (<c>@x.nodes(...)</c>) are never allowed.
/// </remarks>
internal sealed class TableFunctionNotAllowedRule : IValidationRule
{
    public int Order => 10;

    public string ReasonCode => ReasonCodes.TableFunctionNotAllowed;

    public ValidationResult? Check(ValidationContext context)
    {
        var allowed = context.SemanticLayer.AllowedTableFunctions;

        foreach (var node in context.Nodes)
        {
            var (isTableFunction, displayName, permitted) = node switch
            {
                SchemaObjectFunctionTableReference f => SchemaFunction(f.SchemaObject, allowed),
                GlobalFunctionTableReference f => BuiltIn(f.Name.Value, allowed),
                OpenJsonTableReference => BuiltIn("OPENJSON", allowed),
                OpenXmlTableReference => BuiltIn("OPENXML", allowed),
                FullTextTableReference f => BuiltIn(f.FullTextFunctionType.ToString().ToUpperInvariant() + "TABLE", allowed),
                SemanticTableReference f => BuiltIn(f.SemanticFunctionType.ToString().ToUpperInvariant(), allowed),
                ChangeTableChangesTableReference or ChangeTableVersionTableReference => BuiltIn("CHANGETABLE", allowed),
                PredictTableReference => BuiltIn("PREDICT", allowed),
                VectorSearchTableReference => BuiltIn("VECTOR_SEARCH", allowed),
                AIGenerateChunksTableReference => BuiltIn("AI_GENERATE_CHUNKS", allowed),
                VariableMethodCallTableReference => (true, "variable method call", false),
                _ => (false, "", true),
            };

            if (isTableFunction && !permitted)
            {
                return ValidationResult.Fail(ReasonCode, $"Table-valued function '{displayName}' is not allowed.");
            }
        }

        return null;
    }

    private static (bool, string, bool) BuiltIn(string name, IReadOnlySet<string> allowed) =>
        (true, name, allowed.Contains(name));

    private static (bool, string, bool) SchemaFunction(SchemaObjectName name, IReadOnlySet<string> allowed)
    {
        var schema = name.SchemaIdentifier?.Value ?? SqlNames.DefaultSchema;
        var baseName = name.BaseIdentifier.Value;
        var qualified = $"{schema}.{baseName}";
        var permitted = allowed.Contains(qualified)
            || (string.Equals(schema, SqlNames.DefaultSchema, StringComparison.OrdinalIgnoreCase)
                && allowed.Contains(baseName));
        return (true, qualified, permitted);
    }
}
