using System.Globalization;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace AskMyData.IntegrationTests.Database;

/// <summary>
/// A throw-away SQL Server 2022 container that can run <c>db/dataset.sql</c> with the container's own
/// sqlcmd (the same way the compose <c>db-init</c> job does).
/// </summary>
public sealed class DatasetSqlServer : IAsyncLifetime
{
    public const string ReadOnlyPassword = "R3adOnly!Passw0rd";

    private const string SqlcmdPath = "/opt/mssql-tools18/bin/sqlcmd";
    private const string ScriptPathInContainer = "/tmp/dataset.sql";

    /// <summary>Tables whose content is compared; DatasetVersion is excluded because it holds a timestamp.</summary>
    public static readonly string[] DataTables =
        ["Customers", "Products", "Stores", "Orders", "OrderLines", "Calendar", "Employees"];

    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var script = await File.ReadAllBytesAsync(DatasetScriptPath());
        await _container.CopyAsync(script, ScriptPathInContainer);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Runs the Dataset_Script with <c>sqlcmd -b</c>; returns exit code and combined output.</summary>
    public async Task<ScriptRun> RunScriptAsync()
    {
        var result = await _container.ExecAsync(
        [
            SqlcmdPath, "-b", "-C", "-S", "localhost", "-U", MsSqlBuilder.DefaultUsername,
            "-P", MsSqlBuilder.DefaultPassword, "-i", ScriptPathInContainer,
            "-v", $"READONLY_PASSWORD={ReadOnlyPassword}",
        ]);
        return new ScriptRun(result.ExitCode ?? -1, result.Stdout + Environment.NewLine + result.Stderr);
    }

    /// <summary>Executes a statement against the given database as sa.</summary>
    public async Task ExecuteAsync(string database, string sql)
    {
        await using var connection = await OpenAsync(database);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Row count and CHECKSUM_AGG(CHECKSUM(*)) for each data table, plus the DatasetVersion row count.</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetTableChecksumsAsync()
    {
        var checksums = new Dictionary<string, string>();
        await using var connection = await OpenAsync("AskMyData");
        foreach (var table in DataTables.Append("DatasetVersion"))
        {
            var query = table == "DatasetVersion"
                ? "SELECT COUNT_BIG(*), 0 FROM dbo.DatasetVersion"
                : $"SELECT COUNT_BIG(*), ISNULL(CHECKSUM_AGG(CHECKSUM(*)), 0) FROM dbo.[{table}]";
            await using var command = new SqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            checksums[table] = string.Create(
                CultureInfo.InvariantCulture, $"{reader.GetInt64(0)}:{Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture)}");
        }

        return checksums;
    }

    /// <summary>Returns every "table.column" present in INFORMATION_SCHEMA.COLUMNS (schema dbo).</summary>
    public async Task<ISet<string>> GetColumnsAsync()
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = await OpenAsync("AskMyData");
        await using var command = new SqlCommand(
            "SELECT TABLE_SCHEMA + '.' + TABLE_NAME + '.' + COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private async Task<SqlConnection> OpenAsync(string database)
    {
        var builder = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = database,
            TrustServerCertificate = true,
        };
        var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static string DatasetScriptPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AskMyData.sln")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException("Could not locate the repository root (AskMyData.sln).")
            : Path.Combine(directory.FullName, "db", "dataset.sql");
    }
}

public sealed record ScriptRun(long ExitCode, string Output);
