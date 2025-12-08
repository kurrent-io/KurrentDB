// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using Jint;
using Jint.Native;
using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

public interface IValueType {
	static abstract IValueType ParseFrom(JsValue value);
	static abstract IValueType ParseFrom(string value);
	static abstract string GetCreateStatement();
	static abstract Type? Type { get; }
	string GetQueryStatement();
	void BindTo(PreparedStatement statement, ref int index);
	void AppendTo(Appender.Row row);
}

internal readonly struct Int16ValueType(short key) : IValueType {
	public static Type Type { get; } = typeof(short);
	public static IValueType ParseFrom(JsValue value) => new Int16ValueType(Convert.ToInt16(value.AsNumber()));
	public static IValueType ParseFrom(string value) => new Int16ValueType(Convert.ToInt16(value));
	public static string GetCreateStatement() => ", partition SMALLINT not null";
	public string GetQueryStatement() => "and partition = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, key);
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct Int32ValueType(int key) : IValueType {
	public static Type Type { get; } = typeof(int);
	public static IValueType ParseFrom(JsValue value) => new Int32ValueType(Convert.ToInt32(value.AsNumber()));
	public static IValueType ParseFrom(string value) => new Int32ValueType(Convert.ToInt32(value));
	public static string GetCreateStatement() => ", partition INTEGER not null";
	public string GetQueryStatement() => "and partition = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, key);
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct Int64ValueType(long key) : IValueType {
	public static Type Type { get; } = typeof(long);
	public static IValueType ParseFrom(JsValue value) => new Int64ValueType(Convert.ToInt64(value.AsNumber()));
	public static IValueType ParseFrom(string value) => new Int64ValueType(Convert.ToInt64(value));
	public static string GetCreateStatement() => ", partition BIGINT not null";
	public string GetQueryStatement() => "and partition = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, key);
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct UInt32ValueType(uint key) : IValueType {
	public static Type Type { get; } = typeof(uint);
	public static IValueType ParseFrom(JsValue value) => new UInt32ValueType(Convert.ToUInt32(value.AsNumber()));
	public static IValueType ParseFrom(string value) => new UInt32ValueType(Convert.ToUInt32(value));
	public static string GetCreateStatement() => ", partition UINTEGER not null";
	public string GetQueryStatement() => "and partition = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, key);
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct UInt64ValueType(ulong key) : IValueType {
	public static Type Type { get; } = typeof(ulong);
	public static IValueType ParseFrom(JsValue value) => new UInt64ValueType(Convert.ToUInt64(value.AsNumber()));
	public static IValueType ParseFrom(string value) => new UInt64ValueType(Convert.ToUInt64(value));
	public static string GetCreateStatement() => ", partition UBIGINT not null";
	public string GetQueryStatement() => "and partition = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, key);
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct DoubleValueType(double key) : IValueType {
	public static Type Type { get; } = typeof(double);
	public static IValueType ParseFrom(JsValue value) => new DoubleValueType(value.AsNumber());
	public static IValueType ParseFrom(string value) => new DoubleValueType(Convert.ToDouble(value));
	public static string GetCreateStatement() => ", partition DOUBLE not null";
	public string GetQueryStatement() => "and partition = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, key);
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString(CultureInfo.InvariantCulture);
}

internal readonly struct StringValueType(string key) : IValueType {
	public static Type Type { get; } = typeof(string);
	public static IValueType ParseFrom(JsValue value) => new StringValueType(value.AsString());
	public static IValueType ParseFrom(string value) => new StringValueType(value);
	public static string GetCreateStatement() => ", partition VARCHAR not null";
	public string GetQueryStatement() => "and partition = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, key);
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key;
}

internal readonly struct NullValueType : IValueType {
	public static Type? Type { get => null; }
	public static IValueType ParseFrom(JsValue value) {
		if (!value.IsNull())
			throw new ArgumentException(nameof(value));

		return new NullValueType();
	}

	public static IValueType ParseFrom(string value) => throw new NotSupportedException();
	public static string GetCreateStatement() => string.Empty;
	public string GetQueryStatement() => string.Empty;
	public void BindTo(PreparedStatement statement, ref int index) { }
	public void AppendTo(Appender.Row row) { }
}
