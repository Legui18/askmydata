using AskMyData.Application.Validation;
using AskMyData.Domain.Results;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.Infrastructure.Validation.Rules;

/// <summary>
/// Rule 12, SELECT_STAR (Requirement 3.14): any <c>*</c> or <c>t.*</c> select item in any select
/// list. <c>COUNT(*)</c> is a wildcard column parameter of a function call, not a
/// <see cref="SelectStarExpression"/>, and passes.
/// </summary>
internal sealed class SelectStarRule : IValidationRule
{
    public int Order => 12;

    public string ReasonCode => ReasonCodes.SelectStar;

    public ValidationResult? Check(ValidationContext context) =>
        context.Nodes.OfType<SelectStarExpression>().Any()
            ? ValidationResult.Fail(ReasonCode, "SELECT * is not allowed; list the columns explicitly.")
            : null;
}
