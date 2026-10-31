using AskMyData.Application.Validation;
using AskMyData.Domain.Results;

namespace AskMyData.Infrastructure.Validation.Rules;

/// <summary>
/// Rule 1, PARSE_ERROR (Requirement 3.1): the parser reported an error, the input is empty or
/// whitespace, or the input contains no statement at all (for example only a comment).
/// </summary>
internal sealed class ParseErrorRule : IValidationRule
{
    public int Order => 1;

    public string ReasonCode => ReasonCodes.ParseError;

    public ValidationResult? Check(ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Sql))
        {
            return ValidationResult.Fail(ReasonCode, "The SQL is empty.");
        }

        if (context.ParseErrors.Count > 0)
        {
            var error = context.ParseErrors[0];
            return ValidationResult.Fail(
                ReasonCode, $"Line {error.Line}, column {error.Column}: {error.Message}");
        }

        return context.Statements.Count == 0
            ? ValidationResult.Fail(ReasonCode, "The SQL contains no statement.")
            : null;
    }
}
