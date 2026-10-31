namespace AskMyData.Domain.Semantic;

/// <summary>
/// The governed Semantic_Layer definitions for the Dataset (db/dataset.sql). Built in code so the
/// Domain layer performs no I/O (Requirements 1.1, 1.2, 1.5, 1.6, 14.6).
/// dbo.Employees and dbo.DatasetVersion exist in the Dataset but are deliberately not in the Allowlist.
/// </summary>
public static class SemanticLayerCatalog
{
    private const string Dbo = "dbo";
    private const string OrderLines = "dbo.OrderLines";
    private const string Orders = "dbo.Orders";

    /// <summary>
    /// Creates the Semantic_Layer. The Allowed_Table_Function_List is empty unless
    /// <paramref name="allowedTableFunctions"/> is supplied (Requirement 3.11).
    /// </summary>
    public static SemanticLayer Create(IEnumerable<string>? allowedTableFunctions = null) =>
        new(CreateMetrics(), CreateAllowlist(), allowedTableFunctions);

    private static Metric[] CreateMetrics() =>
    [
        new(
            "TotalRevenue",
            "Net sales revenue after line discounts.",
            "SUM(dbo.OrderLines.Quantity * dbo.OrderLines.UnitPrice * (1 - dbo.OrderLines.Discount))",
            [OrderLines]),
        new(
            "UnitsSold",
            "Total number of product units sold.",
            "SUM(dbo.OrderLines.Quantity)",
            [OrderLines]),
        new(
            "OrderCount",
            "Number of distinct orders.",
            "COUNT(DISTINCT dbo.Orders.OrderId)",
            [Orders]),
        new(
            "GrossMargin",
            "Net revenue after discounts minus the cost of goods sold.",
            "SUM(dbo.OrderLines.Quantity * (dbo.OrderLines.UnitPrice * (1 - dbo.OrderLines.Discount) - dbo.OrderLines.UnitCost))",
            [OrderLines]),
        new(
            "AverageOrderValue",
            "Net revenue after discounts divided by the number of distinct orders.",
            "SUM(dbo.OrderLines.Quantity * dbo.OrderLines.UnitPrice * (1 - dbo.OrderLines.Discount)) / COUNT(DISTINCT dbo.Orders.OrderId)",
            [OrderLines, Orders]),
        new(
            "CustomerCount",
            "Number of distinct customers who placed an order.",
            "COUNT(DISTINCT dbo.Orders.CustomerId)",
            [Orders]),
    ];

    private static AllowlistEntry[] CreateAllowlist() =>
    [
        Table("Customers",
            Column("CustomerId", "int", "Primary key."),
            Column("CustomerName", "nvarchar(100)", "Customer display name."),
            Column("Segment", "nvarchar(20)", "Customer segment."),
            Column("Country", "nvarchar(50)", "Customer country."),
            Column("City", "nvarchar(50)", "Customer city."),
            Column("CreatedDate", "date", "Date the customer was created.")),
        Table("Products",
            Column("ProductId", "int", "Primary key."),
            Column("ProductName", "nvarchar(100)", "Product display name."),
            Column("Category", "nvarchar(50)", "Product category."),
            Column("Subcategory", "nvarchar(50)", "Product subcategory."),
            Column("ListPrice", "decimal(10,2)", "Current list price."),
            Column("StandardCost", "decimal(10,2)", "Current standard cost.")),
        Table("Stores",
            Column("StoreId", "int", "Primary key."),
            Column("StoreName", "nvarchar(100)", "Store display name."),
            Column("Region", "nvarchar(30)", "Sales region."),
            Column("Country", "nvarchar(50)", "Store country.")),
        Table("Orders",
            Column("OrderId", "int", "Primary key."),
            Column("CustomerId", "int", "Foreign key to dbo.Customers.CustomerId."),
            Column("StoreId", "int", "Foreign key to dbo.Stores.StoreId."),
            Column("OrderDate", "date", "Date the order was placed."),
            Column("Channel", "nvarchar(20)", "Sales channel."),
            Column("Status", "nvarchar(20)", "Order status.")),
        Table("OrderLines",
            Column("OrderLineId", "int", "Primary key."),
            Column("OrderId", "int", "Foreign key to dbo.Orders.OrderId."),
            Column("ProductId", "int", "Foreign key to dbo.Products.ProductId."),
            Column("Quantity", "int", "Units ordered."),
            Column("UnitPrice", "decimal(10,2)", "Unit price charged before discount."),
            Column("Discount", "decimal(4,3)", "Discount fraction, 0 to 1."),
            Column("UnitCost", "decimal(10,2)", "Unit cost at time of sale.")),
        Table("Calendar",
            Column("DateKey", "int", "Primary key."),
            Column("Date", "date", "Calendar date."),
            Column("Year", "int", "Calendar year."),
            Column("Quarter", "int", "Calendar quarter, 1 to 4."),
            Column("Month", "int", "Calendar month, 1 to 12."),
            Column("MonthName", "nvarchar(10)", "Month name.")),
    ];

    private static AllowlistEntry Table(string name, params AllowlistColumn[] columns) =>
        new(Dbo, name, AllowlistObjectKind.Table, columns);

    private static AllowlistColumn Column(string name, string sqlType, string description) =>
        new(name, sqlType, description);
}
