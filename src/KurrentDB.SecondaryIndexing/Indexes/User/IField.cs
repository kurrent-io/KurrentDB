// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using DuckDB.NET.Data.DataChunk.Writer;
using Jint;
using Jint.Native;
using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

public interface IField {
	static abstract IField ParseFrom(JsValue value);
	static abstract IField ParseFrom(string value);
	static abstract string GetCreateStatement(string field);
	static abstract Type? Type { get; }
	string GetQueryStatement(string field);
	void BindTo(PreparedStatement statement, ref int index);
	void AppendTo(Appender.Row row);
	[Experimental("DuckDBNET001")] void WriteTo(IDuckDBDataWriter writer, ulong rowIndex);
}

internal readonly record struct Int16Field(short Key) : IField {
	public static Type Type { get; } = typeof(short);
	public static IField ParseFrom(JsValue value) => new Int16Field(Convert.ToInt16(value.AsNumber()));
	public static IField ParseFrom(string value) => new Int16Field(Convert.ToInt16(value));
	public static string GetCreateStatement(string field) => $", \"{field}\" SMALLINT not null";
	public string GetQueryStatement(string field) => $"and \"{field}\" = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, Key);
	public void AppendTo(Appender.Row row) => row.Append(Key);
	[Experimental("DuckDBNET001")] public void WriteTo(IDuckDBDataWriter writer, ulong rowIndex) => writer.WriteValue(Key, rowIndex);
	public override string ToString() => Key.ToString();
}

internal readonly record struct Int32Field(int Key) : IField {
	public static Type Type { get; } = typeof(int);
	public static IField ParseFrom(JsValue value) => new Int32Field(Convert.ToInt32(value.AsNumber()));
	public static IField ParseFrom(string value) => new Int32Field(Convert.ToInt32(value));
	public static string GetCreateStatement(string field) => $", \"{field}\" INTEGER not null";
	public string GetQueryStatement(string field) => $"and \"{field}\" = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, Key);
	public void AppendTo(Appender.Row row) => row.Append(Key);
	[Experimental("DuckDBNET001")] public void WriteTo(IDuckDBDataWriter writer, ulong rowIndex) => writer.WriteValue(Key, rowIndex);
	public override string ToString() => Key.ToString();
}

internal readonly record struct Int64Field(long Key) : IField {
	public static Type Type { get; } = typeof(long);
	public static IField ParseFrom(JsValue value) => new Int64Field(Convert.ToInt64(value.AsNumber()));
	public static IField ParseFrom(string value) => new Int64Field(Convert.ToInt64(value));
	public static string GetCreateStatement(string field) => $", \"{field}\" BIGINT not null";
	public string GetQueryStatement(string field) => $"and \"{field}\" = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, Key);
	public void AppendTo(Appender.Row row) => row.Append(Key);
	[Experimental("DuckDBNET001")] public void WriteTo(IDuckDBDataWriter writer, ulong rowIndex) => writer.WriteValue(Key, rowIndex);
	public override string ToString() => Key.ToString();
}

internal readonly record struct UInt32Field(uint Key) : IField {
	public static Type Type { get; } = typeof(uint);
	public static IField ParseFrom(JsValue value) => new UInt32Field(Convert.ToUInt32(value.AsNumber()));
	public static IField ParseFrom(string value) => new UInt32Field(Convert.ToUInt32(value));
	public static string GetCreateStatement(string field) => $", \"{field}\" UINTEGER not null";
	public string GetQueryStatement(string field) => $"and \"{field}\" = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, Key);
	public void AppendTo(Appender.Row row) => row.Append(Key);
	[Experimental("DuckDBNET001")] public void WriteTo(IDuckDBDataWriter writer, ulong rowIndex) => writer.WriteValue(Key, rowIndex);
	public override string ToString() => Key.ToString();
}

internal readonly record struct UInt64Field(ulong Key) : IField {
	public static Type Type { get; } = typeof(ulong);
	public static IField ParseFrom(JsValue value) => new UInt64Field(Convert.ToUInt64(value.AsNumber()));
	public static IField ParseFrom(string value) => new UInt64Field(Convert.ToUInt64(value));
	public static string GetCreateStatement(string field) => $", \"{field}\" UBIGINT not null";
	public string GetQueryStatement(string field) => $"and \"{field}\" = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, Key);
	public void AppendTo(Appender.Row row) => row.Append(Key);
	[Experimental("DuckDBNET001")] public void WriteTo(IDuckDBDataWriter writer, ulong rowIndex) => writer.WriteValue(Key, rowIndex);
	public override string ToString() => Key.ToString();
}

internal readonly record struct DoubleField(double Key) : IField {
	public static Type Type { get; } = typeof(double);
	public static IField ParseFrom(JsValue value) => new DoubleField(value.AsNumber());
	public static IField ParseFrom(string value) => new DoubleField(Convert.ToDouble(value));
	public static string GetCreateStatement(string field) => $", \"{field}\" DOUBLE not null";
	public string GetQueryStatement(string field) => $"and \"{field}\" = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, Key);
	public void AppendTo(Appender.Row row) => row.Append(Key);
	[Experimental("DuckDBNET001")] public void WriteTo(IDuckDBDataWriter writer, ulong rowIndex) => writer.WriteValue(Key, rowIndex);
	public override string ToString() => Key.ToString(CultureInfo.InvariantCulture);
}

internal readonly record struct StringField(string Key) : IField {
	public static Type Type { get; } = typeof(string);
	public static IField ParseFrom(JsValue value) => new StringField(value.AsString());
	public static IField ParseFrom(string value) => new StringField(value);
	public static string GetCreateStatement(string field) => $", \"{field}\" VARCHAR not null";
	public string GetQueryStatement(string field) => $"and \"{field}\" = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, Key);
	public void AppendTo(Appender.Row row) => row.Append(Key);
	[Experimental("DuckDBNET001")] public void WriteTo(IDuckDBDataWriter writer, ulong rowIndex) => writer.WriteValue(Key, rowIndex);
	public override string ToString() => Key;
}

internal readonly record struct NullField : IField {
	public static Type? Type { get => null; }
	public static IField ParseFrom(JsValue value) {
		if (!value.IsNull())
			throw new ArgumentException(nameof(value));

		return new NullField();
	}

	public static IField ParseFrom(string value) => throw new NotSupportedException();
	public static string GetCreateStatement(string field) => string.Empty;
	public string GetQueryStatement(string field) => string.Empty;
	public void BindTo(PreparedStatement statement, ref int index) { }
	public void AppendTo(Appender.Row row) { }
	[Experimental("DuckDBNET001")] public void WriteTo(IDuckDBDataWriter writer, ulong rowIndex) { }
}
