namespace AskMyData.Domain.Results;

/// <summary>
/// Machine-readable Reason_Code values returned when a question cannot be answered.
/// </summary>
public static class ReasonCodes
{
    public const string ParseError = "PARSE_ERROR";
    public const string MultipleStatements = "MULTIPLE_STATEMENTS";
    public const string NotSelectOnly = "NOT_SELECT_ONLY";
    public const string CommentNotAllowed = "COMMENT_NOT_ALLOWED";
    public const string OpenRowsetNotAllowed = "OPENROWSET_NOT_ALLOWED";
    public const string OpenQueryNotAllowed = "OPENQUERY_NOT_ALLOWED";
    public const string LinkedServerNotAllowed = "LINKED_SERVER_NOT_ALLOWED";
    public const string CrossDatabaseNotAllowed = "CROSS_DATABASE_NOT_ALLOWED";
    public const string SystemObjectNotAllowed = "SYSTEM_OBJECT_NOT_ALLOWED";
    public const string TableFunctionNotAllowed = "TABLE_FUNCTION_NOT_ALLOWED";
    public const string FunctionNotAllowed = "FUNCTION_NOT_ALLOWED";
    public const string SelectStar = "SELECT_STAR";
    public const string TableNotAllowed = "TABLE_NOT_ALLOWED";
    public const string ColumnNotAllowed = "COLUMN_NOT_ALLOWED";
    public const string MetricDefinitionMismatch = "METRIC_DEFINITION_MISMATCH";
    public const string Refused = "REFUSED";
    public const string GenerationInvalid = "GENERATION_INVALID";
}
