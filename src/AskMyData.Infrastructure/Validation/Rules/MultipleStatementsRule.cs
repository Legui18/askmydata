using AskMyData.Application.Validation;
using AskMyData.Domain.Results;

namespace AskMyData.Infrastructure.Validation.Rules;

/// <summary>
/// Rule 2, MULTIPLE_STATEMENTS (Requirement 3.3): more than one statement, counted across all
/// batches including <c>GO</c>-separated ones.
/// </summary>
internal sealed class MultipleStatementsRule : IValidationRule
{
    public int Order => 2;

    public string ReasonCode => ReasonCodes.MultipleStatements;

    public ValidationResult? Check(ValidationContext context) =>
        context.Statements.Count > 1
            ? ValidationResult.Fail(
                ReasonCode, $"Found {context.Statements.Count} statements; exactly one is allowed.")
            : null;
}
