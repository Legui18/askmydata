using AskMyData.Application.Execution;
using AskMyData.Application.Validation;

namespace AskMyData.UnitTests.Orchestrator;

/// <summary>
/// IQueryExecutor spy driven by a script. Records every <see cref="ValidatedSql"/> and <see cref="QueryBudget"/>
/// it receives and waits each step's delay on the supplied <see cref="TimeProvider"/> while honoring cancellation.
/// When the script is exhausted the last step repeats.
/// </summary>
public sealed class ScriptedQueryExecutor(IReadOnlyList<ExecutorStep> script, TimeProvider time) : IQueryExecutor
{
    private readonly List<ValidatedSql> _executed = [];
    private readonly List<QueryBudget> _budgets = [];
    private readonly object _gate = new();

    public IReadOnlyList<ValidatedSql> Executed
    {
        get
        {
            lock (_gate)
            {
                return _executed.ToArray();
            }
        }
    }

    public IReadOnlyList<QueryBudget> Budgets
    {
        get
        {
            lock (_gate)
            {
                return _budgets.ToArray();
            }
        }
    }

    public int CallCount => Executed.Count;

    public async Task<OneOf<QueryResult, ExecutionError>> ExecuteAsync(
        ValidatedSql sql,
        QueryBudget budget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ExecutorStep step;
        lock (_gate)
        {
            if (script.Count == 0)
            {
                throw new InvalidOperationException("The executor script is empty.");
            }

            step = script[Math.Min(_executed.Count, script.Count - 1)];
            _executed.Add(sql);
            _budgets.Add(budget);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (step.Delay > TimeSpan.Zero)
        {
            await Task.Delay(step.Delay, time, cancellationToken).ConfigureAwait(false);
        }

        if (step.Error is { } category)
        {
            return new ExecutionError(category, step.Delay);
        }

        return step.Result ?? throw new InvalidOperationException("An executor step needs a result or an error.");
    }
}
