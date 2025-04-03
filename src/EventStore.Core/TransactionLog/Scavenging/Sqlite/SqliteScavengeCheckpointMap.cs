// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using Microsoft.Data.Sqlite;

namespace EventStore.Core.TransactionLog.Scavenging.Sqlite;

public class SqliteScavengeCheckpointMap<TStreamId>: IInitializeSqliteBackend, IScavengeMap<Unit, ScavengeCheckpoint> {
	private AddCommand _add;
	private GetCommand _get;
	private RemoveCommand _remove;

	private const string TableName = "Checkpoint";

	public void Initialize(SqliteBackend sqlite) {
		var sql = $@"
				CREATE TABLE IF NOT EXISTS {TableName} (
					key Integer PRIMARY KEY,
					value Text NOT NULL)";
		
		sqlite.InitializeDb(sql);
		
		_add = new AddCommand(sqlite);
		_get = new GetCommand(sqlite);
		_remove = new RemoveCommand(sqlite);
	}

	public ScavengeCheckpoint this[Unit key] {
		set => AddValue(key, value);
	}

	private void AddValue(Unit _, ScavengeCheckpoint value) {
		_add.Execute(value);
	}

	public bool TryGetValue(Unit key, out ScavengeCheckpoint value) {
		return _get.TryExecute(out value);
	}

	public bool TryRemove(Unit key, out ScavengeCheckpoint value) {
		return _remove.TryExecute(out value);
	}

	public IEnumerable<KeyValuePair<Unit, ScavengeCheckpoint>> AllRecords() {
		throw new NotImplementedException();
	}

	private class AddCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _valueParam;

		public AddCommand(SqliteBackend sqlite) {
			var sql = $@"
					INSERT INTO {TableName}
					VALUES(0, $value)
					ON CONFLICT(key) DO UPDATE SET value=$value";
			
			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_valueParam = _cmd.Parameters.Add("$value", SqliteType.Text);
			_cmd.Prepare();
			
			_sqlite = sqlite;
		}

		public void Execute(ScavengeCheckpoint value) {
			_valueParam.Value = ScavengeCheckpointJsonPersistence<TStreamId>.Serialize(value);
			_sqlite.ExecuteNonQuery(_cmd);
		}
	}
	
	private class GetCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly Func<SqliteDataReader, ScavengeCheckpoint> _reader;
		
		public GetCommand(SqliteBackend sqlite) {
			var sql = $"SELECT value FROM {TableName} WHERE key = 0";
			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_cmd.Prepare();
			
			_sqlite = sqlite;
			_reader = reader => {
				var ok = ScavengeCheckpointJsonPersistence<TStreamId>.TryDeserialize(reader.GetString(0), out var v);
				// code executed here means a record was found, but deserialization might fail
				// which should be handled as false (no record found), return null to indicate this.
				return ok ? v : null;
			};
		}

		public bool TryExecute(out ScavengeCheckpoint value) {
			return _sqlite.ExecuteSingleRead(_cmd, _reader, out value) && value != null;
		}
	}
	
	private class RemoveCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _selectCmd;
		private readonly SqliteCommand _deleteCmd;
		private readonly Func<SqliteDataReader, ScavengeCheckpoint> _reader;

		public RemoveCommand(SqliteBackend sqlite) {
			_sqlite = sqlite;
			_reader = reader => {
				var ok = ScavengeCheckpointJsonPersistence<TStreamId>.TryDeserialize(reader.GetString(0), out var v);
				// code executed here means a record was found, but deserialization might fail
				// which should be handled as false (no record found), return null to indicate this.
				return ok ? v : null;
			};
			
			var selectSql = $"SELECT value FROM {TableName} WHERE key = 0";
			_selectCmd = sqlite.CreateCommand();
			_selectCmd.CommandText = selectSql;
			_selectCmd.Prepare();

			var deleteSql = $"DELETE FROM {TableName} WHERE key = 0";
			_deleteCmd = sqlite.CreateCommand();
			_deleteCmd.CommandText = deleteSql;
			_deleteCmd.Prepare();
		}

		public bool TryExecute(out ScavengeCheckpoint value) {
			return _sqlite.ExecuteReadAndDelete(_selectCmd, _deleteCmd, _reader, out value) && value != null;
		}
	}
}
