// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Stream;

internal static class StreamSql {
	public static long? GetStreamIdByName(
		this DuckDBAdvancedConnection connection,
		string streamName
	) =>
		connection.QueryFirstOrDefault<GetStreamIdByNameQueryArgs, long, GetStreamIdByNameQuery>(
			new GetStreamIdByNameQueryArgs(streamName)
		);

	public static long? GetStreamIdByName(
		this DuckDBConnectionPool pool,
		string streamName
	) =>
		pool.QueryFirstOrDefault<GetStreamIdByNameQueryArgs, long, GetStreamIdByNameQuery>(
			new GetStreamIdByNameQueryArgs(streamName)
		);

	private record struct GetStreamIdByNameQueryArgs(string StreamName);

	private struct GetStreamIdByNameQuery : IQuery<GetStreamIdByNameQueryArgs, long> {
		public static BindingContext Bind(in GetStreamIdByNameQueryArgs args, PreparedStatement statement)
			=> new(statement) { args.StreamName };

		public static bool UseStreamingMode => false;

		public static ReadOnlySpan<byte> CommandText => "select id from streams where name=$1 limit 1"u8;

		public static long Parse(ref DataChunk.Row row) => row.ReadInt64();
	}

	public static long? GetStreamMaxSequences(this DuckDBAdvancedConnection connection) =>
		connection.QueryFirstOrDefault<Optional<long>, GetStreamMaxSequencesQuery>()?.OrNull();

	public static long? GetStreamMaxSequences(this DuckDBConnectionPool pool) =>
		pool.QueryFirstOrDefault<Optional<long>, GetStreamMaxSequencesQuery>()?.OrNull();

	private struct GetStreamMaxSequencesQuery : IQuery<Optional<long>> {
		public static ReadOnlySpan<byte> CommandText => "select max(id) from streams"u8;

		public static bool UseStreamingMode => false;

		public static Optional<long> Parse(ref DataChunk.Row row) => row.TryReadInt64().ToOptional();
	}

	public readonly record struct UpdateStreamMetadataParams(
		string StreamName,
		long MaxAge, // TODO: Make nullable after merging: https://github.com/kurrent-io/Kurrent.Quack/pull/6
		long MaxCount, // TODO: Make nullable after merging: https://github.com/kurrent-io/Kurrent.Quack/pull/6
		bool IsDeleted,
		long TruncateBefore, // TODO: Make nullable after merging: https://github.com/kurrent-io/Kurrent.Quack/pull/6
		string Acl // TODO: Make nullable after merging: https://github.com/kurrent-io/Kurrent.Quack/pull/6
	) {
		public static UpdateStreamMetadataParams From(string streamName, StreamMetadata streamMetadata) =>
			new(
				streamName,
				(long?)streamMetadata.MaxAge?.TotalSeconds ??
				long.MinValue, // TODO: remove default value after merging: https://github.com/kurrent-io/Kurrent.Quack/pull/6
				streamMetadata.MaxCount ??
				long.MinValue, // TODO: remove default value after merging: https://github.com/kurrent-io/Kurrent.Quack/pull/6
				streamMetadata.TruncateBefore == EventNumber.DeletedStream,
				streamMetadata.TruncateBefore ??
				long.MinValue, // TODO: remove default value after merging: https://github.com/kurrent-io/Kurrent.Quack/pull/6
				streamMetadata.Acl == null || streamMetadata.Acl.ReadRoles.Length == 0
					? "" // TODO: use null after merging: https://github.com/kurrent-io/Kurrent.Quack/pull/6
					: $"[${string.Join(",", streamMetadata.Acl.ReadRoles)}]"
			);
	}

	public static void UpdateStreamMetadata(
		this DuckDBAdvancedConnection connection,
		string streamName,
		StreamMetadata metadata
	) =>
		connection.ExecuteNonQuery<UpdateStreamMetadataParams, UpdateStreamMetadataStatement>(
			UpdateStreamMetadataParams.From(streamName, metadata)
		);

	private struct UpdateStreamMetadataStatement : IPreparedStatement<UpdateStreamMetadataParams> {
		public static BindingContext Bind(in UpdateStreamMetadataParams args, PreparedStatement statement) =>
			new(statement) {
				args.StreamName,
				args.MaxAge,
				args.MaxCount,
				args.IsDeleted,
				args.TruncateBefore,
				args.Acl
			};

		public static ReadOnlySpan<byte> CommandText =>
			"""
			UPDATE streams
			SET
				max_age = $2,
				max_count = $3,
				is_deleted = $4,
			    truncate_before = $5,
				acl = $6,
			where
			    name = $1
			"""u8;
	}

	public record struct StreamSummary(
		long Id,
		string Name,
		ulong NameHash,
		long? MaxAge,
		long? MaxCount,
		bool IsDeleted,
		long? TruncateBefore,
		string? Acl
	);


	public static StreamSummary? GetStreamsSummary(
		this DuckDBConnectionPool pool,
		string streamName
	) =>
		pool.Query<GetStreamSummaryArgs, StreamSummary, GetStreamSummaryQuery>(new GetStreamSummaryArgs(streamName))
			.FirstOrDefault();

	private readonly record struct GetStreamSummaryArgs(string StreamName);

	private struct GetStreamSummaryQuery : IQuery<GetStreamSummaryArgs, StreamSummary> {
		public static BindingContext Bind(in GetStreamSummaryArgs args, PreparedStatement statement) =>
			new(statement) { args.StreamName };

		public static ReadOnlySpan<byte> CommandText =>
			"""
			SELECT
			    s.id,
			    s.name,
			    s.name_hash,
			    s.max_age,
			    s.max_count,
			    s.is_deleted,
			    s.truncate_before,
			    s.acl
			FROM streams s
			WHERE name = $1
			"""u8;

		public static StreamSummary Parse(ref DataChunk.Row row) =>
			new(
				row.ReadInt32(),
				row.ReadString(),
				row.ReadUInt64(),
				row.TryReadInt64(),
				row.TryReadInt64(),
				row.ReadBoolean(),
				row.TryReadInt64(),
				row.TryReadString()
			);
	}
}
