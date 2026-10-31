using AskMyData.Application.Validation;
using AskMyData.Domain.Results;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.Infrastructure.Validation.Rules;

/// <summary>
/// Rule 9, SYSTEM_OBJECT_NOT_ALLOWED (Requirement 3.17): any object, column qualifier or function
/// whose schema is <c>sys</c> or <c>INFORMATION_SCHEMA</c> (case-insensitive, quoting removed),
/// including <c>sys.fn_*</c> functions and the legacy <c>::fn_*</c> built-in rowset syntax.
/// </summary>
internal sealed class SystemObjectNotAllowedRule : IValidationRule
{
    public int Order => 9;

    public string ReasonCode => ReasonCodes.SystemObjectNotAllowed;

    public ValidationResult? Check(ValidationContext context)
    {
        foreach (var node in context.Nodes)
        {
            string? offending = node switch
            {
                SchemaObjectName name when SqlNames.IsSystemSchema(name.SchemaIdentifier?.Value) =>
                    SqlNames.Format(name),
                ColumnReferenceExpression { MultiPartIdentifier: { Count: >= 3 } parts }
                    when SqlNames.IsSystemSchema(parts.Identifiers[parts.Count - 3].Value) =>
                    SqlNames.Format(parts),
                MultiPartIdentifierCallTarget { MultiPartIdentifier: { Count: >= 1 } target }
                    when SqlNames.IsSystemSchema(target.Identifiers[target.Count - 1].Value) =>
                    SqlNames.Format(target),
                BuiltInFunctionTableReference builtIn => "::" + builtIn.Name.Value,
                _ => null,
            };

            if (offending is not null)
            {
                return ValidationResult.Fail(ReasonCode, $"System object '{offending}' is not allowed.");
            }
        }

        return null;
    }
}
