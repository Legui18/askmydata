using AskMyData.Application.Validation;
using AskMyData.Domain.Results;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.Infrastructure.Validation.Rules;

/// <summary>
/// Rule 4, COMMENT_NOT_ALLOWED (Requirement 3.5): any <c>--</c> or <c>/* */</c> comment token in
/// the token stream.
/// </summary>
internal sealed class CommentNotAllowedRule : IValidationRule
{
    public int Order => 4;

    public string ReasonCode => ReasonCodes.CommentNotAllowed;

    public ValidationResult? Check(ValidationContext context) =>
        context.Tokens.Any(t => t.TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment)
            ? ValidationResult.Fail(ReasonCode, "SQL comments are not allowed.")
            : null;
}
