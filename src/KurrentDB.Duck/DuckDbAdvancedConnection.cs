// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotNext.Collections.Specialized;
using DuckDB.NET.Data;

namespace KurrentDB.Duck;

public class DuckDbAdvancedConnection : DuckDBConnection {
	private readonly TypeMap<PreparedStatement> _preparedStatements = new();

	/// <summary>
	/// Gets the prepared statement.
	/// </summary>
	/// <typeparam name="TStatement">The type of the prepared statement.</typeparam>
	/// <returns>The prepared statement.</returns>
	public PreparedStatement Prepare<TStatement>()
		where TStatement : IPreparedStatement {

		if (!_preparedStatements.TryGetValue<TStatement>(out var result)) {
			result = new(this, TStatement.CommandText);
			_preparedStatements.Add<TStatement>(result);
		}

		return result;
	}

	public long ExecuteNonQuery<TStatement>()
		where TStatement : IParameterlessStatement
		=> ExecuteNonQuery<ValueTuple, TStatement>(new());

	public long ExecuteNonQuery<TArgs, TStatement>(in TArgs args)
		where TArgs : struct, ITuple
		where TStatement : IPreparedStatement<TArgs> {
		var statement = Bind<TArgs, TStatement>(in args, out var paramsCount);

		// execute
		long rowsChanged;
		try {
			rowsChanged = statement.ExecuteNonQuery();
		} finally {
			// clear bindings
			if (paramsCount > 0L)
				statement.ClearBindings();
		}

		return rowsChanged;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private PreparedStatement Bind<TArgs, TStatement>(ref readonly TArgs args, out long paramsCount)
		where TArgs : struct, ITuple
		where TStatement : IPreparedStatement<TArgs> {
		var statement = Prepare<TStatement>();
		if (args is ValueTuple) {
			paramsCount = 0L;
		} else {
			paramsCount = TStatement.Bind(in args, new(statement)).Count;
			Debug.Assert(paramsCount == statement.ParametersCount);
		}

		return statement;
	}

	public StreamQueryResult ExecuteQuery<TStatement>()
		where TStatement : IParameterlessStatement
		=> ExecuteQuery<ValueTuple, TStatement>(new());

	public StreamQueryResult ExecuteQuery<TArgs, TStatement>(in TArgs args)
		where TArgs : struct, ITuple
		where TStatement : IPreparedStatement<TArgs> {
		var statement = Bind<TArgs, TStatement>(in args, out var paramsCount);

		// execute
		try {
			return statement.ExecuteQuery();
		} finally {
			// clear bindings
			if (paramsCount > 0L)
				statement.ClearBindings();
		}
	}

	public StreamQueryResult<TRow, TStatement> ExecuteQuery<TArgs, TRow, TStatement>(in TArgs args)
		where TArgs : struct, ITuple
		where TRow : struct, ITuple
		where TStatement : IQuery<TArgs, TRow>
		=> new(ExecuteQuery<TArgs, TStatement>(in args));

	public StreamQueryResult<TRow, TStatement> ExecuteQuery<TRow, TStatement>()
		where TRow : struct, ITuple
		where TStatement : IQuery<TRow>
		=> ExecuteQuery<ValueTuple, TRow, TStatement>(new ValueTuple());

	protected override void Dispose(bool disposing) {
		if (disposing) {
			foreach (var statement in _preparedStatements) {
				statement.Dispose();
			}
		}

		base.Dispose(disposing);
	}
}
