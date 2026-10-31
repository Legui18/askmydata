using AskMyData.Domain.Results;

namespace AskMyData.Application.Execution;

/// <summary>
/// Failed execution of a ValidatedSql. Carries only the category, never SQL Server error text (R5.9, R9.2).
/// </summary>
/// <param name="Category">TIMEOUT or EXECUTION_ERROR.</param>
/// <param name="Duration">Time spent before the failure.</param>
public sealed record ExecutionError(ExecutionErrorCategory Category, TimeSpan Duration);
