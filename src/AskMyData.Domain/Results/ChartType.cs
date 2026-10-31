namespace AskMyData.Domain.Results;

/// <summary>
/// Suggested chart type for a result. Serialized as <c>bar</c> / <c>line</c> / <c>table-only</c>
/// via <see cref="WireFormat"/>.
/// </summary>
public enum ChartType
{
    Bar,
    Line,
    TableOnly,
}
