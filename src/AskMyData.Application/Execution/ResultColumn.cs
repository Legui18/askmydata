namespace AskMyData.Application.Execution;

/// <summary>One column of a query result.</summary>
/// <param name="Name">Column name as returned by SQL Server.</param>
/// <param name="SqlType">SQL Server data type name (for example <c>nvarchar</c>).</param>
/// <param name="Kind">Coarse kind derived from <paramref name="SqlType"/>.</param>
public sealed record ResultColumn(string Name, string SqlType, ColumnKind Kind);
