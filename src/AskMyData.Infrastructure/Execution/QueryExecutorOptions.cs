namespace AskMyData.Infrastructure.Execution;

/// <summary>Configuration for <see cref="SqlServerQueryExecutor"/>.</summary>
public sealed class QueryExecutorOptions
{
    /// <summary>Read_Only_User connection string. Comes from env/secret store and is never logged.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>Row_Limit: maximum number of rows returned to the caller.</summary>
    public int RowLimit { get; set; } = 1000;
}
