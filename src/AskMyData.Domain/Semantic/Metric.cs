namespace AskMyData.Domain.Semantic;

/// <summary>
/// A governed business measure (Requirement 1.1).
/// </summary>
/// <param name="Name">Metric name, e.g. "TotalRevenue". Lookups are case-insensitive.</param>
/// <param name="Description">Business description of the Metric.</param>
/// <param name="SqlExpression">The T-SQL expression the Metric is defined as.</param>
/// <param name="DependsOnTables">Schema-qualified Allowlist tables the expression depends on, e.g. "dbo.OrderLines".</param>
public sealed record Metric(
    string Name,
    string Description,
    string SqlExpression,
    IReadOnlyList<string> DependsOnTables);
