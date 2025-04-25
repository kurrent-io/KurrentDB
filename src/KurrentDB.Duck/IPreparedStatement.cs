// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KurrentDB.Duck;

/// <summary>
/// Represents the prepared statement.
/// </summary>
public interface IPreparedStatement {
	/// <summary>
	/// Represents UTF-8 encoded query string.
	/// </summary>
	static abstract ReadOnlySpan<byte> CommandText { get; }
}

/// <summary>
/// Represents the prepared statement.
/// </summary>
/// <typeparam name="TArgs">The tuple that represents parameters.</typeparam>
public interface IPreparedStatement<TArgs> : IPreparedStatement
	where TArgs : struct, ITuple {

	/// <summary>
	/// Binds arguments to the statement parameters.
	/// </summary>
	/// <param name="args">The arguments to be bounded.</param>
	/// <param name="source">The binding source.</param>
	/// <returns>The binding between formal parameters and actual arguments.</returns>
	static abstract BindingContext Bind(ref readonly TArgs args, BindingSource source);
}

/// <summary>
/// Represents the prepared statement without formal parameters.
/// </summary>
public interface IParameterlessStatement : IPreparedStatement<ValueTuple> {
	static BindingContext IPreparedStatement<ValueTuple>.Bind(ref readonly ValueTuple args, BindingSource source)
		=> new(source);
}

/// <summary>
/// Represents the prepared query with formal parameters and the row schema.
/// </summary>
/// <typeparam name="TInput"></typeparam>
/// <typeparam name="TOutput"></typeparam>
public interface IQuery<TInput, out TOutput> : IPreparedStatement<TInput>, IRowParser<TOutput>
	where TInput : struct, ITuple
	where TOutput : struct, ITuple;

/// <summary>
/// Represents the prepared query without formal parameters and the row schema.
/// </summary>
/// <typeparam name="TOutput"></typeparam>
public interface IQuery<out TOutput> : IParameterlessStatement, IQuery<ValueTuple, TOutput>
	where TOutput : struct, ITuple;

/// <summary>
/// Represents the binding source.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct BindingSource {
	internal readonly PreparedStatement Statement;

	internal BindingSource(PreparedStatement statement) => Statement = statement;
}

/// <summary>
/// Provides a binding between formal parameters and the actual arguments.
/// </summary>
/// <param name="source">The binding source.</param>
[StructLayout(LayoutKind.Auto)]
public struct BindingContext(BindingSource source) : IEnumerable<object> {
	private int index = 1;

	internal readonly int Count => index - 1;

	public void Add(int value) => source.Statement.Bind(index++, value);
	public void Add(uint value) => source.Statement.Bind(index++, value);
	public void Add(long value) => source.Statement.Bind(index++, value);
	public void Add(ulong value) => source.Statement.Bind(index++, value);
	public void Add(DBNull value) => source.Statement.Bind(index++, value);
	public void Add(DateTime value) => source.Statement.Bind(index++, value);
	public void Add(ReadOnlySpan<byte> buffer) => source.Statement.Bind(index++, buffer);
	public void Add(ReadOnlySpan<char> chars) => source.Statement.Bind(index++, chars);

	public void Add(string? str) {
		if (str is not null) {
			Add(str.AsSpan());
		} else {
			Add(DBNull.Value);
		}
	}

	private static IEnumerator<object> GetEmptyEnumerator() => Enumerable.Empty<object>().GetEnumerator();

	IEnumerator<object> IEnumerable<object>.GetEnumerator() => GetEmptyEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEmptyEnumerator();
}
