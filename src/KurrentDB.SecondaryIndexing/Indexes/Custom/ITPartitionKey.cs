// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using Jint;
using Jint.Native;
using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

internal interface ITPartitionKey {
	static abstract ITPartitionKey ExtractFrom(JsValue value);
	static abstract string? GetDuckDbColumnCreateStatement(string columnName);
	void AppendTo(Appender.Row row);
}

internal readonly struct Int16PartitionKey(short key) : ITPartitionKey {
	public static ITPartitionKey ExtractFrom(JsValue value) => new Int16PartitionKey(Convert.ToInt16(value.AsNumber()));
	public static string GetDuckDbColumnCreateStatement(string columnName) => $"{columnName} smallint not null";

	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct Int32PartitionKey(int key) : ITPartitionKey {
	public static ITPartitionKey ExtractFrom(JsValue value) => new Int32PartitionKey(Convert.ToInt32(value.AsNumber()));
	public static string GetDuckDbColumnCreateStatement(string columnName) => $"{columnName} int not null";
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct Int64PartitionKey(long key) : ITPartitionKey {
	public static ITPartitionKey ExtractFrom(JsValue value) => new Int64PartitionKey(Convert.ToInt64(value.AsNumber()));
	public static string GetDuckDbColumnCreateStatement(string columnName) => $"{columnName} bigint not null";
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct UInt32PartitionKey(uint key) : ITPartitionKey {
	public static ITPartitionKey ExtractFrom(JsValue value) => new UInt32PartitionKey(Convert.ToUInt32(value.AsNumber()));
	public static string GetDuckDbColumnCreateStatement(string columnName) => $"{columnName} uint not null";
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct UInt64PartitionKey(ulong key) : ITPartitionKey {
	public static ITPartitionKey ExtractFrom(JsValue value) => new UInt64PartitionKey(Convert.ToUInt64(value.AsNumber()));
	public static string GetDuckDbColumnCreateStatement(string columnName) => $"{columnName} ubigint not null";
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct NumberPartitionKey(double key) : ITPartitionKey {
	public static ITPartitionKey ExtractFrom(JsValue value) => new NumberPartitionKey(value.AsNumber());
	public static string GetDuckDbColumnCreateStatement(string columnName) => $"{columnName} double not null";
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString(CultureInfo.InvariantCulture);
}

internal readonly struct StringPartitionKey(string key) : ITPartitionKey {
	public static ITPartitionKey ExtractFrom(JsValue value) => new StringPartitionKey(value.AsString());
	public static string GetDuckDbColumnCreateStatement(string columnName) => $"{columnName} varchar not null";
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key;
}

internal readonly struct NullPartitionKey : ITPartitionKey {
	public static ITPartitionKey ExtractFrom(JsValue value) {
		if (!value.IsNull())
			throw new ArgumentException(nameof(value));

		return new NullPartitionKey();
	}

	public static string? GetDuckDbColumnCreateStatement(string columnName) => null;

	public void AppendTo(Appender.Row row) { }
}
