using AskMyData.Application.Validation;
using AskMyData.Domain.Results;

namespace AskMyData.Infrastructure.Validation.Rules;

/// <summary>
/// Rule 14, COLUMN_NOT_ALLOWED (Requirements 3.15, 3.16): a column reference that does not
/// resolve to an Allowlist column of its base table, a CTE or derived-table output column, or a
/// select-list alias used in ORDER BY.
/// </summary>
internal sealed class ColumnNotAllowedRule : IValidationRule
{
    public int Order => 14;

    public string ReasonCode => ReasonCodes.ColumnNotAllowed;

    public ValidationResult? Check(ValidationContext context) =>
        context.Scopes.ColumnViolations is [var first, ..]
            ? ValidationResult.Fail(ReasonCode, first.Detail)
            : null;
}
