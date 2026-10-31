namespace AskMyData.Domain.Results;

/// <summary>
/// Converts result enums to and from their serialized (wire) string forms.
/// Parsing is exact and case-sensitive.
/// </summary>
public static class WireFormat
{
    public const string Timeout = "TIMEOUT";
    public const string ExecutionError = "EXECUTION_ERROR";
    public const string Bar = "bar";
    public const string Line = "line";
    public const string TableOnly = "table-only";

    public static string ToWireString(this ExecutionErrorCategory category) => category switch
    {
        ExecutionErrorCategory.Timeout => Timeout,
        ExecutionErrorCategory.ExecutionError => ExecutionError,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown execution error category."),
    };

    public static string ToWireString(this ChartType chartType) => chartType switch
    {
        ChartType.Bar => Bar,
        ChartType.Line => Line,
        ChartType.TableOnly => TableOnly,
        _ => throw new ArgumentOutOfRangeException(nameof(chartType), chartType, "Unknown chart type."),
    };

    public static bool TryParseExecutionErrorCategory(string? value, out ExecutionErrorCategory category)
    {
        switch (value)
        {
            case Timeout:
                category = ExecutionErrorCategory.Timeout;
                return true;
            case ExecutionError:
                category = ExecutionErrorCategory.ExecutionError;
                return true;
            default:
                category = default;
                return false;
        }
    }

    public static bool TryParseChartType(string? value, out ChartType chartType)
    {
        switch (value)
        {
            case Bar:
                chartType = ChartType.Bar;
                return true;
            case Line:
                chartType = ChartType.Line;
                return true;
            case TableOnly:
                chartType = ChartType.TableOnly;
                return true;
            default:
                chartType = default;
                return false;
        }
    }
}
