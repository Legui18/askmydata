using AskMyData.Application.Validation;

namespace AskMyData.Application.Execution;

/// <summary>Port that executes validated SQL against SQL Server as the Read_Only_User.</summary>
public interface IQueryExecutor
{
    /// <summary>
    /// Executes <paramref name="sql"/> within <paramref name="budget"/>. Returns a <see cref="QueryResult"/>
    /// or an <see cref="ExecutionError"/>; throws <see cref="OperationCanceledException"/> when
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    Task<OneOf<QueryResult, ExecutionError>> ExecuteAsync(
        ValidatedSql sql,
        QueryBudget budget,
        CancellationToken cancellationToken);
}
