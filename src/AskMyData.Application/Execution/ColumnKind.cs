namespace AskMyData.Application.Execution;

/// <summary>Coarse classification of a result column's SQL type, used by the ChartSuggester.</summary>
public enum ColumnKind
{
    Numeric,
    Text,
    Temporal,
    Boolean,
    Other,
}
