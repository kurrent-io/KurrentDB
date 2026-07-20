using System.Collections;
using System.Data.Common;

namespace DuckLance.Tests.Support;

/// <summary>
/// A minimal single-row <see cref="DbDataReader"/> test double. Rows are keyed by column (storage) name;
/// values are returned exactly as supplied, mirroring the validated DuckDB.NET wire shapes.
/// </summary>
public sealed class FakeDbDataReader : DbDataReader {
    readonly List<string>                _columns;
    readonly Dictionary<string, object?> _row;
    bool                                 _read;

    FakeDbDataReader(IEnumerable<(string Column, object? Value)> cells) {
        var list = cells.ToList();
        _columns = list.Select(c => c.Column).ToList();
        _row     = list.ToDictionary(keySelector: c => c.Column, elementSelector: c => c.Value, StringComparer.Ordinal);
    }

    public override int  FieldCount      => _columns.Count;
    public override bool HasRows         => _columns.Count > 0;
    public override bool IsClosed        => false;
    public override int  Depth           => 0;
    public override int  RecordsAffected => 0;
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public static FakeDbDataReader SingleRow(params (string Column, object? Value)[] cells) => new(cells);

    public override int GetOrdinal(string name) {
        var ordinal = _columns.IndexOf(name);

        // Mirror the ADO.NET DbDataReader.GetOrdinal contract, which throws IndexOutOfRangeException
        // when the column name is not found; the mapper relies on catching exactly this exception.
        #pragma warning disable CA2201 // Do not raise reserved exception types
        return ordinal >= 0 ? ordinal : throw new IndexOutOfRangeException($"Column '{name}' not found.");
        #pragma warning restore CA2201
    }

    public override object GetValue(int ordinal) => _row[_columns[ordinal]] ?? throw new InvalidOperationException("Value is DBNull; check IsDBNull first.");

    public override bool IsDBNull(int ordinal) => _row[_columns[ordinal]] is null or DBNull;

    public override bool Read() {
        if (_read)
            return false;

        _read = true;
        return true;
    }

    public override string GetName(int ordinal) => _columns[ordinal];

    public override string GetDataTypeName(int ordinal) => GetValue(ordinal).GetType().Name;

    public override Type GetFieldType(int ordinal) => GetValue(ordinal).GetType();

    public override bool NextResult() => false;

    public override bool GetBoolean(int ordinal) => throw new NotSupportedException();

    public override byte GetByte(int ordinal) => throw new NotSupportedException();

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

    public override char GetChar(int ordinal) => throw new NotSupportedException();

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

    public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();

    public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();

    public override double GetDouble(int ordinal) => throw new NotSupportedException();

    public override float GetFloat(int ordinal) => throw new NotSupportedException();

    public override Guid GetGuid(int ordinal) => throw new NotSupportedException();

    public override short GetInt16(int ordinal) => throw new NotSupportedException();

    public override int GetInt32(int ordinal) => throw new NotSupportedException();

    public override long GetInt64(int ordinal) => throw new NotSupportedException();

    public override string GetString(int ordinal) => throw new NotSupportedException();

    public override int GetValues(object[] values) => throw new NotSupportedException();

    public override IEnumerator GetEnumerator() => throw new NotSupportedException();
}
