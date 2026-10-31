using AskMyData.Application.Validation;
using AskMyData.Domain.Results;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.Infrastructure.Validation.Rules;

/// <summary>
/// Rule 7, LINKED_SERVER_NOT_ALLOWED (Requirement 3.8): a four-part object name
/// (<c>server.database.schema.object</c>), a five-part column reference, or a function call
/// target with a server part.
/// </summary>
internal sealed class LinkedServerNotAllowedRule : IValidationRule
{
    public int Order => 7;

    public string ReasonCode => ReasonCodes.LinkedServerNotAllowed;

    public ValidationResult? Check(ValidationContext context)
    {
        foreach (var node in context.Nodes)
        {
            var offending = node switch
            {
                SchemaObjectName name when name.ServerIdentifier is not null => name,
                ColumnReferenceExpression { MultiPartIdentifier.Count: >= 5 } column => column.MultiPartIdentifier,
                MultiPartIdentifierCallTarget { MultiPartIdentifier.Count: >= 3 } target => target.MultiPartIdentifier,
                _ => null,
            };

            if (offending is not null)
            {
                return ValidationResult.Fail(
                    ReasonCode, $"Linked server reference '{SqlNames.Format(offending)}' is not allowed.");
            }
        }

        return null;
    }
}
