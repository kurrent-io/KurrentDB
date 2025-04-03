// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using Microsoft.Data.Sqlite;

namespace EventStore.Core.TransactionLog.Scavenging.Sqlite;

public class SqliteOriginalStreamScavengeMap<TKey> : IInitializeSqliteBackend, IOriginalStreamScavengeMap<TKey> {
	private readonly string _keyTypeOverride;
	private AddCommand _add;
	private SetTombstoneCommand _setTombstone;
	private SetMetadataCommand _setMetadata;
	private SetDiscardPointsCommand _setDiscardPoints;
	private GetCommand _get;
	private GetChunkExecutionInfoCommand _getChunkExecutionInfo;
	private DeleteCommand _delete;
	private DeleteManyCommand _deleteMany;
	private FromCheckpointCommand _fromCheckpoint;
	private AllRecordsCommand _all;
	private ActiveRecordsCommand _active;
	private static Func<SqliteDataReader, OriginalStreamData> _readOriginalStreamData;

	private string TableName { get; }
	
	public SqliteOriginalStreamScavengeMap(string name, string keyTypeOverride = null) {
		TableName = name;
		_keyTypeOverride = keyTypeOverride;

		_readOriginalStreamData = reader => {
			var d = new OriginalStreamData();
			d.IsTombstoned = reader.GetBoolean(0);
			d.MaxAge = SqliteBackend.GetTimeSpanFromSeconds(1, reader);
			d.MaxCount = SqliteBackend.GetNullableFieldValue<long?>(2, reader);
			d.TruncateBefore = SqliteBackend.GetNullableFieldValue<long?>(3, reader);

			var discardPoint = SqliteBackend.GetNullableFieldValue<long?>(4, reader);
			if (discardPoint.HasValue) {
				d.DiscardPoint = DiscardPoint.DiscardBefore(discardPoint.Value);
			}

			var maybeDiscardPoint = SqliteBackend.GetNullableFieldValue<long?>(5, reader);
			if (maybeDiscardPoint.HasValue) {
				d.MaybeDiscardPoint = DiscardPoint.DiscardBefore(maybeDiscardPoint.Value);
			}
			
			d.Status = reader.GetFieldValue<CalculationStatus>(6);

			return d;
		};
	}

	public void Initialize(SqliteBackend sqlite) {
		var keyType = _keyTypeOverride ?? SqliteTypeMapping.GetTypeName<TKey>();
		var sql = $@"
				CREATE TABLE IF NOT EXISTS {TableName} (
					key {keyType} PRIMARY KEY,
					isTombstoned      INTEGER DEFAULT 0,
					maxAge            INTEGER NULL,
					maxCount          INTEGER NULL,
					truncateBefore    INTEGER NULL,
					discardPoint      INTEGER DEFAULT 0,
					maybeDiscardPoint INTEGER DEFAULT 0,
					status            INTEGER DEFAULT 0);
				CREATE INDEX IF NOT EXISTS {TableName}KeyStatus ON {TableName} (status, key)";
		
		sqlite.InitializeDb(sql);

		_add = new AddCommand(TableName, sqlite);
		_setTombstone = new SetTombstoneCommand(TableName, sqlite);
		_setMetadata = new SetMetadataCommand(TableName, sqlite);
		_setDiscardPoints = new SetDiscardPointsCommand(TableName, sqlite);
		_get = new GetCommand(TableName, sqlite);
		_getChunkExecutionInfo = new GetChunkExecutionInfoCommand(TableName, sqlite);
		_delete = new DeleteCommand(TableName, sqlite);
		_deleteMany = new DeleteManyCommand(TableName, sqlite);
		_fromCheckpoint = new FromCheckpointCommand(TableName, sqlite);
		_all = new AllRecordsCommand(TableName, sqlite);
		_active = new ActiveRecordsCommand(TableName, sqlite);
	}

	public OriginalStreamData this[TKey key] {
		set => _add.Execute(key, value);
	}

	public bool TryGetValue(TKey key, out OriginalStreamData value) {
		return _get.TryExecute(key, out value);
	}

	public bool TryRemove(TKey key, out OriginalStreamData value) {
		return _delete.TryExecute(key, out value);
	}
	
	public void SetTombstone(TKey key) {
		_setTombstone.Execute(key);
	}

	public void SetMetadata(TKey key, StreamMetadata metadata) {
		_setMetadata.Execute(key, metadata);
	}
	
	public void SetDiscardPoints(TKey key, CalculationStatus status, DiscardPoint discardPoint, DiscardPoint maybeDiscardPoint) {
		_setDiscardPoints.Execute(key, status, discardPoint, maybeDiscardPoint);
	}

	public bool TryGetChunkExecutionInfo(TKey key, out ChunkExecutionInfo details) {
		return _getChunkExecutionInfo.TryExecute(key, out details);
	}

