using System.Globalization;
using System.Text.Json;
using AskMyData.Application.Execution;
using AskMyData.Evals.Cases;

namespace AskMyData.Evals.Runner;

/// <summary>Compares an actual <see cref="QueryResult"/> with a rows <see cref="EvalExpected"/> (R6.5).</summary>
public static class ResultComparer
{
    private const char Separator = '\u001f';

    /// <summary>
    /// Returns <c>null</c> when the result matches, otherwise a short mismatch description.
    /// <para>
    /// With <paramref name="requireColumnNames"/> true (Stub_Mode) the column names must equal the expected ones.
    /// With false (Live_Mode) columns are matched by position: any non-empty name is accepted, because a real
    /// model may legitimately choose another alias (for example the Metric name). A column with no name at all
    /// is still reported, since an unnamed column is a poor answer for a user.
    /// </para>
    /// </summary>
    public static string? Compare(EvalExpected expected, QueryResult actual, bool requireColumnNames = true)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        var actualColumns = actual.Columns.Select(c => c.Name).ToList();
        if (requireColumnNames)
        {
            if (actualColumns.Count != expected.Columns.Count
                || !actualColumns.Zip(expected.Columns).All(p => string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase)))
            {
                return ColumnsDiffer(expected, actualColumns);
            }
        }
        else
        {
            if (actualColumns.Count != expected.Columns.Count)
            {
                return ColumnsDiffer(expected, actualColumns);
            }

            if (actualColumns.Any(string.IsNullOrWhiteSpace))
            {
                return $"A result column has no name (missing alias): actual [{string.Join(", ", actualColumns)}].";
            }
        }

        var expectedRows = expected.Rows.Select(r => Key(r.Select(Normalize))).ToList();
        var actualRows = actual.Rows.Select(r => Key(r.Select(Normalize))).ToList();
        if (expectedRows.Count != actualRows.Count)
        {
            return $"Row count differs: expected {expectedRows.Count}, actual {actualRows.Count}.";
        }

        if (!expected.OrderSensitive)
        {
            expectedRows.Sort(StringComparer.Ordinal);
            actualRows.Sort(StringComparer.Ordinal);
        }

        for (var i = 0; i < expectedRows.Count; i++)
        {
            if (!string.Equals(expectedRows[i], actualRows[i], StringComparison.Ordinal))
            {
                return $"Row {i} differs. Expected: {Show(expectedRows[i])}; actual: {Show(actualRows[i])}.";
            }
        }

        return null;
    }

    private static string ColumnsDiffer(EvalExpected expected, List<string> actualColumns) =>
        $"Columns differ: expected [{string.Join(", ", expected.Columns)}], actual [{string.Join(", ", actualColumns)}].";

    private static string Key(IEnumerable<string> cells) => string.Join(Separator, cells);

    /// <summary>Readable form of a row key for mismatch messages.</summary>
    private static string Show(string key) => "[" + key.Replace(Separator.ToString(), " | ") + "]";

    private static string Normalize(JsonElement cell) => cell.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => "null",
        JsonValueKind.Number => Number(cell.GetDecimal()),
        JsonValueKind.True => "bool:true",
        JsonValueKind.False => "bool:false",
        JsonValueKind.String => "s:" + cell.GetString(),
        _ => "raw:" + cell.GetRawText(),
    };

    private static string Normalize(object? value) => value switch
    {
        null or DBNull => "null",
        bool b => b ? "bool:true" : "bool:false",
        byte or sbyte or short or ushort or int or uint or long or ulong => Number(Convert.ToDecimal(value, CultureInfo.InvariantCulture)),
        decimal d => Number(d),
        double d => Number(Convert.ToDecimal(d, CultureInfo.InvariantCulture)),
        float f => Number(Convert.ToDecimal(f, CultureInfo.InvariantCulture)),
        DateTime dt => "s:" + (dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
        DateOnly d => "s:" + d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        string s => "s:" + s,
        _ => "s:" + Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    private static string Number(decimal value) =>
        "n:" + Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
}