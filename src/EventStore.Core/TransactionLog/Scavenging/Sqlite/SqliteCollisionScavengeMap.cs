// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using Microsoft.Data.Sqlite;

namespace EventStore.Core.TransactionLog.Scavenging.Sqlite;

public class SqliteCollisionScavengeMap<TKey>: IInitializeSqliteBackend, IScavengeMap<TKey, Unit> {
	private AddCommand _add;
	private GetCommand _get;
	private RemoveCommand _remove;
	private AllRecordsCommand _all;

	private const string TableName = "HashCollisions";

	public void Initialize(SqliteBackend sqlite) {
		var sql = $@"
				CREATE TABLE IF NOT EXISTS {TableName} (
					key {SqliteTypeMapping.GetTypeName<TKey>()} PRIMARY KEY)";
		
		sqlite.InitializeDb(sql);
		
		_add = new AddCommand(sqlite);
		_get = new GetCommand(sqlite);
		_all = new AllRecordsCommand(sqlite);
		_remove = new RemoveCommand(sqlite);
	}

	public Unit this[TKey key] {
		set => AddValue(key, value);
	}

	private void AddValue(TKey key, Unit _) {
		_add.Execute(key);
	}

	public bool TryGetValue(TKey key, out Unit value) {
		if (_get.TryExecute(key)) {
			value = Unit.Instance;
			return true;
		}

		return false;
	}

	public bool TryRemove(TKey key, out Unit value) {
		if (_remove.TryExecute(key)) {
			value = Unit.Instance;
			return true;
		}

		return false;
	}

	public IEnumerable<KeyValuePair<TKey, Unit>> AllRecords() {
		return _all.Execute();
	}

	private class AddCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;

		public AddCommand(SqliteBackend sqlite) {
			var sql = $@"
					INSERT INTO {TableName}
					VALUES($key)
					ON CONFLICT(key) DO UPDATE SET key=$key";
			
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
	
	private class GetCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;
		private readonly Func<SqliteDataReader, Unit> _reader;

		public GetCommand(SqliteBackend sqlite) {
			var sql = $@"
					SELECT key
					FROM {TableName}
					WHERE key = $key";
			
			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_keyParam = _cmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_cmd.Prepare();
			
			_sqlite = sqlite;
			_reader = reader => Unit.Instance;
		}

		public bool TryExecute(TKey key) {
			_keyParam.Value = key;
			return _sqlite.ExecuteSingleRead(_cmd, _reader, out _);
		}
	}
	private class RemoveCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _selectCmd;
		private readonly SqliteCommand _deleteCmd;
		private readonly SqliteParameter _selectKeyParam;
		private readonly SqliteParameter _deleteKeyParam;
		private readonly Func<SqliteDataReader, Unit> _reader;

		public RemoveCommand(SqliteBackend sqlite) {
			_sqlite = sqlite;
			_reader = reader => Unit.Instance;
			
			var selectSql = $@"
					SELECT key
					FROM {TableName}
					WHERE key = $key";
			
			_selectCmd = sqlite.CreateCommand();
			_selectCmd.CommandText = selectSql;
			_selectKeyParam = _selectCmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_selectCmd.Prepare();

			var deleteSql = $@"
					DELETE FROM {TableName}
					WHERE key = $key";
			
			_deleteCmd = sqlite.CreateCommand();
			_deleteCmd.CommandText = deleteSql;
			_deleteKeyParam = _deleteCmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_deleteCmd.Prepare();
		}

		public bool TryExecute(TKey key) {
			_selectKeyParam.Value = key;
			_deleteKeyParam.Value = key;
			return _sqlite.ExecuteReadAndDelete(_selectCmd, _deleteCmd, _reader, out _);
		}
	}
	
	private class AllRecordsCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly Func<SqliteDataReader, KeyValuePair<TKey, Unit>> _reader;

		public AllRecordsCommand(SqliteBackend sqlite) {
			var sql = $@"
					SELECT key
					FROM {TableName}
					ORDER BY key";

			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_cmd.Prepare();
			
			_sqlite = sqlite;
			_reader = reader => new KeyValuePair<TKey, Unit>(reader.GetFieldValue<TKey>(0), Unit.Instance);
		}

		public IEnumerable<KeyValuePair<TKey, Unit>> Execute() {
			return _sqlite.ExecuteReader(_cmd, _reader);
		}
	}
}
