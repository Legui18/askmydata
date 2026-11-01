using AskMyData.Application.Execution;
using AskMyData.Application.Validation;

namespace AskMyData.Evals.Runner;

/// <summary>
/// Decorator that counts every call that reaches the inner <see cref="IQueryExecutor"/> (R6.9). One instance is
/// created per Eval_Case, so <see cref="ExecutedCount"/> is the number of executions for that case.
/// </summary>
public sealed class CountingQueryExecutor(IQueryExecutor inner) : IQueryExecutor
{
    private int _executedCount;

    public int ExecutedCount => Volatile.Read(ref _executedCount);

    public Task<OneOf<QueryResult, ExecutionError>> ExecuteAsync(
        ValidatedSql sql,
        QueryBudget budget,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _executedCount);
        return inner.ExecuteAsync(sql, budget, cancellationToken);
    }
}
