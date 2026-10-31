using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AskMyData.Infrastructure.Validation;

/// <summary>Name helpers shared by the validation rules.</summary>
internal static class SqlNames
{
    public const string DefaultSchema = "dbo";

    /// <summary>True for <c>sys</c> and <c>INFORMATION_SCHEMA</c>, ignoring case.</summary>
    public static bool IsSystemSchema(string? schema) =>
        string.Equals(schema, "sys", StringComparison.OrdinalIgnoreCase)
        || string.Equals(schema, "INFORMATION_SCHEMA", StringComparison.OrdinalIgnoreCase);

    /// <summary>Formats a multi-part name as dot-separated unquoted parts.</summary>
    public static string Format(MultiPartIdentifier name) =>
        string.Join(".", name.Identifiers.Select(i => i.Value));
}
