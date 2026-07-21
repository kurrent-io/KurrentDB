// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using System.Numerics;
using Jint;
using Jint.Native;
using Kurrent.Quack;
using Kurrent.Quack.Threading;
using KurrentDB.Protocol.V2.Indexes;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

/// <summary>
/// A single configured field of a user index. One long-lived instance per field: it holds the
/// field's name, type, nullability and lookup settings, and knows how to write, query and format
/// its own DuckDB column. Values are parsed transiently; nothing per-event is stored on the instance.
/// </summary>
public interface IField {
	/// The logical field name as supplied by the user (e.g. "country").
	string Name { get; }

	/// The DuckDB column name for this field (e.g. "field_country").
	string ColumnName { get; }

	/// The JavaScript selector that extracts this field's value from a mapped record.
	string Selector { get; }

	/// Whether a DuckDB ART index should be built on this field's column for fast equality lookups.
	bool OptimizeLookups { get; }

	/// The column definition for the create table statement, e.g. ", \"field_country\" VARCHAR null".
	string GetCreateStatement(bool nullable);

	/// An equality predicate fragment for a query on this field, e.g. " and \"field_country\" = ?".
	string GetEqualityPredicate();

	/// Appends a NULL value for this field (its selector returned null/undefined).
	void AppendNull(ref BufferedAppender.Row row);

	/// Converts the selector's (non-null) value to its canonical string form. Throws if the value is not of the field's
	/// type; callers run this in the guarded evaluate phase so a bad value drops the event rather than failing a write.
	string FormatValue(JsValue value);

	/// Appends the selector's (non-null) value to the row in the column's native type. Must be called only after
	/// <see cref="FormatValue"/> has validated the same value in the guarded evaluate phase; it does not re-validate.
	void AppendValue(JsValue value, ref BufferedAppender.Row row);

	/// Parses <paramref name="text"/> into the column's type and binds it to the statement. Throws if invalid.
	void BindEquality(PreparedStatement statement, ref int index, string text);

	/// The canonical string form of a query text value, used to match live subscriptions. Throws if invalid.
	string NormalizeValue(ReadOnlySpan<char> text);

	/// Creates the field instance for a configured index field. The single type-dispatch site.
	static IField Create(IndexField field) =>
		field.Type switch {
			IndexFieldType.String => new StringField(field.Name, field.Selector, field.OptimizeLookups),
			IndexFieldType.Double => new DoubleField(field.Name, field.Selector, field.OptimizeLookups),
//			IndexFieldType.Int16 => new Int16Field(field.Name, field.Selector, field.OptimizeLookups),
			IndexFieldType.Int32 => new Int32Field(field.Name, field.Selector, field.OptimizeLookups),
			IndexFieldType.Int64 => new Int64Field(field.Name, field.Selector, field.OptimizeLookups),
//			IndexFieldType.Uint32 => new UInt32Field(field.Name, field.Selector, field.OptimizeLookups),
//			IndexFieldType.Uint64 => new UInt64Field(field.Name, field.Selector, field.OptimizeLookups),
			_ => throw new ArgumentOutOfRangeException(nameof(field), field.Type, "Unsupported index field type")
		};
}

internal abstract class FieldBase(string name, string selector, bool optimizeLookups) : IField {
	public string Name { get; } = name;
	public string Selector { get; } = selector;
	public bool OptimizeLookups { get; } = optimizeLookups;
	public string ColumnName { get; } = UserIndexSql.GetColumnNameFor(name);

	protected abstract string DuckDbType { get; }

	public string GetCreateStatement(bool nullable) => $", \"{ColumnName}\" {DuckDbType} {(nullable ? "null" : "not null")}";
	public string GetEqualityPredicate() => $" and \"{ColumnName}\" = ?";
	public void AppendNull(ref BufferedAppender.Row row) => row.Add(DBNull.Value);

	public abstract string FormatValue(JsValue value);
	public abstract void AppendValue(JsValue value, ref BufferedAppender.Row row);
	public abstract void BindEquality(PreparedStatement statement, ref int index, string text);
	public abstract string NormalizeValue(ReadOnlySpan<char> text);
}

internal sealed class StringField(string name, string selector, bool optimizeLookups)
	: FieldBase(name, selector, optimizeLookups) {
	protected override string DuckDbType => "VARCHAR";

	public override string FormatValue(JsValue value) => value.AsString();
	public override void AppendValue(JsValue value, ref BufferedAppender.Row row) => row.Add(value.AsString());
	public override void BindEquality(PreparedStatement statement, ref int index, string text) => statement.Bind(index++, text);
	public override string NormalizeValue(ReadOnlySpan<char> text) => text.ToString();
}

internal abstract class NumericField<TNumber>(string name, string selector, bool optimizeLookups)
	: FieldBase(name, selector, optimizeLookups)
	where TNumber : struct, INumber<TNumber> {
	public override string FormatValue(JsValue value) => Format(TNumber.CreateChecked(value.AsNumber()));
	public override void AppendValue(JsValue value, ref BufferedAppender.Row row) => AppendNumber(TNumber.CreateChecked(value.AsNumber()), ref row);
	public override void BindEquality(PreparedStatement statement, ref int index, string text) => Bind(statement, ref index, Parse(text));
	public override string NormalizeValue(ReadOnlySpan<char> text) => Format(Parse(text));

	// Row.Add and PreparedStatement.Bind expose typed overloads (no generic numeric overload), so each concrete type binds itself.
	protected abstract void AppendNumber(TNumber value, ref BufferedAppender.Row row);
	protected abstract void Bind(PreparedStatement statement, ref int index, TNumber value);

	private static TNumber Parse(ReadOnlySpan<char> text) => TNumber.Parse(text, CultureInfo.InvariantCulture);
	private static string Format(TNumber value) => value.ToString(null, CultureInfo.InvariantCulture);
}

internal sealed class Int32Field(string name, string selector, bool optimizeLookups)
	: NumericField<int>(name, selector, optimizeLookups) {
	protected override string DuckDbType => "INTEGER";
	protected override void AppendNumber(int value, ref BufferedAppender.Row row) => row.Add(value);
	protected override void Bind(PreparedStatement statement, ref int index, int value) => statement.Bind(index++, value);
}

internal sealed class Int64Field(string name, string selector, bool optimizeLookups)
	: NumericField<long>(name, selector, optimizeLookups) {
	protected override string DuckDbType => "BIGINT";
	protected override void AppendNumber(long value, ref BufferedAppender.Row row) => row.Add(value);
	protected override void Bind(PreparedStatement statement, ref int index, long value) => statement.Bind(index++, value);
}

internal sealed class DoubleField(string name, string selector, bool optimizeLookups)
	: NumericField<double>(name, selector, optimizeLookups) {
	protected override string DuckDbType => "DOUBLE";
	protected override void AppendNumber(double value, ref BufferedAppender.Row row) => row.Add(value);
	protected override void Bind(PreparedStatement statement, ref int index, double value) => statement.Bind(index++, value);
}
