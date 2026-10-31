using AskMyData.Application.Validation;
using AskMyData.Domain.Results;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.Infrastructure.Validation.Rules;

/// <summary>
/// Rule 5, OPENROWSET_NOT_ALLOWED (Requirement 3.6): every OPENROWSET form (provider, BULK,
/// internal, Cosmos) and OPENDATASOURCE, which also opens an ad hoc connection.
/// </summary>
internal sealed class OpenRowsetNotAllowedRule : IValidationRule
{
    public int Order => 5;

    public string ReasonCode => ReasonCodes.OpenRowsetNotAllowed;

    public ValidationResult? Check(ValidationContext context)
    {
        foreach (var node in context.Nodes)
        {
            switch (node)
            {
                case OpenRowsetTableReference or BulkOpenRowset or InternalOpenRowset or OpenRowsetCosmos:
                    return ValidationResult.Fail(ReasonCode, "OPENROWSET is not allowed.");
                case AdHocTableReference or AdHocDataSource:
                    return ValidationResult.Fail(ReasonCode, "OPENDATASOURCE is not allowed.");
            }
        }

        return null;
    }
}
