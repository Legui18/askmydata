using System.Data;
using AskMyData.Application.Execution;
using AskMyData.Application.Validation;
using AskMyData.Domain.Results;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskMyData.Infrastructure.Execution;

/// <summary>Executes ValidatedSql as the Read_Only_User, streaming at most Row_Limit + 1 rows.</summary>
public sealed class SqlServerQueryExecutor(
    IOptions<QueryExecutorOptions> options,
    ILogger<SqlServerQueryExecutor> log,
    TimeProvider time) : IQueryExecutor
{
    private const int SqlTimeoutNumber = -2;

    public async Task<OneOf<QueryResult, ExecutionError>> ExecuteAsync(
        ValidatedSql sql,
        QueryBudget budget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(budget);

        var opts = options.Value;
        var start = time.GetTimestamp();
        var commandTimeout = Math.Max(1, (int)Math.Ceiling(budget.CommandTimeout.TotalSeconds));

        try
        {
            await using var connection = new SqlConnection(opts.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = sql.Text; // never rewritten
            command.CommandTimeout = commandTimeout;

            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken)
                .ConfigureAwait(false);

            var columns = new List<ResultColumn>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var typeName = reader.GetDataTypeName(i);
                columns.Add(new ResultColumn(reader.GetName(i), typeName, SqlTypeClassifier.Classify(typeName)));
            }

            var read = await RowReader
                .ReadAsync(reader, opts.RowLimit, cancellationToken, command.Cancel)
                .ConfigureAwait(false);

            return new QueryResult(columns, read.Rows, read.Truncated, time.GetElapsedTime(start));
        }
        catch (SqlException ex) when (cancellationToken.IsCancellationRequested)
        {
            // A cancelled token can surface as a SqlException; the Orchestrator classifies cancellation.
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
        catch (SqlException ex)
        {
            var category = ex.Number == SqlTimeoutNumber
                ? ExecutionErrorCategory.Timeout
                : ExecutionErrorCategory.ExecutionError;

            if (category == ExecutionErrorCategory.ExecutionError)
            {
                // Error text stays inside Infrastructure (R4.8, R5.9, R9.2).
                log.LogWarning(
                    "SQL execution failed. Number={Number} Severity={Severity} State={State} Message={Message}",
                    ex.Number, ex.Class, ex.State, ex.Message);
            }

            return new ExecutionError(category, time.GetElapsedTime(start));
        }
    }
}