	public IEnumerable<KeyValuePair<TKey, OriginalStreamData>> AllRecords() {
		return _all.Execute();
	}

	public IEnumerable<KeyValuePair<TKey, OriginalStreamData>> ActiveRecords() {
		return _active.Execute();
	}

	public IEnumerable<KeyValuePair<TKey, OriginalStreamData>> ActiveRecordsFromCheckpoint(TKey checkpoint) {
		return _fromCheckpoint.Execute(checkpoint);
	}

	public void DeleteMany(bool deleteArchived) {
		_deleteMany.Execute(deleteArchived);
	}

	private class AddCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;
		private readonly SqliteParameter _isTombstonedParam;
		private readonly SqliteParameter _maxAgeParam;
		private readonly SqliteParameter _maxCountParam;
		private readonly SqliteParameter _truncateBeforeParam;
		private readonly SqliteParameter _discardPointParam;
		private readonly SqliteParameter _maybeDiscardPointParam;
		private readonly SqliteParameter _statusParam;

		public AddCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					INSERT INTO {tableName} VALUES(
						$key,
						$isTombstoned,
						$maxAge,
						$maxCount,
						$truncateBefore,
						$discardPoint,
						$maybeDiscardPoint,
						$status)
					ON CONFLICT(key) DO UPDATE SET
						isTombstoned = $isTombstoned,
						maxAge = $maxAge,
						maxCount = $maxCount,
						truncateBefore = $truncateBefore,
						discardPoint = $discardPoint,
						maybeDiscardPoint = $maybeDiscardPoint,
						status = $status";

			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_keyParam = _cmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_isTombstonedParam = _cmd.Parameters.Add("$isTombstoned", SqliteType.Integer);
			_maxAgeParam = _cmd.Parameters.Add("$maxAge", SqliteType.Integer);
			_maxCountParam = _cmd.Parameters.Add("$maxCount", SqliteType.Integer);
			_truncateBeforeParam = _cmd.Parameters.Add("$truncateBefore", SqliteType.Integer);
			_discardPointParam = _cmd.Parameters.Add("$discardPoint", SqliteType.Integer);
			_maybeDiscardPointParam = _cmd.Parameters.Add("$maybeDiscardPoint", SqliteType.Integer);
			_statusParam = _cmd.Parameters.Add("$status", SqliteType.Integer);
			_cmd.Prepare();

