using AskMyData.Domain.Semantic;

namespace AskMyData.UnitTests.Generators;

/// <summary>Coarse column type used to choose expressions and predicates.</summary>
public enum ColumnType
{
    Numeric,
    Text,
    Date,
}

/// <summary>A join edge between two Allowlist columns (a foreign key, or Orders.OrderDate to Calendar.Date).</summary>
public sealed record JoinEdge(string FromTable, string FromColumn, string ToTable, string ToColumn)
{
    public bool Touches(string table) =>
        string.Equals(FromTable, table, StringComparison.Ordinal) || string.Equals(ToTable, table, StringComparison.Ordinal);

    public string Other(string table) => string.Equals(FromTable, table, StringComparison.Ordinal) ? ToTable : FromTable;
}

/// <summary>
/// Generator view of the governed catalog: Allowlist tables and columns come from
/// <see cref="SemanticLayerCatalog.Create"/>; the join edges mirror the foreign keys of the Dataset.
/// </summary>
public static class DatasetModel
{
    public static SemanticLayer Layer { get; } = SemanticLayerCatalog.Create();

    public static IReadOnlyList<string> TableNames { get; } = Layer.Allowlist.Select(e => e.Name).ToArray();

    public static IReadOnlyList<JoinEdge> Edges { get; } =
    [
        new("Orders", "CustomerId", "Customers", "CustomerId"),
        new("Orders", "StoreId", "Stores", "StoreId"),
        new("OrderLines", "OrderId", "Orders", "OrderId"),
        new("OrderLines", "ProductId", "Products", "ProductId"),
        new("Orders", "OrderDate", "Calendar", "Date"),
    ];

    private static readonly Dictionary<string, string> DefaultAliases = new(StringComparer.Ordinal)
    {
        ["Customers"] = "c",
        ["Products"] = "p",
        ["Stores"] = "s",
        ["Orders"] = "o",
        ["OrderLines"] = "ol",
        ["Calendar"] = "cal",
    };

    public static string DefaultAlias(string table) => DefaultAliases[table];

    public static IReadOnlyList<AllowlistColumn> ColumnsOf(string table) =>
        Layer.TryGetObject("dbo", table, out var entry)
            ? entry.Columns
            : throw new ArgumentException($"'{table}' is not an Allowlist table.", nameof(table));

    public static ColumnType TypeOf(AllowlistColumn column) => column.SqlType switch
    {
        var t when t.StartsWith("int", StringComparison.OrdinalIgnoreCase)
                   || t.StartsWith("decimal", StringComparison.OrdinalIgnoreCase) => ColumnType.Numeric,
        var t when t.StartsWith("date", StringComparison.OrdinalIgnoreCase) => ColumnType.Date,
        _ => ColumnType.Text,
    };
}
