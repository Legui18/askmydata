namespace AskMyData.Application.Execution;

/// <summary>Successful result of executing a ValidatedSql.</summary>
/// <param name="Columns">Result columns in order.</param>
/// <param name="Rows">At most Row_Limit rows; each row has one value per column.</param>
/// <param name="Truncated">Truncated_Flag: true when more than Row_Limit rows were available (R4.5, R4.6).</param>
/// <param name="Duration">Execution duration.</param>
public sealed record QueryResult(
    IReadOnlyList<ResultColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    bool Truncated,
    TimeSpan Duration);
