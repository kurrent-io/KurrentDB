// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using Microsoft.Data.Sqlite;

namespace EventStore.Core.TransactionLog.Scavenging.Sqlite;

public class SqliteChunkTimeStampRangeScavengeMap : IInitializeSqliteBackend, IScavengeMap<int, ChunkTimeStampRange> {
	private AddCommand _add;
	private GetCommand _get;
	private DeleteCommand _delete;
	private AllRecordsCommand _all;
	private static Func<SqliteDataReader, ChunkTimeStampRange> _readChunkTimeStampRange;

	private const string TableName = "ChunkTimeStampRanges";

	public SqliteChunkTimeStampRangeScavengeMap() {
		_readChunkTimeStampRange = reader => {
			var min = new DateTime(reader.GetInt64(0), DateTimeKind.Utc);
			var max = new DateTime(reader.GetInt64(1), DateTimeKind.Utc);
			return new ChunkTimeStampRange(min, max);
		};
	}

	public void Initialize(SqliteBackend sqlite) {
		var sql = $@"
				CREATE TABLE IF NOT EXISTS {TableName} (
					key INTEGER PRIMARY KEY,
					min INTEGER,
					max INTEGER)";
	
		sqlite.InitializeDb(sql);

		_add = new AddCommand(TableName, sqlite);
		_get = new GetCommand(TableName, sqlite);
		_delete = new DeleteCommand(TableName, sqlite);
		_all = new AllRecordsCommand(TableName, sqlite);
	}

	public ChunkTimeStampRange this[int key] {
		set => _add.Execute(key, value);
	}

	public bool TryGetValue(int key, out ChunkTimeStampRange value) {
		return _get.TryExecute(key, out value);
	}

	public bool TryRemove(int key, out ChunkTimeStampRange value) {
		return _delete.TryExecute(key, out value);
	}

	public IEnumerable<KeyValuePair<int, ChunkTimeStampRange>> AllRecords() {
		return _all.Execute();
	}

	private class AddCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;
		private readonly SqliteParameter _minParam;
		private readonly SqliteParameter _maxParam;

		public AddCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					INSERT INTO {tableName}
					VALUES($key, $min, $max)
					ON CONFLICT(key) DO UPDATE SET min=$min, max=$max";

			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_keyParam = _cmd.Parameters.Add("$key", SqliteType.Integer);
			_minParam = _cmd.Parameters.Add("$min", SqliteType.Integer);
			_maxParam = _cmd.Parameters.Add("$max", SqliteType.Integer);
			_cmd.Prepare();

			_sqlite = sqlite;
		}

		public void Execute(int key, ChunkTimeStampRange value) {
			_keyParam.Value = key;
			_minParam.Value = value.Min.Ticks;
			_maxParam.Value = value.Max.Ticks;
			_sqlite.ExecuteNonQuery(_cmd);
		}
	}

	private class GetCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;

		public GetCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					SELECT min, max
					FROM {tableName}
					WHERE key = $key";
			
			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_keyParam = _cmd.Parameters.Add("$key", SqliteType.Integer);
			_cmd.Prepare();
			
			_sqlite = sqlite;
		}

		public bool TryExecute(int key, out ChunkTimeStampRange value) {
			_keyParam.Value = key;
			return _sqlite.ExecuteSingleRead(_cmd, _readChunkTimeStampRange, out value);
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
					SELECT min, max
					FROM {tableName}
					WHERE key = $key";
			
			_selectCmd = sqlite.CreateCommand();
			_selectCmd.CommandText = selectSql;
			_selectKeyParam = _selectCmd.Parameters.Add("$key", SqliteType.Integer);
			_selectCmd.Prepare();

			var deleteSql = $@"
					DELETE FROM {tableName}
					WHERE key = $key";
			
			_deleteCmd = sqlite.CreateCommand();
			_deleteCmd.CommandText = deleteSql;
			_deleteKeyParam = _deleteCmd.Parameters.Add("$key", SqliteType.Integer);
			_deleteCmd.Prepare();
			
			_sqlite = sqlite;
		}

		public bool TryExecute(int key, out ChunkTimeStampRange value) {
			_selectKeyParam.Value = key;
			_deleteKeyParam.Value = key;
			return _sqlite.ExecuteReadAndDelete(_selectCmd, _deleteCmd, _readChunkTimeStampRange, out value);
		}
	}

	private class AllRecordsCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly Func<SqliteDataReader, KeyValuePair<int, ChunkTimeStampRange>> _reader;

		public AllRecordsCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					SELECT min, max, key
					FROM {tableName}
					ORDER BY key";
			
			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_cmd.Prepare();
			
			_sqlite = sqlite;

			_reader = reader => {
				var chunkTimeStampRange = _readChunkTimeStampRange(reader);
				var key = reader.GetFieldValue<int>(2);
				return new KeyValuePair<int, ChunkTimeStampRange>(key, chunkTimeStampRange);
			};
		}

		public IEnumerable<KeyValuePair<int, ChunkTimeStampRange>> Execute() {
			return _sqlite.ExecuteReader(_cmd, _reader);
		}
	}
}
