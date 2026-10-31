using AskMyData.Application.Validation;
using AskMyData.Domain.Results;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.Infrastructure.Validation.Rules;

/// <summary>
/// Rule 3, NOT_SELECT_ONLY (Requirement 3.2): the single statement is not a plain
/// <see cref="SelectStatement"/> (EXEC, system procedure calls, DML, DDL, SET, DECLARE, ...),
/// or it is a SELECT INTO. Composable DML (<c>FROM (MERGE ... OUTPUT ...)</c>) is only valid as
/// the source of an INSERT, so it is rejected here as a non-SELECT statement.
/// </summary>
internal sealed class NotSelectOnlyRule : IValidationRule
{
    public int Order => 3;

    public string ReasonCode => ReasonCodes.NotSelectOnly;

    public ValidationResult? Check(ValidationContext context)
    {
        var statement = context.Statements[0];

        if (statement.GetType() != typeof(SelectStatement))
        {
            return ValidationResult.Fail(
                ReasonCode, $"Statement type '{statement.GetType().Name}' is not allowed; only SELECT is.");
        }

        return ((SelectStatement)statement).Into is not null
            ? ValidationResult.Fail(ReasonCode, "SELECT INTO is not allowed.")
            : null;
    }
}
