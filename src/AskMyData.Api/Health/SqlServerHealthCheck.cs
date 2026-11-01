using System.Text.Json;
using AskMyData.Infrastructure.Execution;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace AskMyData.Api.Health;

/// <summary>
/// Runs <c>SELECT 1</c> as the Read_Only_User with a 5 s timeout (R7.11).
/// Never surfaces the connection string or exception details in its result.
/// </summary>
public sealed class SqlServerHealthCheck : IHealthCheck
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly IOptions<QueryExecutorOptions> _options;

    public SqlServerHealthCheck(IOptions<QueryExecutorOptions> options)
    {
        _options = options;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);
        try
        {
            var builder = new SqlConnectionStringBuilder(_options.Value.ConnectionString)
            {
                ConnectTimeout = (int)Timeout.TotalSeconds,
            };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cts.Token);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = (int)Timeout.TotalSeconds;
            await command.ExecuteScalarAsync(cts.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("SQL Server is unreachable.");
        }
    }

    /// <summary>Writes <c>{"status":..., "sqlServer":...}</c> and nothing else.</summary>
    public static async Task WriteResponseAsync(HttpContext httpContext, HealthReport report)
    {
        var sql = report.Entries.TryGetValue("sqlServer", out var entry) ? entry.Status : HealthStatus.Unhealthy;
        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status == HealthStatus.Healthy ? "Healthy" : "Unhealthy",
            sqlServer = sql == HealthStatus.Healthy ? "Healthy" : "Unhealthy",
        }));
    }
}
