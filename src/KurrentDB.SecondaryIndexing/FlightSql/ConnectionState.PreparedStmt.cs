// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Apache.Arrow;
using DotNext.Text;

namespace KurrentDB.SecondaryIndexing.FlightSql;

partial class ConnectionState {
	/// <summary>
	/// To avoid DoS from the single client, we need to limit the number
	/// of prepared statements that can be registered for the same gRPC connection.
	/// </summary>
	private const int MaxStatementCount = 100;

	private readonly ConcurrentDictionary<Guid, PreparedStatement> _statements = new();
	private volatile int _statementCount;

	/// <summary>
	/// Creates the prepared statement.
	/// </summary>
	/// <remarks>
	/// Under the hood, this method rewrites the original query into safe variant for later execution,
	/// effectively implementing a sandbox for the query.
	/// </remarks>
	/// <param name="query">The original query supplied by the FlightSql client.</param>
	/// <param name="handle">The unique identifier of the registered</param>
	/// <param name="statement">The prepared statement object.</param>
	/// <param name="parameters">Arrow schema of the prepared statement parameters.</param>
	/// <returns>
	/// <see langword="true"/> if the internal registry has enough space to place a new prepared statement;
	/// <see langword="false"/> if there is no space in the internal registry.
	/// </returns>
	public bool CreatePreparedStatement(ReadOnlySpan<char> query,
		out Guid handle,
		[NotNullWhen(true)] out PreparedStatement? statement,
		[NotNullWhen(true)] out Schema? parameters) {
		handle = Guid.NewGuid();
		if (!TryIncrementStatementCount()) {
			statement = null;
			parameters = null;
			return false;
		}

		var newStatement = CreatePreparedStatement(query, out parameters);
		var existingStatement = _statements.GetOrAdd(handle, newStatement);

		if (ReferenceEquals(existingStatement, newStatement)) {
			statement = newStatement;
			return true;
		}

		Interlocked.Decrement(ref _statementCount);
		newStatement.Dispose();
		statement = null;
		parameters = null;
		return false;
	}

	/// <summary>
	/// Stores the arguments for the prepared statement.
	/// </summary>
	/// <param name="handle">The unique identifier of the statement.</param>
	/// <param name="arguments">Arrow batch of arguments.</param>
	/// <returns>
	/// <see langword="true"/> if <paramref name="handle"/> statement was registered with <see cref="CreatePreparedStatement"/>;
	/// <see langword="false"/> if the statement doesn't exist.
	/// </returns>
	public bool BindPreparedStatement(Guid handle, RecordBatch arguments) {
		if (!_statements.TryGetValue(handle, out var statement) || !statement.TryIncrementRef())
			return false;

		// nothing to bind, the batch is empty
		if (arguments.Length is 0) {
			arguments.Dispose();
			return true;
		}

		// save the binding context
		var result = statement.TryBind(arguments, out var oldBatch);
		oldBatch?.Dispose();
		statement.DecrementRef();

		return result;
	}

	/// <summary>
	/// Releases the resources associated with the specified prepared statement.
	/// </summary>
	/// <param name="handle">The unique identifier of the prepared statement.</param>
	/// <returns>
	/// <see langword="true"/> if <paramref name="handle"/> statement was registered with <see cref="CreatePreparedStatement"/>;
	/// <see langword="false"/> if the statement doesn't exist.
	/// </returns>
	public bool ClosePreparedStatement(Guid handle) {
		if (!_statements.TryRemove(handle, out var statement))
			return false;

		Interlocked.Decrement(ref _statementCount);
		statement.DecrementRef();
		return true;
	}

	/// <summary>
	/// Tries to get the prepared statement.
	/// </summary>
	/// <param name="handle">The unique identifier of the prepared statement.</param>
	/// <returns>
	/// The prepared statement with the incremented reference counter;
	/// or <see langword="null"/> if the statement doesn't exist.
	/// </returns>
	public PreparedStatement? TryGetPreparedStatement(Guid handle)
		=> _statements.TryGetValue(handle, out var statement) && statement.TryIncrementRef()
			? statement
			: null;

	private bool TryIncrementStatementCount() {
		for (int current = _statementCount, tmp;; current = tmp) {
			if (current >= MaxStatementCount)
				return false;

			tmp = Interlocked.CompareExchange(ref _statementCount, current + 1, current);
			if (tmp == current)
				return true;
		}
	}

	private PreparedStatement CreatePreparedStatement(ReadOnlySpan<char> query,
		out Schema parameters) {
		var buffer = Encoding.UTF8.GetBytes(query, allocator: null);
		try {
			var preparedQuery = engine
				.PrepareQuery(buffer.Span, new() { UseDigitalSignature = false });
			return new(preparedQuery, engine.GetArrowSchema(preparedQuery.Span, out parameters));
		} finally {
			buffer.Dispose();
		}
	}

	/// <summary>
	/// Gets the schema of the dataset to be returned by the prepared statement.
	/// </summary>
	/// <param name="handle">The unique identifier of the prepared statement.</param>
	/// <param name="schema">The dataset schema.</param>
	/// <returns>
	/// <see langword="true"/> if <paramref name="handle"/> statement was registered with <see cref="CreatePreparedStatement"/>;
	/// <see langword="false"/> if the statement doesn't exist.
	/// </returns>
	public bool TryGetPreparedStatementSchema(Guid handle, [NotNullWhen(true)] out Schema? schema) {
		if (_statements.TryGetValue(handle, out var statement)) {
			schema = statement.DatasetSchema;
			return true;
		}

		schema = null;
		return false;
	}
}
