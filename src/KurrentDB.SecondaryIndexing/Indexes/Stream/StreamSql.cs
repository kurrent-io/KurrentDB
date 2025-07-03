// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;
using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Stream;

internal static class StreamSql {
	public record struct GetStreamIdByNameQueryArgs(string StreamName);

	public struct GetStreamIdByNameQuery : IQuery<GetStreamIdByNameQueryArgs, long> {
		public static BindingContext Bind(in GetStreamIdByNameQueryArgs args, PreparedStatement statement)
			=> new(statement) { args.StreamName };

		public static bool UseStreamingMode => false;

		public static ReadOnlySpan<byte> CommandText => "select id from streams where name=$1 limit 1"u8;

		public static long Parse(ref DataChunk.Row row) => row.ReadInt64();
	}

	public struct GetStreamMaxSequencesQuery : IQuery<Optional<long>> {
		public static ReadOnlySpan<byte> CommandText => "select max(id) from streams"u8;

		public static bool UseStreamingMode => false;

		public static Optional<long> Parse(ref DataChunk.Row row) => row.TryReadInt64().ToOptional();
	}

	public record struct StreamSummary(long Id, string Name, long LastLogPosition);

	public readonly record struct UpdateStreamMetadataParams(
		TimeSpan? MaxAge,
		long? MaxCount,
		long? TruncateBefore,
		StreamAcl Acl);

	public static void UpdateStreamMetadata(this DuckDBAdvancedConnection connection,
		UpdateStreamMetadataParams metadata) =>
		connection.ExecuteNonQuery<UpdateStreamMetadataParams, UpdateStreamMetadataStatement>(metadata);

	private struct UpdateStreamMetadataStatement : IPreparedStatement<UpdateStreamMetadataParams> {
		public static BindingContext Bind(in UpdateStreamMetadataParams args, PreparedStatement statement) {
			var bindingContext = new BindingContext(statement) {
				(long?)args.MaxAge?.TotalSeconds,
				args.MaxCount,
				args.TruncateBefore == EventNumber.DeletedStream,
				args.TruncateBefore,
				args.Acl.ReadRoles.Length == 0 ? null : $"[${string.Join(",", args.Acl.ReadRoles)}]"
			};

			return bindingContext;
		}

		public static ReadOnlySpan<byte> CommandText =>
			"""
			UPDATE streams
			SET
				max_age = $1,
				max_count = $2,
				is_deleted = $3,
			    truncate_before = $3,
				acl = $4,
			"""u8;
	}

	public struct GetStreamsSummaryQuery : IQuery<(long LastId, int Take), StreamSummary> {
		public static BindingContext Bind(in (long LastId, int Take) args, PreparedStatement statement) =>
			new(statement) { args.Take, args.LastId };

		public static ReadOnlySpan<byte> CommandText =>
			"""
			SELECT s.id, s.name, max_seq.max_streams_seq
			FROM streams s
			LEFT JOIN (
			    SELECT streams, MAX(streams_seq) AS max_streams_seq
			    FROM idx_all
			    GROUP BY streams
			) max_seq ON s.id = max_seq.streams;
			WHERE id>$1
			LIMIT $2
			"""u8;

		public static StreamSummary Parse(ref DataChunk.Row row) =>
			new(row.ReadInt32(), row.ReadString(), row.ReadInt64());
	}
}
