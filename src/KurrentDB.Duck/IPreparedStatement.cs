// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;

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
	/// <param name="statement">The binding source.</param>
	/// <returns>The binding between formal parameters and actual arguments.</returns>
	static abstract BindingContext Bind(ref readonly TArgs args, PreparedStatement statement);
}

/// <summary>
/// Represents the prepared statement without formal parameters.
/// </summary>
public interface IParameterlessStatement : IPreparedStatement<ValueTuple> {
	static BindingContext IPreparedStatement<ValueTuple>.Bind(ref readonly ValueTuple args, PreparedStatement statement)
		=> new(statement);
}
