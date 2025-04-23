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

	public long ExecuteNonQuery<TArgs, TStatement>(TArgs args)
		where TArgs : struct, ITuple
		where TStatement : IPreparedStatement<TArgs> {

		var statement = Prepare<TStatement>();
		long count;
		if (args is ValueTuple) {
			count = 0L;
		} else {
			count = TStatement.Bind(ref args, new(statement)).Count;
			Debug.Assert(count == statement.ParametersCount);
		}

		// execute
		var queryResult = default(QueryResult);
		long rowsChanged;
		try {
			queryResult = statement.Execute();
			rowsChanged = queryResult.RowsChanged;
		} finally {
			queryResult.Dispose();

			// clear bindings
			if (count > 0L)
				statement.ClearBindings();
		}

		return rowsChanged;
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			foreach (var statement in _preparedStatements) {
				statement.Dispose();
			}
		}

		base.Dispose(disposing);
	}
}
