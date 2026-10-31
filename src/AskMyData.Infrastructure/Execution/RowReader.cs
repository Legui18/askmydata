using System.Data.Common;

namespace AskMyData.Infrastructure.Execution;

/// <summary>Rows read from a reader plus the Truncated_Flag.</summary>
internal readonly record struct RowReadResult(IReadOnlyList<object?[]> Rows, bool Truncated);

internal static class RowReader
{
    /// <summary>
    /// Reads at most <paramref name="rowLimit"/> + 1 rows. Returns min(n, rowLimit) rows and
    /// <c>Truncated = n &gt; rowLimit</c>. On truncation the extra row is discarded,
    /// <paramref name="cancelCommand"/> is invoked (so the server stops sending rows) and the reader is disposed.
    /// The SQL is never rewritten.
    /// </summary>
    internal static async Task<RowReadResult> ReadAsync(
        DbDataReader reader,
        int rowLimit,
        CancellationToken cancellationToken,
        Action? cancelCommand = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegative(rowLimit);

        var rows = new List<object?[]>();
        var fieldCount = reader.FieldCount;

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (rows.Count == rowLimit)
            {
                // Row_Limit + 1 reached: discard it, stop the command and release the reader.
                try
                {
                    cancelCommand?.Invoke();
                }
                catch (Exception)
                {
                    // Best effort; the result is already complete.
                }

                try
                {
                    await reader.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Cancelling mid-stream may surface a server-side cancel error on dispose; ignore.
                }

                return new RowReadResult(rows, true);
            }

            var row = new object?[fieldCount];
            for (var i = 0; i < fieldCount; i++)
            {
                var value = reader.GetValue(i); // sequential order, required by SequentialAccess
                row[i] = value is DBNull ? null : value;
            }

            rows.Add(row);
        }

        return new RowReadResult(rows, false);
    }
}
