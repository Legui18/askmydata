using AskMyData.Application.Validation;

namespace AskMyData.UnitTests.Orchestrator;

/// <summary>
/// ISqlValidator spy wrapping a real validator (normally <c>ScriptDomSqlValidator</c>). Records every call and
/// the SQL text of every call that passed, so tests can compare executed SQL with validator passes.
/// </summary>
public sealed class ValidatorSpy(ISqlValidator inner) : ISqlValidator
{
    private readonly List<(string Sql, ValidationResult Result)> _calls = [];
    private readonly object _gate = new();

    public IReadOnlyList<(string Sql, ValidationResult Result)> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToArray();
            }
        }
    }

    /// <summary>SQL texts that received a pass result, in call order.</summary>
    public IReadOnlyList<string> PassedSql => Calls.Where(c => c.Result.Passed).Select(c => c.Sql).ToArray();

    public ValidationResult Validate(string sql, IReadOnlyList<string> metricUsage)
    {
        var result = inner.Validate(sql, metricUsage);
        lock (_gate)
        {
            _calls.Add((sql, result));
        }

        return result;
    }
}
