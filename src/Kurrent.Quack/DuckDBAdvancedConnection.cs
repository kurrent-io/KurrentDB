// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotNext.Collections.Specialized;
using DuckDB.NET.Data;

namespace Kurrent.Quack;

public class DuckDBAdvancedConnection : DuckDBConnection {
	private readonly TypeMap<PreparedStatement> _preparedStatements = new();

	/// <summary>
	/// Gets the prepared statement.
	/// </summary>
	/// <remarks>
	/// The caller should not dispose the statement, because it's cached.
	/// </remarks>
	/// <typeparam name="TStatement">The type of the prepared statement.</typeparam>
	/// <returns>The prepared statement.</returns>
	public PreparedStatement GetPreparedStatement<TStatement>()
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
		where TArgs : struct
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
		where TArgs : struct
		where TStatement : IPreparedStatement<TArgs> {
		var statement = GetPreparedStatement<TStatement>();
		if (args is ValueTuple) {
			paramsCount = 0L;
		} else {
			paramsCount = TStatement.Bind(in args, statement).Count;
			Debug.Assert(paramsCount == statement.ParametersCount);
		}

		return statement;
	}

	public StreamQueryResult ExecuteQuery<TQuery>()
		where TQuery : IParameterlessStatement
		=> ExecuteQuery<ValueTuple, TQuery>(new());

	public StreamQueryResult ExecuteQuery<TArgs, TQuery>(in TArgs args)
		where TArgs : struct
		where TQuery : IPreparedStatement<TArgs> {
		var statement = Bind<TArgs, TQuery>(in args, out var paramsCount);

		// execute
		try {
			return statement.ExecuteQuery();
		} finally {
			// clear bindings
			if (paramsCount > 0L)
				statement.ClearBindings();
		}
	}

	public StreamQueryResult<TRow, TQuery> ExecuteQuery<TArgs, TRow, TQuery>(in TArgs args)
		where TArgs : struct
		where TRow : struct
		where TQuery : IPreparedStatement<TArgs>, IDataRowParser<TRow>
		=> new(ExecuteQuery<TArgs, TQuery>(in args));

	public StreamQueryResult<TRow, TQuery> ExecuteQuery<TRow, TQuery>()
		where TRow : struct
		where TQuery : IParameterlessStatement, IDataRowParser<TRow>
		=> ExecuteQuery<ValueTuple, TRow, TQuery>(new ValueTuple());

	protected override void Dispose(bool disposing) {
		if (disposing) {
			foreach (var statement in _preparedStatements) {
				statement.Dispose();
			}
		}

		base.Dispose(disposing);
	}
}
