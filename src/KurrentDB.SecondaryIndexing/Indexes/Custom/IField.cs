// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using Jint;
using Jint.Native;
using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

public interface IField {
	static abstract IField ParseFrom(JsValue value);
	static abstract IField ParseFrom(string value);
	static abstract string GetCreateStatement();
	static abstract Type? Type { get; }
	string GetQueryStatement();
	void BindTo(PreparedStatement statement, ref int index);
	void AppendTo(Appender.Row row);
}

internal readonly struct Int16Field(short key) : IField {
	public static Type Type { get; } = typeof(short);
	public static IField ParseFrom(JsValue value) => new Int16Field(Convert.ToInt16(value.AsNumber()));
	public static IField ParseFrom(string value) => new Int16Field(Convert.ToInt16(value));
	public static string GetCreateStatement() => ", partition SMALLINT not null";
	public string GetQueryStatement() => "and partition = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, key);
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct Int32Field(int key) : IField {
	public static Type Type { get; } = typeof(int);
	public static IField ParseFrom(JsValue value) => new Int32Field(Convert.ToInt32(value.AsNumber()));
	public static IField ParseFrom(string value) => new Int32Field(Convert.ToInt32(value));
	public static string GetCreateStatement() => ", partition INTEGER not null";
	public string GetQueryStatement() => "and partition = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, key);
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct Int64Field(long key) : IField {
	public static Type Type { get; } = typeof(long);
	public static IField ParseFrom(JsValue value) => new Int64Field(Convert.ToInt64(value.AsNumber()));
	public static IField ParseFrom(string value) => new Int64Field(Convert.ToInt64(value));
	public static string GetCreateStatement() => ", partition BIGINT not null";
	public string GetQueryStatement() => "and partition = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, key);
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct UInt32Field(uint key) : IField {
	public static Type Type { get; } = typeof(uint);
	public static IField ParseFrom(JsValue value) => new UInt32Field(Convert.ToUInt32(value.AsNumber()));
	public static IField ParseFrom(string value) => new UInt32Field(Convert.ToUInt32(value));
	public static string GetCreateStatement() => ", partition UINTEGER not null";
	public string GetQueryStatement() => "and partition = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, key);
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

internal readonly struct UInt64Field(ulong key) : IField {
	public static Type Type { get; } = typeof(ulong);
	public static IField ParseFrom(JsValue value) => new UInt64Field(Convert.ToUInt64(value.AsNumber()));
	public static IField ParseFrom(string value) => new UInt64Field(Convert.ToUInt64(value));
	public static string GetCreateStatement() => ", partition UBIGINT not null";
	public string GetQueryStatement() => "and partition = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, key);
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString();
}

//qq rename
internal readonly struct DoubleField(double key) : IField {
	public static Type Type { get; } = typeof(double);
	public static IField ParseFrom(JsValue value) => new DoubleField(value.AsNumber());
	public static IField ParseFrom(string value) => new DoubleField(Convert.ToDouble(value));
	public static string GetCreateStatement() => ", partition DOUBLE not null";
	public string GetQueryStatement() => "and partition = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, key);
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key.ToString(CultureInfo.InvariantCulture);
}

internal readonly struct StringField(string key) : IField {
	public static Type Type { get; } = typeof(string);
	public static IField ParseFrom(JsValue value) => new StringField(value.AsString());
	public static IField ParseFrom(string value) => new StringField(value);
	public static string GetCreateStatement() => ", partition VARCHAR not null";
	public string GetQueryStatement() => "and partition = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, key);
	public void AppendTo(Appender.Row row) => row.Append(key);
	public override string ToString() => key;
}

internal readonly struct NullField : IField {
	public static Type? Type { get => null; }
	public static IField ParseFrom(JsValue value) {
		if (!value.IsNull())
			throw new ArgumentException(nameof(value));

		return new NullField();
	}

	public static IField ParseFrom(string value) => throw new NotSupportedException();
	public static string GetCreateStatement() => string.Empty;
	public string GetQueryStatement() => string.Empty;
	public void BindTo(PreparedStatement statement, ref int index) { }
	public void AppendTo(Appender.Row row) { }
}
