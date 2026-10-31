using System.Collections;
using System.Data.Common;
using AskMyData.Infrastructure.Execution;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AskMyData.UnitTests.Infrastructure.Execution;

public sealed class RowLimitProperties
{
    // Feature: askmydata-conversational-analytics, Property 13: Row_Limit and Truncated_Flag invariants
    /// <summary>**Validates: Requirements 4.4, 4.5, 4.6**</summary>
    [Property(MaxTest = 100)]
    public Property Row_limit_and_truncated_flag_invariants_hold()
    {
        var sizes = Gen.Choose(0, 60).ToArbitrary();
        var limits = Gen.Choose(0, 60).ToArbitrary();

        return Prop.ForAll(sizes, limits, (n, limit) =>
        {
            using var reader = new CountingReader(n);
            var cancelCalls = 0;

            var result = RowReader.ReadAsync(reader, limit, CancellationToken.None, () => cancelCalls++)
                .GetAwaiter().GetResult();

            var truncated = n > limit;
            var rowsOk = result.Rows.Count == Math.Min(n, limit);
            var flagOk = result.Truncated == truncated;
            var readsOk = reader.Reads <= limit + 1 && reader.Reads <= n + 1;
            var rowValuesOk = result.Rows.Select((r, i) => (int)r[0]! == i).All(x => x);
            var cleanupOk = truncated
                ? cancelCalls == 1 && reader.DisposeCount >= 1
                : cancelCalls == 0;

            return (rowsOk && flagOk && readsOk && rowValuesOk && cleanupOk)
                .Label($"n={n} limit={limit} rows={result.Rows.Count} truncated={result.Truncated} " +
                       $"reads={reader.Reads} cancel={cancelCalls} disposed={reader.DisposeCount}");
        });
    }

    /// <summary>Fake reader yielding <c>total</c> single-column rows and counting Read calls.</summary>
    private sealed class CountingReader(int total) : DbDataReader
    {
        private int _position = -1;

        public int Reads { get; private set; }
        public int DisposeCount { get; private set; }

        public override bool Read()
        {
            Reads++;
            if (_position + 1 >= total)
            {
                return false;
            }

            _position++;
            return true;
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());

        public override ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }

        public override int FieldCount => 1;
        public override object GetValue(int ordinal) => _position;
        public override int Depth => 0;
        public override bool HasRows => total > 0;
        public override bool IsClosed => false;
        public override int RecordsAffected => -1;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(0);
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override string GetDataTypeName(int ordinal) => "int";
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override IEnumerator GetEnumerator() => throw new NotSupportedException();
        public override Type GetFieldType(int ordinal) => typeof(int);
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override int GetInt32(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override string GetName(int ordinal) => "c";
        public override int GetOrdinal(string name) => 0;
        public override string GetString(int ordinal) => throw new NotSupportedException();
        public override int GetValues(object[] values) => throw new NotSupportedException();
        public override bool IsDBNull(int ordinal) => false;
        public override bool NextResult() => false;
    }
}
