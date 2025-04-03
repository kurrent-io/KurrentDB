// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using Microsoft.Data.Sqlite;

namespace EventStore.Core.TransactionLog.Scavenging.Sqlite;

public class SqliteScavengeMap<TKey, TValue> :
	IInitializeSqliteBackend,
	IScavengeMap<TKey, TValue> {

	private AddCommand _add;
	private GetCommand _get;
	private RemoveCommand _delete;
	private AllRecordsCommand _all;

	private readonly Dictionary<Type, string> _sqliteTypeMap = new Dictionary<Type, string>() {
		{typeof(int), nameof(SqliteType.Integer)},
		{typeof(float), nameof(SqliteType.Real)},
		{typeof(ulong), nameof(SqliteType.Integer)},
		{typeof(string), nameof(SqliteType.Text)},
	};
	private readonly string _keyTypeOverride;

	protected string TableName { get; }
	
	public SqliteScavengeMap(string name, string keyTypeOverride = null) {
		TableName = name;
		_keyTypeOverride = keyTypeOverride;
		AssertTypesAreSupported();
	}

	private void AssertTypesAreSupported() {
		if (!IsSupportedType<TKey>()) {
			throw new ArgumentException(
				$"Scavenge map {TableName} has an unsupported type {typeof(TKey).Name} for key specified");
		}
		
		if (!IsSupportedType<TValue>()) {
			throw new ArgumentException(
				$"Scavenge map {TableName} has an unsupported type {typeof(TValue).Name} for value specified");
		}
	}

	private bool IsSupportedType<T>() {
		return _sqliteTypeMap.ContainsKey(typeof(T));
	}

	public virtual void Initialize(SqliteBackend sqlite) {
		var keyType = _keyTypeOverride ?? SqliteTypeMapping.GetTypeName<TKey>();
		var valueType = SqliteTypeMapping.GetTypeName<TValue>();
		var createSql = $@"
				CREATE TABLE IF NOT EXISTS {TableName} (
					key {keyType} PRIMARY KEY,
					value {valueType} NOT NULL)";

		sqlite.InitializeDb(createSql);

		_add = new AddCommand(TableName, sqlite);
		_get = new GetCommand(TableName, sqlite);
		_delete = new RemoveCommand(TableName, sqlite);
		_all = new AllRecordsCommand(TableName, sqlite);
	}
	
	public TValue this[TKey key] {
		set => AddValue(key, value);
	}

	private void AddValue(TKey key, TValue value) {
		_add.Execute(key, value);
	}

	public bool TryGetValue(TKey key, out TValue value) {
		return _get.TryExecute(key, out value);
	}

	public bool TryRemove(TKey key, out TValue value) {
		return _delete.TryExecute(key, out value);
	}

	public IEnumerable<KeyValuePair<TKey, TValue>> AllRecords() {
		return _all.Execute();
	}

	private class AddCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;
		private readonly SqliteParameter _valueParam;

		public AddCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					INSERT INTO {tableName}
					VALUES($key, $value)
					ON CONFLICT(key) DO UPDATE SET value=$value";
			
			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_keyParam = _cmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_valueParam = _cmd.Parameters.Add("$value", SqliteTypeMapping.Map<TKey>());
			_cmd.Prepare();
			
			_sqlite = sqlite;
		}

		public void Execute(TKey key, TValue value) {
			_keyParam.Value = key;
			_valueParam.Value = value;
			_sqlite.ExecuteNonQuery(_cmd);
		}
	}

	private class GetCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;
		private readonly Func<SqliteDataReader, TValue> _reader;

		public GetCommand(string tableName, SqliteBackend sqlite) {
			var selectSql = $@"
					SELECT value
					FROM {tableName}
					WHERE key = $key";
			
			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = selectSql;
			_keyParam = _cmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_cmd.Prepare();
			
			_sqlite = sqlite;
			_reader = reader => reader.GetFieldValue<TValue>(0);
		}

		public bool TryExecute(TKey key, out TValue value) {
			_keyParam.Value = key;
			return _sqlite.ExecuteSingleRead(_cmd, _reader, out value);
		}
	}

	private class RemoveCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _selectCmd;
		private readonly SqliteCommand _deleteCmd;
		private readonly SqliteParameter _selectKeyParam;
		private readonly SqliteParameter _deleteKeyParam;
		private readonly Func<SqliteDataReader, TValue> _reader;

		public RemoveCommand(string tableName, SqliteBackend sqlite) {
			var selectSql = $@"
					SELECT value
					FROM {tableName}
					WHERE key = $key";
			
			_selectCmd = sqlite.CreateCommand();
			_selectCmd.CommandText = selectSql;
			_selectKeyParam = _selectCmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_selectCmd.Prepare();

			var deleteSql = $@"
					DELETE FROM {tableName}
					WHERE key = $key";
			
			_deleteCmd = sqlite.CreateCommand();
			_deleteCmd.CommandText = deleteSql;
			_deleteKeyParam = _deleteCmd.Parameters.Add("$key", SqliteTypeMapping.Map<TKey>());
			_deleteCmd.Prepare();
			
			_sqlite = sqlite;
			_reader = reader => reader.GetFieldValue<TValue>(0);
		}

		public bool TryExecute(TKey key, out TValue value) {
			_selectKeyParam.Value = key;
			_deleteKeyParam.Value = key;
			return _sqlite.ExecuteReadAndDelete(_selectCmd, _deleteCmd, _reader, out value);
		}
	}

	private class AllRecordsCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly Func<SqliteDataReader, KeyValuePair<TKey, TValue>> _reader;

		public AllRecordsCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					SELECT key, value
					FROM {tableName}
					ORDER BY key";
			
			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_cmd.Prepare();
			
			_sqlite = sqlite;
			_reader = reader => new KeyValuePair<TKey, TValue>(
				reader.GetFieldValue<TKey>(0), reader.GetFieldValue<TValue>(1));
		}

		public IEnumerable<KeyValuePair<TKey, TValue>> Execute() {
			return _sqlite.ExecuteReader(_cmd, _reader);
		}
	}
}
