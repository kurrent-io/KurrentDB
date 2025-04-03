// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using Microsoft.Data.Sqlite;

namespace EventStore.Core.TransactionLog.Scavenging.Sqlite;


public class SqliteMetastreamScavengeMap<TKey> : IInitializeSqliteBackend, IMetastreamScavengeMap<TKey> {
	private readonly string _keyTypeOverride;
	private AddCommand _add;
	private SetTombstoneCommand _setTombstone;
	private SetDiscardPointCommand _setDiscardPoint;
	private GetCommand _get;
	private DeleteCommand _delete;
	private DeleteAllCommand _deleteAll;
	private AllRecordsCommand _all;
	private static Func<SqliteDataReader, MetastreamData> _readMetastreamData;

	private string TableName { get; }

	public SqliteMetastreamScavengeMap(string name, string keyTypeOverride = null) {
		TableName = name;
		_keyTypeOverride = keyTypeOverride;
		_readMetastreamData = reader => {
			var isTombstoned = reader.GetBoolean(0);
			var discardPoint = DiscardPoint.KeepAll;
			var discardPointField = SqliteBackend.GetNullableFieldValue<long?>(1, reader);
			if (discardPointField.HasValue) {
				discardPoint = DiscardPoint.DiscardBefore(discardPointField.Value);
			}
		
			return new MetastreamData(isTombstoned, discardPoint);
		};
	}

	public void Initialize(SqliteBackend sqlite) {
		var keyType = _keyTypeOverride ?? SqliteTypeMapping.GetTypeName<TKey>();
		var sql = $@"
				CREATE TABLE IF NOT EXISTS {TableName} (
					key {keyType} PRIMARY KEY,
					isTombstoned INTEGER DEFAULT 0,
					discardPoint INTEGER NULL)";
	
		sqlite.InitializeDb(sql);

		_add = new AddCommand(TableName, sqlite);
		_setTombstone = new SetTombstoneCommand(TableName, sqlite);
		_setDiscardPoint = new SetDiscardPointCommand(TableName, sqlite);
		_get = new GetCommand(TableName, sqlite);
		_delete = new DeleteCommand(TableName, sqlite);
		_deleteAll = new DeleteAllCommand(TableName, sqlite);
		_all = new AllRecordsCommand(TableName, sqlite);
	}

	public MetastreamData this[TKey key] {
		set => _add.Execute(key, value);
	}

	public bool TryGetValue(TKey key, out MetastreamData value) {
		return _get.TryExecute(key, out value);
	}

	public bool TryRemove(TKey key, out MetastreamData value) {
		return _delete.TryExecute(key, out value);
	}

	public IEnumerable<KeyValuePair<TKey, MetastreamData>> AllRecords() {
		return _all.Execute();
	}

	public void SetTombstone(TKey key) {
		_setTombstone.Execute(key);
	}

	public void SetDiscardPoint(TKey key, DiscardPoint discardPoint) {
		_setDiscardPoint.Execute(key, discardPoint);
	}

	public void DeleteAll() {
		_deleteAll.Execute();
	}

	private class AddCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;
		private readonly SqliteParameter _isTombstonedParam;
		private readonly SqliteParameter _discardPointParam;

		public AddCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					INSERT INTO {tableName}
					VALUES($key, $isTombstoned, $discardPoint)
					ON CONFLICT(key) DO UPDATE SET
						isTombstoned=$isTombstoned,
						discardPoint=$discardPoint";

			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_keyParam = _cmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_isTombstonedParam = _cmd.Parameters.Add("$isTombstoned", SqliteType.Integer);
			_discardPointParam = _cmd.Parameters.Add("$discardPoint", SqliteType.Integer);
			_cmd.Prepare();

			_sqlite = sqlite;
		}

		public void Execute(TKey key, MetastreamData value) {
			_keyParam.Value = key;
			_isTombstonedParam.Value = value.IsTombstoned;
			_discardPointParam.Value = value.DiscardPoint.FirstEventNumberToKeep;
			_sqlite.ExecuteNonQuery(_cmd);
		}
	}
	
	private class SetTombstoneCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;

		public SetTombstoneCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					INSERT INTO {tableName} (key, isTombstoned)
					VALUES($key, 1) 
					ON CONFLICT(key) DO UPDATE SET isTombstoned=1";
			
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

	private class SetDiscardPointCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;
		private readonly SqliteParameter _discardPointParam;

		public SetDiscardPointCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					INSERT INTO {tableName} (key, discardPoint)
					VALUES ($key, $discardPoint)
					ON CONFLICT(key) DO UPDATE SET discardPoint = $discardPoint";

			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_keyParam = _cmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_discardPointParam = _cmd.Parameters.Add("$discardPoint", SqliteType.Integer);
			_cmd.Prepare();

			_sqlite = sqlite;
		}

		public void Execute(TKey key, DiscardPoint discardPoint) {
			_keyParam.Value = key;
			_discardPointParam.Value = discardPoint.FirstEventNumberToKeep;
			_sqlite.ExecuteNonQuery(_cmd);
		}
	}
	
	private class GetCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;

		public GetCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					SELECT isTombstoned, discardPoint
					FROM {tableName}
					WHERE key = $key";
			
			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_keyParam = _cmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_cmd.Prepare();
			
			_sqlite = sqlite;
		}

		public bool TryExecute(TKey key, out MetastreamData value) {
			_keyParam.Value = key;
			return _sqlite.ExecuteSingleRead(_cmd, _readMetastreamData, out value);
		}
	}

	private class DeleteCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _selectCmd;
		private readonly SqliteCommand _deleteCmd;
		private readonly SqliteParameter _selectKeyParam;
		private readonly SqliteParameter _deleteKeyParam;

		public DeleteCommand(string tableName, SqliteBackend sqlite) {
			var selectSql = $"SELECT isTombstoned, discardPoint FROM {tableName} WHERE key = $key";
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

		public bool TryExecute(TKey key, out MetastreamData value) {
			_selectKeyParam.Value = key;
			_deleteKeyParam.Value = key;
			return _sqlite.ExecuteReadAndDelete(_selectCmd, _deleteCmd, _readMetastreamData, out value);
		}
	}

	private class DeleteAllCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _deleteCmd;

		public DeleteAllCommand(string tableName, SqliteBackend sqlite) {
			// sqlite treats this efficiently (as a truncate) https://www.sqlite.org/lang_delete.html
			var deleteSql = $"DELETE FROM {tableName}";
			_deleteCmd = sqlite.CreateCommand();
			_deleteCmd.CommandText = deleteSql;
			_deleteCmd.Prepare();
			
			_sqlite = sqlite;
		}

		public void Execute() {
			_sqlite.ExecuteNonQuery(_deleteCmd);
		}
	}

	private class AllRecordsCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly Func<SqliteDataReader, KeyValuePair<TKey, MetastreamData>> _reader;

		public AllRecordsCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					SELECT isTombstoned, discardPoint, key
					FROM {tableName}
					ORDER BY key";
			
			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_cmd.Prepare();
			
			_sqlite = sqlite;
			_reader = reader => {
				var value = _readMetastreamData(reader);
				var key = reader.GetFieldValue<TKey>(2);
				return new KeyValuePair<TKey, MetastreamData>(key, value);
			};
		}

		public IEnumerable<KeyValuePair<TKey, MetastreamData>> Execute() {
			return _sqlite.ExecuteReader(_cmd, _reader);
		}
	}
}
