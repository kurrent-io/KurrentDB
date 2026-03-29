// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DotNext;
using DotNext.Buffers;
using DuckDB.NET.Data;
using Kurrent.Quack;
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

	public MemoryOwner<byte> PrepareQuery(ReadOnlySpan<byte> queryUtf8, bool digitallySign) {
		var builder = new PreparedQueryBuilder();
		using var rewrittenQuery = RewriteQuery(queryUtf8, ref builder);

		return builder.Build(rewrittenQuery.Span, digitallySign ? _signatureKey.Span : ReadOnlySpan<byte>.Empty);
	}

	public async ValueTask ExecuteAsync<TConsumer>(ReadOnlyMemory<byte> preparedQuery,
		TConsumer consumer,
		bool checkIntegrity,
		CancellationToken token)
		where TConsumer : IQueryResultConsumer {
		var parsedQuery = new PreparedQuery(preparedQuery.Span);
		if (checkIntegrity) {
			CheckIntegrity(in parsedQuery);
		}

		var snapshots = new PoolingBufferWriter<SnapshotInfo> { Capacity = parsedQuery.ViewCount + 1 }; // + default index
		var rental = sharedPool.Rent(out var connection);
		try {
			CaptureSnapshots(in parsedQuery, connection, snapshots, token);
			using var statement = new PreparedStatement(connection, parsedQuery.Query);
			consumer.Bind(new QueryBinder(in statement));

			using var reader = new QueryResultReader(in statement, consumer.UseStreaming);
			await consumer.ConsumeAsync(reader, token);
		} finally {
			Disposable.Dispose(snapshots.WrittenMemory.Span); // release all captured snapshot
			((IDisposable)rental).Dispose();
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

	[StructLayout(LayoutKind.Auto)]
	private struct SnapshotInfo : IDisposable {
		public BufferedView.Snapshot Snapshot;
		public UserIndexEngineSubscription.ReadLock ReadLock;

		public void Dispose() {
			Snapshot.Dispose();
			ReadLock.Dispose();
		}
	}
}