			_sqlite = sqlite;
		}

		public void Execute(TKey key, OriginalStreamData value) {
			_keyParam.Value = key;
			_isTombstonedParam.Value = value.IsTombstoned;
			_maxAgeParam.Value = value.MaxAge.HasValue ? (object)(long)value.MaxAge.Value.TotalSeconds : DBNull.Value;
			_maxCountParam.Value = value.MaxCount.HasValue ? (object)value.MaxCount : DBNull.Value;
			_truncateBeforeParam.Value =
				value.TruncateBefore.HasValue ? (object)value.TruncateBefore : DBNull.Value;
			_discardPointParam.Value = value.DiscardPoint.FirstEventNumberToKeep;
			_maybeDiscardPointParam.Value = value.MaybeDiscardPoint.FirstEventNumberToKeep;
			_statusParam.Value = value.Status;

			_sqlite.ExecuteNonQuery(_cmd);
		}
	}
	
	private class SetTombstoneCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;

		public SetTombstoneCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					INSERT INTO {tableName} (key, isTombstoned, status)
					VALUES ($key, 1, {(int)CalculationStatus.Active})
					ON CONFLICT(key) DO UPDATE SET
						isTombstoned = 1,
						status = {(int)CalculationStatus.Active}";
			
			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_keyParam = _cmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_cmd.Prepare();

			_sqlite = sqlite;
		}

		public void Execute(TKey key) {
			_keyParam.Value = key;
			_sqlite.ExecuteNonQuery(_cmd);
		}
	}
	
	private class SetMetadataCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;
		private readonly SqliteParameter _maxAgeParam;
		private readonly SqliteParameter _maxCountParam;
		private readonly SqliteParameter _truncateBeforeParam;

		public SetMetadataCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					INSERT INTO {tableName} (key, maxAge, maxCount, truncateBefore, status)
					VALUES($key, $maxAge, $maxCount, $truncateBefore, {(int)CalculationStatus.Active})
					ON CONFLICT(key) DO UPDATE SET
						maxAge = $maxAge,
						maxCount = $maxCount,
						truncateBefore = $truncateBefore,
						status = {(int)CalculationStatus.Active}";

			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_keyParam = _cmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_maxAgeParam = _cmd.Parameters.Add("$maxAge", SqliteType.Integer);
			_maxCountParam = _cmd.Parameters.Add("$maxCount", SqliteType.Integer);
			_truncateBeforeParam = _cmd.Parameters.Add("$truncateBefore", SqliteType.Integer);
			_cmd.Prepare();

			_sqlite = sqlite;
		}

		public void Execute(TKey key, StreamMetadata value) {
			_keyParam.Value = key;
			_maxAgeParam.Value = value.MaxAge.HasValue ? (object)(long)value.MaxAge.Value.TotalSeconds : DBNull.Value;
			_maxCountParam.Value = value.MaxCount.HasValue ? (object)value.MaxCount : DBNull.Value;
			_truncateBeforeParam.Value = value.TruncateBefore.HasValue ? (object)value.TruncateBefore : DBNull.Value;
			_sqlite.ExecuteNonQuery(_cmd);
		}
	}

	private class SetDiscardPointsCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;
		private readonly SqliteParameter _discardPointParam;
		private readonly SqliteParameter _maybeDiscardPointParam;
		private readonly SqliteParameter _statusParam;

		public SetDiscardPointsCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					INSERT INTO {tableName} (key, discardPoint, maybeDiscardPoint, status)
					VALUES ($key, $discardPoint, $maybeDiscardPoint, $status)
					ON CONFLICT(key) DO UPDATE SET
						discardPoint = $discardPoint,
						maybeDiscardPoint = $maybeDiscardPoint,
						status = $status";

			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_keyParam = _cmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_discardPointParam = _cmd.Parameters.Add("$discardPoint", SqliteType.Integer);
			_maybeDiscardPointParam = _cmd.Parameters.Add("$maybeDiscardPoint", SqliteType.Integer);
			_statusParam = _cmd.Parameters.Add("$status", SqliteType.Integer);
			_cmd.Prepare();

			_sqlite = sqlite;
		}

		public void Execute(TKey key, CalculationStatus status, DiscardPoint discardPoint, DiscardPoint maybeDiscardPoint) {
			_keyParam.Value = key;
			_discardPointParam.Value = discardPoint.FirstEventNumberToKeep;
			_maybeDiscardPointParam.Value = maybeDiscardPoint.FirstEventNumberToKeep;
			_statusParam.Value = status;
			_sqlite.ExecuteNonQuery(_cmd);
		}
	}

	private class GetCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;

		public GetCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					SELECT isTombstoned, maxAge, maxCount, truncateBefore, discardPoint, maybeDiscardPoint, status
					FROM {tableName}
					WHERE key = $key";

			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_keyParam = _cmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_cmd.Prepare();
			
			_sqlite = sqlite;
		}

		public bool TryExecute(TKey key, out OriginalStreamData value) {
			_keyParam.Value = key;
			return _sqlite.ExecuteSingleRead(_cmd, _readOriginalStreamData, out value);
		}
	}

	private class GetChunkExecutionInfoCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;
		private readonly Func<SqliteDataReader, ChunkExecutionInfo> _reader;

		public GetChunkExecutionInfoCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					SELECT isTombstoned, maxAge, discardPoint, maybeDiscardPoint
					FROM {tableName}
					WHERE key = $key";

			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_keyParam = _cmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_cmd.Prepare();
			
			_sqlite = sqlite;
			_reader = reader => {
				var isTombstoned = reader.GetBoolean(0);
				var maxAge = SqliteBackend.GetTimeSpanFromSeconds(1, reader);
				var discardPoint = DiscardPoint.DiscardBefore(reader.GetFieldValue<long>(2));
				var maybeDiscardPoint = DiscardPoint.DiscardBefore(reader.GetFieldValue<long>(3));
				return new ChunkExecutionInfo(isTombstoned, discardPoint, maybeDiscardPoint, maxAge);
			};
		}

		public bool TryExecute(TKey key, out ChunkExecutionInfo value) {
			_keyParam.Value = key;
			return _sqlite.ExecuteSingleRead(_cmd, _reader, out value);
		}
	}

	private class DeleteCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _selectCmd;
		private readonly SqliteCommand _deleteCmd;
		private readonly SqliteParameter _selectKeyParam;
		private readonly SqliteParameter _deleteKeyParam;

		public DeleteCommand(string tableName, SqliteBackend sqlite) {
			var selectSql = $@"
					SELECT isTombstoned, maxAge, maxCount, truncateBefore, discardPoint, maybeDiscardPoint, status
					FROM {tableName}
					WHERE key = $key";

			_selectCmd = sqlite.CreateCommand();
			_selectCmd.CommandText = selectSql;
			_selectKeyParam = _selectCmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_selectCmd.Prepare();

			var deleteSql = $"DELETE FROM {tableName} WHERE key = $key";
			_deleteCmd = sqlite.CreateCommand();
			_deleteCmd.CommandText = deleteSql;
			_deleteKeyParam = _deleteCmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_deleteCmd.Prepare();
			
			_sqlite = sqlite;
		}

		public bool TryExecute(TKey key, out OriginalStreamData value) {
			_selectKeyParam.Value = key;
			_deleteKeyParam.Value = key;
			return _sqlite.ExecuteReadAndDelete(_selectCmd, _deleteCmd, _readOriginalStreamData, out value);
		}
	}
	
	private class DeleteManyCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _deleteCmd;
		private readonly SqliteParameter _archiveStatusParam;

		public DeleteManyCommand(string tableName, SqliteBackend sqlite) {
			var deleteSql = $@"
					DELETE FROM {tableName}
					WHERE status = {(int)CalculationStatus.Spent} OR status = $archive";

			_deleteCmd = sqlite.CreateCommand();
			_deleteCmd.CommandText = deleteSql;
			_archiveStatusParam = _deleteCmd.Parameters.Add("$archive", SqliteType.Integer);
			_deleteCmd.Prepare();
			
			_sqlite = sqlite;
		}

		public void Execute(bool deleteArchived) {
			// only delete archived when requested, otherwise only delete Spent.
			_archiveStatusParam.Value = deleteArchived ? CalculationStatus.Archived : CalculationStatus.Spent;
			_sqlite.ExecuteNonQuery(_deleteCmd);
		}
	}

	// the sqlite docs (https://www.sqlite.org/lang_select.html) say that without an orderby clause
	// the order in which the rows are returned is undefined. so we include an orderby clause
	// in FromCheckpointCommand and ActiveRecordsCommand to facilitate checkpointing and continuing
	//
	// the sqlite docs (https://www.sqlite.org/isolation.html) say that on a single connection the
	// behaviour of running insert/update/delete during a select is safe, but undefined with respect
	// to whether the updates will appear in the select. in particular, updating the current row
	// may cause the row to reappear later in the select. we are ordering the select by key so in
	// our case it seems doubtful that it will reappear because it would violate the orderby clause,
	// but in the worse case we will just duplicate the effort but otherwise no harm is done.
	// NB we no longer have a read open while writing
	private class FromCheckpointCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;
		private readonly Func<SqliteDataReader, KeyValuePair<TKey, OriginalStreamData>> _reader;

		public FromCheckpointCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					SELECT isTombstoned, maxAge, maxCount, truncateBefore, discardPoint, maybeDiscardPoint, status, key
					FROM {tableName}
					WHERE key > $key AND status = {(int)CalculationStatus.Active}
					ORDER BY key";

			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_keyParam = _cmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_cmd.Prepare();
			
			_sqlite = sqlite;
			_reader = reader => {
				var value = _readOriginalStreamData(reader);
				var key = reader.GetFieldValue<TKey>(7);
				return new KeyValuePair<TKey, OriginalStreamData>(key, value);
			};
		}

		public IEnumerable<KeyValuePair<TKey, OriginalStreamData>> Execute(TKey key) {
			_keyParam.Value = key;
			return _sqlite.ExecuteReader(_cmd, _reader);
		}
	}

	private class AllRecordsCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly Func<SqliteDataReader, KeyValuePair<TKey, OriginalStreamData>> _reader;

		public AllRecordsCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					SELECT
						isTombstoned,
						maxAge,
						maxCount,
						truncateBefore,
						discardPoint,
						maybeDiscardPoint,
						status,
						key
					FROM {tableName}
					ORDER BY key";

			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_cmd.Prepare();
			
			_sqlite = sqlite;
			_reader = reader => {
				var data = _readOriginalStreamData(reader);
				var key = reader.GetFieldValue<TKey>(7); 
				return new KeyValuePair<TKey, OriginalStreamData>(key, data);
			};
		}

		public IEnumerable<KeyValuePair<TKey, OriginalStreamData>> Execute() {
			return _sqlite.ExecuteReader(_cmd, _reader);
		}
	}
	
	private class ActiveRecordsCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly Func<SqliteDataReader, KeyValuePair<TKey, OriginalStreamData>> _reader;

		public ActiveRecordsCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					SELECT
						isTombstoned,
						maxAge,
						maxCount,
						truncateBefore,
						discardPoint,
						maybeDiscardPoint,
						status,
						key
					FROM {tableName}
					WHERE status = {(int)CalculationStatus.Active}
					ORDER BY key";

			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_cmd.Prepare();
			
			_sqlite = sqlite;
			_reader = reader => {
				var data = _readOriginalStreamData(reader);
				var key = reader.GetFieldValue<TKey>(7); 
				return new KeyValuePair<TKey, OriginalStreamData>(key, data);
			};
		}

		public IEnumerable<KeyValuePair<TKey, OriginalStreamData>> Execute() {
			return _sqlite.ExecuteReader(_cmd, _reader);
		}
	}
}
