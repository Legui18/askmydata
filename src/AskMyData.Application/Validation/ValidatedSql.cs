namespace AskMyData.Application.Validation;

/// <summary>
/// SQL text that has passed ISqlValidator. The constructor is <c>internal</c> so only the Orchestrator
/// in the Application layer can mint an instance after a validator pass; IQueryExecutor accepts only
/// this type (R2.3, R2.4, R10.5).
/// </summary>
public sealed class ValidatedSql
{
    internal ValidatedSql(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    /// <summary>The validated SQL text, executed unchanged.</summary>
    public string Text { get; }

    /// <inheritdoc />
    public override string ToString() => Text;
}
