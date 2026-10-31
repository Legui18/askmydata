namespace AskMyData.Domain.Results;

/// <summary>
/// Category of a query execution failure. Serialized as <c>TIMEOUT</c> / <c>EXECUTION_ERROR</c>
/// via <see cref="WireFormat"/>.
/// </summary>
public enum ExecutionErrorCategory
{
    Timeout,
    ExecutionError,
}
