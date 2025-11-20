// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using Jint;
using Jint.Native;
using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

internal interface ITPartitionKey {
	const string DuckDbColumnName = "partition_key";
	static abstract ITPartitionKey ParseFrom(JsValue value);
	static abstract ITPartitionKey ParseFrom(string value);
	static abstract string GetDuckDbColumnCreateStatement();
	string GetDuckDbColumnQueryStatement();
	void AppendTo(Appender.Row row);
}

internal readonly struct Int16PartitionKey(short key) : ITPartitionKey {
	public static ITPartitionKey ParseFrom(JsValue value) => new Int16PartitionKey(Convert.ToInt16(value.AsNumber()));
	public static ITPartitionKey ParseFrom(string value) => new Int16PartitionKey(Convert.ToInt16(value));
	public static string GetDuckDbColumnCreateStatement() => $"{ITPartitionKey.DuckDbColumnName} smallint not null";
	public string GetDuckDbColumnQueryStatement() => $"{ITPartitionKey.DuckDbColumnName}={key}";
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct Int32PartitionKey(int key) : ITPartitionKey {
	public static ITPartitionKey ParseFrom(JsValue value) => new Int32PartitionKey(Convert.ToInt32(value.AsNumber()));
	public static ITPartitionKey ParseFrom(string value) => new Int32PartitionKey(Convert.ToInt32(value));
	public static string GetDuckDbColumnCreateStatement() => $"{ITPartitionKey.DuckDbColumnName} int not null";
	public string GetDuckDbColumnQueryStatement() => $"{ITPartitionKey.DuckDbColumnName}={key}";
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct Int64PartitionKey(long key) : ITPartitionKey {
	public static ITPartitionKey ParseFrom(JsValue value) => new Int64PartitionKey(Convert.ToInt64(value.AsNumber()));
	public static ITPartitionKey ParseFrom(string value) => new Int64PartitionKey(Convert.ToInt64(value));
	public static string GetDuckDbColumnCreateStatement() => $"{ITPartitionKey.DuckDbColumnName} bigint not null";
	public string GetDuckDbColumnQueryStatement() => $"{ITPartitionKey.DuckDbColumnName}={key}";
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct UInt32PartitionKey(uint key) : ITPartitionKey {
	public static ITPartitionKey ParseFrom(JsValue value) => new UInt32PartitionKey(Convert.ToUInt32(value.AsNumber()));
	public static ITPartitionKey ParseFrom(string value) => new UInt32PartitionKey(Convert.ToUInt32(value));
	public static string GetDuckDbColumnCreateStatement() => $"{ITPartitionKey.DuckDbColumnName} uint not null";
	public string GetDuckDbColumnQueryStatement() => $"{ITPartitionKey.DuckDbColumnName}={key}";
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct UInt64PartitionKey(ulong key) : ITPartitionKey {
	public static ITPartitionKey ParseFrom(JsValue value) => new UInt64PartitionKey(Convert.ToUInt64(value.AsNumber()));
	public static ITPartitionKey ParseFrom(string value) => new UInt64PartitionKey(Convert.ToUInt64(value));
	public static string GetDuckDbColumnCreateStatement() => $"{ITPartitionKey.DuckDbColumnName} ubigint not null";
	public string GetDuckDbColumnQueryStatement() => $"{ITPartitionKey.DuckDbColumnName}={key}";
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct NumberPartitionKey(double key) : ITPartitionKey {
	public static ITPartitionKey ParseFrom(JsValue value) => new NumberPartitionKey(value.AsNumber());
	public static ITPartitionKey ParseFrom(string value) => new NumberPartitionKey(Convert.ToDouble(value));
	public static string GetDuckDbColumnCreateStatement() => $"{ITPartitionKey.DuckDbColumnName} double not null";
	public string GetDuckDbColumnQueryStatement() => $"{ITPartitionKey.DuckDbColumnName}={key}";
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString(CultureInfo.InvariantCulture);
}

internal readonly struct StringPartitionKey(string key) : ITPartitionKey {
	public static ITPartitionKey ParseFrom(JsValue value) => new StringPartitionKey(value.AsString());
	public static ITPartitionKey ParseFrom(string value) => new StringPartitionKey(value);
	public static string GetDuckDbColumnCreateStatement() => $"{ITPartitionKey.DuckDbColumnName} varchar not null";
	public string GetDuckDbColumnQueryStatement() => $"{ITPartitionKey.DuckDbColumnName}='{Escape(key)}'";
	private static string Escape(string s) => s.Replace("'", "''");
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key;
}

internal readonly struct NullPartitionKey : ITPartitionKey {
	public static ITPartitionKey ParseFrom(JsValue value) {
		if (!value.IsNull())
			throw new ArgumentException(nameof(value));

		return new NullPartitionKey();
	}

	public static ITPartitionKey ParseFrom(string value) => throw new NotSupportedException();
	public static string GetDuckDbColumnCreateStatement() => string.Empty;
	public string GetDuckDbColumnQueryStatement() => throw new NotSupportedException();
	public void AppendTo(Appender.Row row) { }
}
