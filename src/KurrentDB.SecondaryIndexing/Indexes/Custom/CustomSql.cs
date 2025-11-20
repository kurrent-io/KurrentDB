// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.Common.Utils;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

internal static class CustomSql {
	public record struct GetCheckpointQueryArgs(string IndexName);
	public record struct SetCheckpointQueryArgs(string IndexName, long PreparePosition, long? CommitPosition, long Created);
	public record struct ReadCustomIndexQueryArgs(long StartPosition, long EndPosition, int Count);
	public record struct GetCheckpointResult(long PreparePosition, long? CommitPosition, long Timestamp);
	public record struct LastIndexedRecordResult(long PreparePosition, long? CommitPosition, long Timestamp);

	extension(DuckDBAdvancedConnection connection)
	{
		/// <summary>
		/// Create the table for a custom index
		/// </summary>
		public void CreateCustomIndexNonQuery(string tableName, string partitionKeySqlStatement) {
			var cmd =
				$"""
				 create table if not exists {tableName} (
				 	log_position bigint not null,
				 	commit_position bigint null,
				 	event_number bigint not null,
				 	created bigint not null
				 	{partitionKeySqlStatement}
				 );
				 """;

			connection.ExecuteAdHocNonQuery(cmd.ToUtf8Bytes().Span);
		}

		/// <summary>
		/// Delete the table for a custom index
		/// </summary>
		public void DeleteCustomIndexNonQuery(string tableName) {
			var cmd = $"drop table if exists {tableName}";
			connection.ExecuteAdHocNonQuery(cmd.ToUtf8Bytes().Span);
		}

		/// <summary>
		/// Get the last indexed record
		/// </summary>
		public LastIndexedRecordResult? GetLastIndexedRecordQuery(string tableName) {
			var cmd = $"select log_position, commit_position, created from {tableName} order by rowid desc limit 1";

			using var result = connection.ExecuteAdHocQuery(cmd.ToUtf8Bytes().Span);
			if (result.TryFetch(out var chunk) && chunk.TryRead(out var row)) {
				return new LastIndexedRecordResult {
					PreparePosition = row.ReadInt64(),
					CommitPosition = row.TryReadInt64(),
					Timestamp = row.ReadInt64()
				};
			}

			return null;
		}

		/// <summary>
		/// Get index records for the custom index with a log position greater than the start position
		/// </summary>
		public IEnumerable<IndexQueryRecord> ReadCustomIndexQueryExcl(string tableName, string partitionQuery, ReadCustomIndexQueryArgs args) {
			var cmd = $"select log_position, commit_position, event_number from {tableName} where log_position>{args.StartPosition} and log_position<{args.EndPosition} {partitionQuery} order by rowid limit {args.Count}";
			return GetIndexRecords(connection, cmd);
		}

		/// <summary>
		/// Get index records for the custom index with a log position greater or equal than the start position
		/// </summary>
		public IEnumerable<IndexQueryRecord> ReadCustomIndexQueryIncl(string tableName, string partitionQuery, ReadCustomIndexQueryArgs args) {
			var cmd = $"select log_position, commit_position, event_number from {tableName} where log_position>={args.StartPosition} and log_position<{args.EndPosition} {partitionQuery} order by rowid limit {args.Count}";
			return GetIndexRecords(connection, cmd);
		}

		/// <summary>
		/// Get index records for the custom index with the log position less than the start position
		/// </summary>
		public IEnumerable<IndexQueryRecord> ReadCustomIndexBackQueryExcl(string tableName, string partitionQuery, ReadCustomIndexQueryArgs args) {
			var cmd = $"select log_position, commit_position, event_number from {tableName} where log_position<{args.StartPosition} {partitionQuery} order by rowid desc limit {args.Count}";
			return GetIndexRecords(connection, cmd);
		}

		/// <summary>
		/// Get index records for the custom index with log position less or equal than the start position
		/// </summary>
		public IEnumerable<IndexQueryRecord> ReadCustomIndexBackQueryIncl(string tableName, string partitionQuery, ReadCustomIndexQueryArgs args) {
			var cmd = $"select log_position, commit_position, event_number from {tableName} where log_position<={args.StartPosition} {partitionQuery} order by rowid desc limit {args.Count}";
			return GetIndexRecords(connection, cmd);
		}

		private IEnumerable<IndexQueryRecord> GetIndexRecords(string indexQueryCmd) {
			using var result = connection.ExecuteAdHocQuery(indexQueryCmd.ToUtf8Bytes().Span);
			while (result.TryFetch(out var chunk)) {
				while (chunk.TryRead(out var row)) {
					yield return new IndexQueryRecord(row.ReadInt64(), row.TryReadInt64(), row.ReadInt64());
				}
			}
		}
	}

	/// <summary>
	/// Get the checkpoint
	/// </summary>
	public struct GetCheckpointQuery : IQuery<GetCheckpointQueryArgs, GetCheckpointResult> {
		public static BindingContext Bind(in GetCheckpointQueryArgs args, PreparedStatement statement)
		 		=> new(statement) { args.IndexName };

		public static ReadOnlySpan<byte> CommandText => "select log_position, commit_position, created from idx_custom_checkpoints where index_name=$1 limit 1"u8;

		public static bool UseStreamingMode => false;

		public static GetCheckpointResult Parse(ref DataChunk.Row row) => new(row.ReadInt64(), row.TryReadInt64(), row.ReadInt64());
	}

	/// <summary>
	/// Set the checkpoint
	/// </summary>
	public struct SetCheckpointNonQuery : IPreparedStatement<SetCheckpointQueryArgs> {
		public static BindingContext Bind(in SetCheckpointQueryArgs args, PreparedStatement statement)
			=> new(statement) { args.IndexName, args.PreparePosition, args.CommitPosition, args.Created };

		public static ReadOnlySpan<byte> CommandText => "insert or replace into idx_custom_checkpoints (index_name,log_position,commit_position,created) VALUES ($1,$2,$3,$4)"u8;
	}
}
