using AskMyData.Application.Validation;
using AskMyData.Domain.Results;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.Infrastructure.Validation.Rules;

/// <summary>
/// Rule 8, CROSS_DATABASE_NOT_ALLOWED (Requirement 3.9): a three-part object name
/// (<c>database.schema.object</c>, including <c>database..object</c>), a four-part column
/// reference, or a function call target with a database part.
/// </summary>
internal sealed class CrossDatabaseNotAllowedRule : IValidationRule
{
    public int Order => 8;

    public string ReasonCode => ReasonCodes.CrossDatabaseNotAllowed;

    public ValidationResult? Check(ValidationContext context)
    {
        foreach (var node in context.Nodes)
        {
            var offending = node switch
            {
                SchemaObjectName name when name.DatabaseIdentifier is not null => name,
                ColumnReferenceExpression { MultiPartIdentifier.Count: 4 } column => column.MultiPartIdentifier,
                MultiPartIdentifierCallTarget { MultiPartIdentifier.Count: 2 } target => target.MultiPartIdentifier,
                _ => null,
            };

            if (offending is not null)
            {
                return ValidationResult.Fail(
                    ReasonCode, $"Cross-database reference '{SqlNames.Format(offending)}' is not allowed.");
            }
        }

        return null;
    }
}
