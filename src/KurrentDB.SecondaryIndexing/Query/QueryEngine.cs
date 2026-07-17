// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Apache.Arrow;
using DotNext;
using DotNext.Buffers;
using Kurrent.Quack;
using Kurrent.Quack.Arrow;
using Kurrent.Quack.Threading;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Indexes.User;

namespace KurrentDB.SecondaryIndexing.Query;

/// <summary>
/// Represents a single entry point to execute SQL queries over KurrentDB indices.
/// </summary>
/// <param name="defaultIndex"></param>
/// <param name="userIndex"></param>
/// <param name="executor"></param>
internal sealed partial class QueryEngine(DefaultIndexProcessor defaultIndex,
	UserIndexEngine userIndex,
	DuckDBExecutor executor) : IQueryEngine {
	// 32 bytes key is aligned with HMAC SHA-3 256 hash length
	private readonly ReadOnlyMemory<byte> _signatureKey = RandomNumberGenerator.GetBytes(32);

	public ValueTask<MemoryOwner<byte>> PrepareQueryAsync(ReadOnlyMemory<byte> queryUtf8, QueryPreparationOptions options,
		CancellationToken token = default) {
		var useSignature = options.UseDigitalSignature;
		// The whole rewrite (parse + AST transform + serialize) runs inside one rented connection on a dispatcher.
		// The PreparedQueryBuilder is a ref struct, so it lives entirely within this synchronous lambda — no await crosses it.
		return executor.Execute(connection => {
			var builder = new PreparedQueryBuilder();
			using var rewrittenQuery = RewriteQuery(connection, queryUtf8.Span, ref builder);
			return builder.Build(rewrittenQuery.Span, useSignature ? _signatureKey.Span : ReadOnlySpan<byte>.Empty);
		}, token);
	}

	public ValueTask ExecuteAsync<TConsumer>(ReadOnlyMemory<byte> preparedQuery,
		TConsumer consumer,
		QueryExecutionOptions options,
		CancellationToken token)
		where TConsumer : IQueryResultConsumer {
		var checkIntegrity = options.CheckIntegrity;
		// Spec §6: the whole rent/snapshot/prepare/consume/cleanup pipeline runs inside ONE Execute op. The dispatcher
		// blocks on the consumer by design (dispatcherCount bounds concurrent streams). Quack owns cancellation:
		// it registers InterruptQueryOnCancellation per op and maps an interrupt to OperationCanceledException, so
		// the engine no longer registers its own interrupt nor maps DuckDBException(Interrupt).
		return new ValueTask(executor.Execute(connection => {
			var parsedQuery = new PreparedQuery(preparedQuery.Span);
			if (checkIntegrity)
				CheckIntegrity(in parsedQuery);

			var snapshots = new PoolingBufferWriter<SnapshotInfo> { Capacity = parsedQuery.ViewCount + 1 }; // + default index
			var statement = default(PreparedStatement);
			var reader = default(QueryResultReader);
			try {
				CaptureSnapshots(in parsedQuery, connection, snapshots, token);
				statement = new(connection, parsedQuery.Query);
				consumer.Bind(new QueryBinder(in statement));

				reader = new(in statement, consumer.UseStreaming);
				consumer.ConsumeAsync(reader, token).AsTask().GetAwaiter().GetResult(); // dispatcher blocks: by design
				reader.ThrowOnError();
				return 0;
			} finally {
				reader?.Dispose();
				statement.Dispose();
				Disposable.Dispose(snapshots.WrittenMemory.Span); // release all captured snapshot
				snapshots.Dispose();
			}
		}, token).AsTask());
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
		// No consume loop here — this is metadata reflection. Copy the span so it can be captured by the lambda, then
		// run the whole capture-snapshots/prepare/reflect body on a dispatcher against a rented connection. This is a
		// synchronous API, so we block on the dispatcher op (the dispatcher is a dedicated thread — no deadlock).
		var queryBytes = preparedQuery.ToArray();
		return executor.Execute(connection => {
			var parsedQuery = new PreparedQuery(queryBytes);
			var snapshots = new PoolingBufferWriter<SnapshotInfo> { Capacity = parsedQuery.ViewCount + 1 }; // + default index
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
				snapshots.Dispose();
			}
		}, CancellationToken.None).AsTask().GetAwaiter().GetResult();
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
