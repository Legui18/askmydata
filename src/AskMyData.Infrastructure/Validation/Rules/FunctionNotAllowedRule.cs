using AskMyData.Application.Validation;
using AskMyData.Domain.Results;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.Infrastructure.Validation.Rules;

/// <summary>
/// Rule 11, FUNCTION_NOT_ALLOWED (Requirements 3.23, 3.24): any scalar or aggregate
/// <see cref="FunctionCall"/> with a call target, that is a schema-qualified (user-defined or CLR)
/// function or a method call on an expression or type. Unqualified built-in functions pass.
/// </summary>
internal sealed class FunctionNotAllowedRule : IValidationRule
{
    public int Order => 11;

    public string ReasonCode => ReasonCodes.FunctionNotAllowed;

    public ValidationResult? Check(ValidationContext context)
    {
        foreach (var call in context.Nodes.OfType<FunctionCall>())
        {
            if (call.CallTarget is null)
            {
                continue;
            }

            var name = call.CallTarget is MultiPartIdentifierCallTarget target
                ? $"{SqlNames.Format(target.MultiPartIdentifier)}.{call.FunctionName.Value}"
                : call.FunctionName.Value;

            return ValidationResult.Fail(ReasonCode, $"Function '{name}' is not allowed; only built-in functions are.");
        }

        return null;
    }
}
