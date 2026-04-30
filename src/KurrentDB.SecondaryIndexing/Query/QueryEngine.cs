// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Apache.Arrow;
using DotNext;
using DotNext.Buffers;
using DuckDB.NET.Data;
using DuckDB.NET.Native;
using Kurrent.Quack;
using Kurrent.Quack.Arrow;
using Kurrent.Quack.ConnectionPool;
using Kurrent.Quack.Threading;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Indexes.User;

namespace KurrentDB.SecondaryIndexing.Query;

/// <summary>
/// Represents a single entry point to execute SQL queries over KurrentDB indices.
/// </summary>
/// <param name="defaultIndex"></param>
/// <param name="userIndex"></param>
/// <param name="sharedPool"></param>
internal sealed partial class QueryEngine(DefaultIndexProcessor defaultIndex,
	UserIndexEngine userIndex,
	DuckDBConnectionPool sharedPool) : IQueryEngine {
	// 32 bytes key is aligned with HMAC SHA-3 256 hash length
	private readonly ReadOnlyMemory<byte> _signatureKey = RandomNumberGenerator.GetBytes(32);

	public MemoryOwner<byte> PrepareQuery(ReadOnlySpan<byte> queryUtf8, QueryPreparationOptions options) {
		var builder = new PreparedQueryBuilder();
		using var rewrittenQuery = RewriteQuery(queryUtf8, ref builder);

		return builder.Build(rewrittenQuery.Span, options.UseDigitalSignature ? _signatureKey.Span : ReadOnlySpan<byte>.Empty);
	}

	public async ValueTask ExecuteAsync<TConsumer>(ReadOnlyMemory<byte> preparedQuery,
		TConsumer consumer,
		QueryExecutionOptions options,
		CancellationToken token)
		where TConsumer : IQueryResultConsumer {
		var parsedQuery = new PreparedQuery(preparedQuery.Span);
		if (options.CheckIntegrity) {
			CheckIntegrity(in parsedQuery);
		}

		var snapshots = new PoolingBufferWriter<SnapshotInfo> { Capacity = parsedQuery.ViewCount + 1 }; // + default index
		var rental = sharedPool.Rent(out var connection);
		var statement = default(PreparedStatement);
		var reader = default(QueryResultReader);
		var cancellation = connection.InterruptQueryOnCancellation(token);
		try {
			CaptureSnapshots(in parsedQuery, connection, snapshots, token);
			statement = new(connection, parsedQuery.Query);
			consumer.Bind(new QueryBinder(in statement));

			reader = new(in statement, consumer.UseStreaming);
			await consumer.ConsumeAsync(reader, token);
			reader.ThrowOnError(); // to handle query interruption from Quack (see catch block)
		} catch (DuckDBException e) when (e.ErrorType is DuckDBErrorType.Interrupt) {
			throw new OperationCanceledException(token);
		}
		finally {
			await cancellation.DisposeAsync();
			reader?.Dispose();
			statement.Dispose();
			Disposable.Dispose(snapshots.WrittenMemory.Span); // release all captured snapshot
			Disposable.Dispose(new ReadOnlySpan<DuckDBConnectionPool.Scope>(in rental));
			snapshots.Dispose();
		}
	}

	private void CheckIntegrity(ref readonly PreparedQuery parsedQuery) {
		if (!parsedQuery.CheckIntegrity(_signatureKey.Span))
			throw new PreparedQueryIntegrityException();
	}

	private void CaptureSnapshots(ref readonly PreparedQuery preparedQuery,
		DuckDBAdvancedConnection connection,
		PoolingBufferWriter<SnapshotInfo> snapshots,
		CancellationToken token) {
		if (preparedQuery.HasDefaultIndex) {
			// default index detected
			snapshots.Add(new() { Snapshot = defaultIndex.CaptureSnapshot(connection) });
		}

		for (var viewNames = preparedQuery.ViewNames; viewNames.MoveNext(); token.ThrowIfCancellationRequested()) {
			if (userIndex.TryCaptureSnapshot(viewNames.Current, connection, out var readLock, out var snapshot)) {
				// user-defined index detected
				snapshots.Add(new() { Snapshot = snapshot, ReadLock = readLock });
			}
		}
	}

	public Schema GetArrowSchema(ReadOnlySpan<byte> preparedQuery)
		=> GetArrowSchema<Schema, StatementSchemaReflector>(preparedQuery);

	public Schema GetArrowSchema(ReadOnlySpan<byte> preparedQuery, out Schema parametersSchema) {
		(var datasetSchema, parametersSchema) = GetArrowSchema<(Schema, Schema), StatementSchemaReflector>(preparedQuery);
		return datasetSchema;
	}

	private TResult GetArrowSchema<TResult, TReflector>(ReadOnlySpan<byte> preparedQuery)
		where TReflector : ISchemaReflector<TResult>, allows ref struct {
		var parsedQuery = new PreparedQuery(preparedQuery);
		var snapshots = new PoolingBufferWriter<SnapshotInfo> { Capacity = parsedQuery.ViewCount + 1 }; // + default index
		var rental = sharedPool.Rent(out var connection);
		var options = connection.GetArrowOptions();
		var statement = default(PreparedStatement);
		try {
			CaptureSnapshots(in parsedQuery, connection, snapshots, CancellationToken.None);
			statement = new(connection, parsedQuery.Query);
			return TReflector.Reflect(statement, options);
		} finally {
			statement.Dispose();
			options.Dispose();
			Disposable.Dispose(snapshots.WrittenMemory.Span); // release all captured snapshot
			Disposable.Dispose(MemoryMarshal.CreateReadOnlySpan(in rental, 1));
			snapshots.Dispose();
		}
	}

	[StructLayout(LayoutKind.Auto)]
	private struct SnapshotInfo : IDisposable {
		public BufferedView.Snapshot Snapshot;
		public UserIndexEngineSubscription.ReadLock ReadLock;

		public void Dispose() {
			Snapshot.Dispose();
			ReadLock.Dispose();
		}
	}

	private interface ISchemaReflector<out TResult> {
		static abstract TResult Reflect(PreparedStatement statement, ArrowOptions options);
	}

	private readonly ref struct StatementSchemaReflector : ISchemaReflector<Schema>, ISchemaReflector<(Schema, Schema)> {
		static Schema ISchemaReflector<Schema>.Reflect(PreparedStatement statement, ArrowOptions options)
			=> statement.GetArrowSchema(options);

		static (Schema, Schema) ISchemaReflector<(Schema, Schema)>.Reflect(PreparedStatement statement, ArrowOptions options)
			=> (statement.GetArrowSchema(options), statement.GetParameterArrowSchema(options));
	}
}
