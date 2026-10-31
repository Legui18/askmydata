namespace AskMyData.Domain.Semantic;

/// <summary>
/// A table or view the generated SQL may reference, with the columns it may use (Requirement 1.2).
/// </summary>
public sealed record AllowlistEntry(
    string Schema,
    string Name,
    AllowlistObjectKind Kind,
    IReadOnlyList<AllowlistColumn> Columns);

/// <summary>
/// A column of an <see cref="AllowlistEntry"/>.
/// </summary>
public sealed record AllowlistColumn(string Name, string SqlType, string? Description);

/// <summary>
/// The kind of database object an <see cref="AllowlistEntry"/> describes.
/// </summary>
public enum AllowlistObjectKind
{
    Table,
    View,
}
