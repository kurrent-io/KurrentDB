// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using System.Numerics;
using DotNext.Patterns;
using Jint;
using Jint.Native;
using Kurrent.Quack;
using Kurrent.Quack.Threading;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

public interface IField {
	static abstract string GetCreateStatement(string field);
	string GetQueryStatement(string field);
	void BindTo(PreparedStatement statement, ref int index);
	void AppendTo(BufferedAppender.Row row);
}

public interface IField<out TSelf> : IField
	where TSelf : IField<TSelf>
{
	static abstract TSelf ParseFrom(JsValue value);
	static abstract TSelf ParseFrom(string value);
}

file interface INumericField<out TSelf, in TNumber> : IField<TSelf>
	where TSelf : struct, INumericField<TSelf, TNumber>
	where TNumber : struct, INumber<TNumber> {
	static abstract TSelf Create(TNumber value);

	static TSelf IField<TSelf>.ParseFrom(JsValue value) => TSelf.Create(TNumber.CreateChecked(value.AsNumber()));

	static TSelf IField<TSelf>.ParseFrom(string value) => TSelf.Create(TNumber.Parse(value, provider: null));
}

internal readonly record struct Int16Field(short Key) : INumericField<Int16Field, short> {
	public static Int16Field Create(short value) => new(value);
	public static string GetCreateStatement(string field) => $", \"{field}\" SMALLINT not null";
	public string GetQueryStatement(string field) => $"and \"{field}\" = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, Key);
	public void AppendTo(BufferedAppender.Row row) => row.Add(Key);
	public override string ToString() => Key.ToString();
}

internal readonly record struct Int32Field(int Key) : INumericField<Int32Field, int> {
	public static Int32Field Create(int value) => new(value);
	public static string GetCreateStatement(string field) => $", \"{field}\" INTEGER not null";
	public string GetQueryStatement(string field) => $"and \"{field}\" = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, Key);
	public void AppendTo(BufferedAppender.Row row) => row.Add(Key);
	public override string ToString() => Key.ToString();
}

internal readonly record struct Int64Field(long Key) : INumericField<Int64Field, long> {
	public static Int64Field Create(long value) => new(value);
	public static string GetCreateStatement(string field) => $", \"{field}\" BIGINT not null";
	public string GetQueryStatement(string field) => $"and \"{field}\" = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, Key);
	public void AppendTo(BufferedAppender.Row row) => row.Add(Key);
	public override string ToString() => Key.ToString();
}

internal readonly record struct UInt32Field(uint Key) : INumericField<UInt32Field, uint> {
	public static UInt32Field Create(uint value) => new(value);
	public static string GetCreateStatement(string field) => $", \"{field}\" UINTEGER not null";
	public string GetQueryStatement(string field) => $"and \"{field}\" = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, Key);
	public void AppendTo(BufferedAppender.Row row) => row.Add(Key);
	public override string ToString() => Key.ToString();
}

internal readonly record struct UInt64Field(ulong Key) : INumericField<UInt64Field, ulong> {
	public static UInt64Field Create(ulong value) => new(value);
	public static string GetCreateStatement(string field) => $", \"{field}\" UBIGINT not null";
	public string GetQueryStatement(string field) => $"and \"{field}\" = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, Key);
	public void AppendTo(BufferedAppender.Row row) => row.Add(Key);
	public override string ToString() => Key.ToString();
}

internal readonly record struct DoubleField(double Key) : INumericField<DoubleField, double> {
	public static DoubleField Create(double value) => new(value);
	public static string GetCreateStatement(string field) => $", \"{field}\" DOUBLE not null";
	public string GetQueryStatement(string field) => $"and \"{field}\" = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, Key);
	public void AppendTo(BufferedAppender.Row row) => row.Add(Key);
	public override string ToString() => Key.ToString(CultureInfo.InvariantCulture);
}

internal readonly record struct StringField(string Key) : IField<StringField> {
	public static StringField ParseFrom(JsValue value) => ParseFrom(value.AsString());
	public static StringField ParseFrom(string value) => new(value);
	public static string GetCreateStatement(string field) => $", \"{field}\" VARCHAR not null";
	public string GetQueryStatement(string field) => $"and \"{field}\" = ?";
	public void BindTo(PreparedStatement statement, ref int index) => statement.Bind(index++, Key);
	public void AppendTo(BufferedAppender.Row row) => row.Add(Key);
	public override string ToString() => Key;
}

internal sealed class NullField : IField<NullField>, ISingleton<NullField> {
	public static NullField Instance { get; } = new();

	private NullField() {
	}

	public static NullField ParseFrom(JsValue value) {
		return !value.IsNull() ? throw new ArgumentException(null, nameof(value)) : new NullField();
	}

	public static NullField ParseFrom(string value) => throw new NotSupportedException();
	public static string GetCreateStatement(string field) => string.Empty;
	public string GetQueryStatement(string field) => string.Empty;
	public void BindTo(PreparedStatement statement, ref int index) { }
	public void AppendTo(BufferedAppender.Row row) { }
}
