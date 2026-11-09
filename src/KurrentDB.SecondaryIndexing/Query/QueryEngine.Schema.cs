// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Apache.Arrow;
using DotNext;
using DotNext.Buffers;
using Kurrent.Quack;
using Kurrent.Quack.Arrow;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Indexes.User;
using KurrentDB.SecondaryIndexing.Indexes.User.Management;

namespace KurrentDB.SecondaryIndexing.Query;

partial class QueryEngine {
	private static ReadOnlySpan<byte> SelectQueryUtf8 => "SELECT * FROM "u8;

	private const string SystemSchema = "kdb";
	private const string UserIndicesSchema = "usr";
	private const string DefaultIndexTableName = "records";

	IEnumerable<TableInfo> IDatabaseSchemaProvider.Tables => userIndex.Indices
		.Select(static indexName => new TableInfo(UserIndicesSchema, indexName))
		.Append(new(SystemSchema, DefaultIndexTableName));

	// Maps the table to its view directly instead of parsing and rewriting a query
	Schema IDatabaseSchemaProvider.GetTableSchema(TableInfo table) {
		var (viewName, isDefaultIndex) = table.Schema switch {
			SystemSchema when table.Name is DefaultIndexTableName => (DefaultSql.DefaultIndexViewName, true),
			SystemSchema => throw new UnsupportedSystemTableException(table.Name),
			UserIndicesSchema => (UserIndexSql.GetViewNameFor(table.Name), false),
			_ => throw new UnsupportedSchemaException(table.Schema),
		};

		// The buffer holds "SELECT * FROM <view>". The view name is encoded once, right after the prefix,
		// so the same bytes are both the snapshot lookup key and the tail of the query.
		var bufferSize = SelectQueryUtf8.Length + Encoding.UTF8.GetByteCount(viewName);
		using var buffer = (uint)bufferSize <= (uint)SpanOwner<byte>.StackallocThreshold
			? stackalloc byte[bufferSize]
			: new SpanOwner<byte>(bufferSize);
		SelectQueryUtf8.CopyTo(buffer.Span);
		var viewNameLength = Encoding.UTF8.GetBytes(viewName, buffer.Span.Slice(SelectQueryUtf8.Length));
		ReadOnlySpan<byte> queryUtf8 = buffer.Span.Slice(0, SelectQueryUtf8.Length + viewNameLength);

		using var rental = sharedPool.Rent(out var connection);
		var options = connection.GetArrowOptions();
		var snapshot = default(SnapshotInfo);
		var hasSnapshot = false;
		var statement = default(PreparedStatement);
		try {
			// capture the snapshot the same way the query execution does, so the view binds identically
			if (isDefaultIndex) {
				snapshot.Snapshot = defaultIndex.CaptureSnapshot(connection);
			} else if (!userIndex.TryCaptureSnapshot(queryUtf8.Slice(SelectQueryUtf8.Length), connection, out snapshot.ReadLock,
				           out snapshot.Snapshot)) {
				throw new UserIndexNotFoundException(table.Name);
			}

			hasSnapshot = true;
			statement = new(connection, queryUtf8);
			return statement.GetArrowSchema(options);
		} finally {
			statement.Dispose();
			if (hasSnapshot)
				snapshot.Dispose();

			options.Dispose();
		}
	}
}
