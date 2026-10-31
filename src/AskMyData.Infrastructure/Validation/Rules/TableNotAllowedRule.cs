using AskMyData.Application.Validation;
using AskMyData.Domain.Results;

namespace AskMyData.Infrastructure.Validation.Rules;

/// <summary>
/// Rule 13, TABLE_NOT_ALLOWED (Requirements 3.4, 3.15): a <c>NamedTableReference</c> that is not
/// an in-scope CTE name and, resolved with default schema <c>dbo</c>, is not in the Allowlist.
/// Also covers unsupported table sources (for example PIVOT or table variables), which fail closed.
/// </summary>
internal sealed class TableNotAllowedRule : IValidationRule
{
    public int Order => 13;

    public string ReasonCode => ReasonCodes.TableNotAllowed;

    public ValidationResult? Check(ValidationContext context) =>
        context.Scopes.TableViolations is [var first, ..]
            ? ValidationResult.Fail(ReasonCode, first.Detail)
            : null;
}
