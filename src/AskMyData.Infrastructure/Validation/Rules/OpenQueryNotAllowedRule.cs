using AskMyData.Application.Validation;
using AskMyData.Domain.Results;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.Infrastructure.Validation.Rules;

/// <summary>Rule 6, OPENQUERY_NOT_ALLOWED (Requirement 3.7).</summary>
internal sealed class OpenQueryNotAllowedRule : IValidationRule
{
    public int Order => 6;

    public string ReasonCode => ReasonCodes.OpenQueryNotAllowed;

    public ValidationResult? Check(ValidationContext context) =>
        context.Nodes.OfType<OpenQueryTableReference>().Any()
            ? ValidationResult.Fail(ReasonCode, "OPENQUERY is not allowed.")
            : null;
}
