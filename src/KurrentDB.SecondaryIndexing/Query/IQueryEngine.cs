// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Buffers;

namespace KurrentDB.SecondaryIndexing.Query;

public interface IQueryEngine {
	/// <summary>
	/// Prepares the query for execution.
	/// </summary>
	/// <param name="sqlQuery">The query to prepare.</param>
	/// <param name="digitallySign"><see langword="true"/> to sign the result; otherwise, <see langword="false"/>.</param>
	/// <returns>The prepared query.</returns>
	/// <exception cref="QueryPreparationException">The input query has incorrect syntax.</exception>
	MemoryOwner<byte> PrepareQuery(ReadOnlySpan<byte> sqlQuery, bool digitallySign);

	/// <summary>
	/// Executes the prepared query.
	/// </summary>
	/// <param name="preparedQuery">The prepared query returned by <see cref="PrepareQuery"/> method.</param>
	/// <param name="consumer">The query result consumer.</param>
	/// <param name="checkIntegrity">To check the integrity of the prepared query.</param>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <typeparam name="TConsumer">The type of the consumer.</typeparam>
	/// <returns>The task representing asynchronous state of the operation.</returns>
	/// <exception cref="PreparedQueryIntegrityException">The prepared query is invalid.</exception>
	ValueTask ExecuteAsync<TConsumer>(ReadOnlyMemory<byte> preparedQuery, TConsumer consumer, bool checkIntegrity, CancellationToken token = default)
		where TConsumer : IQueryResultConsumer;
}
