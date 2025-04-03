// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using Microsoft.Data.Sqlite;

namespace EventStore.Core.TransactionLog.Scavenging.Sqlite;

public class SqliteChunkWeightScavengeMap : SqliteScavengeMap<int, float>, IChunkWeightScavengeMap {
	private IncreaseWeightCommand _increaseWeight;
	private AllWeightsAreZeroCommand _allWeightsAreZero;
	private SumChunkWeightsCommand _sumChunkWeights;
	private ResetChunkWeightsCommand _resetChunkWeights;

	private const string MapName = "ChunkWeights";

	public SqliteChunkWeightScavengeMap() : base(MapName) { }

	public override void Initialize(SqliteBackend sqlite) {
		base.Initialize(sqlite);

		_increaseWeight = new IncreaseWeightCommand(TableName, sqlite);
		_allWeightsAreZero = new AllWeightsAreZeroCommand(TableName, sqlite);
		_sumChunkWeights = new SumChunkWeightsCommand(TableName, sqlite);
		_resetChunkWeights = new ResetChunkWeightsCommand(TableName, sqlite);
	}

	public bool AllWeightsAreZero() {
		return _allWeightsAreZero.Execute();
	}

	public void IncreaseWeight(int logicalChunkNumber, float extraWeight) {
		_increaseWeight.Execute(logicalChunkNumber, extraWeight);
	}

	public float SumChunkWeights(int startLogicalChunkNumber, int endLogicalChunkNumber) {
		return _sumChunkWeights.Execute(startLogicalChunkNumber, endLogicalChunkNumber);
	}

	public void ResetChunkWeights(int startLogicalChunkNumber, int endLogicalChunkNumber) {
		_resetChunkWeights.Execute(startLogicalChunkNumber, endLogicalChunkNumber);
	}
	
	private class IncreaseWeightCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _keyParam;
		private readonly SqliteParameter _valueParam;

		public IncreaseWeightCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					INSERT INTO {tableName} (key, value)
					VALUES($key, $value)
				    ON CONFLICT(key) DO UPDATE SET value=value+$value";
			
			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_keyParam = _cmd.Parameters.Add("$key", SqliteType.Integer);
			_valueParam = _cmd.Parameters.Add("$value", SqliteType.Real);
			_cmd.Prepare();
			
			_sqlite = sqlite;
		}

		public void Execute(int logicalChunkNumber, float extraWeight) {
			_keyParam.Value = logicalChunkNumber;
			_valueParam.Value = extraWeight;
			_sqlite.ExecuteNonQuery(_cmd);
		}
	}

	// All weights are zero if there do not exist any rows with a nonzero weight
	private class AllWeightsAreZeroCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private Func<SqliteDataReader, bool> _reader;

		public AllWeightsAreZeroCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					SELECT EXISTS (
						SELECT 1
						FROM {tableName}
						WHERE value <> 0
					)";

			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_cmd.Prepare();

			_sqlite = sqlite;
			_reader = reader => reader.GetBoolean(0);
		}

		public bool Execute() {
			_sqlite.ExecuteSingleRead(_cmd, _reader, out var exists);
			return !exists;
		}
	}

	private class SumChunkWeightsCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _startParam;
		private readonly SqliteParameter _endParam;
		private Func<SqliteDataReader, float> _reader;

		public SumChunkWeightsCommand(string tableName, SqliteBackend sqlite) {
			var sql = $@"
					SELECT SUM(value)
					FROM {tableName}
					WHERE key BETWEEN $start AND $end";
			
			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_startParam = _cmd.Parameters.Add("$start", SqliteType.Integer);
			_endParam = _cmd.Parameters.Add("$end", SqliteType.Integer);
			_cmd.Prepare();
			
			_sqlite = sqlite;
			_reader = reader => reader.IsDBNull(0) ? 0 : reader.GetFloat(0);
		}

		public float Execute(int startLogicalChunkNumber, int endLogicalChunkNumber) {
			_startParam.Value = startLogicalChunkNumber;
			_endParam.Value = endLogicalChunkNumber;
			_sqlite.ExecuteSingleRead(_cmd, _reader, out var value);
			return value;
		}
	}
	
	private class ResetChunkWeightsCommand {
		private readonly SqliteBackend _sqlite;
		private readonly SqliteCommand _cmd;
		private readonly SqliteParameter _startParam;
		private readonly SqliteParameter _endParam;

		public ResetChunkWeightsCommand(string tableName, SqliteBackend sqlite) {
			var sql = $"DELETE FROM {tableName} WHERE key BETWEEN $start AND $end";
			_cmd = sqlite.CreateCommand();
			_cmd.CommandText = sql;
			_startParam = _cmd.Parameters.Add("$start", SqliteType.Integer);
			_endParam = _cmd.Parameters.Add("$end", SqliteType.Integer);
			_cmd.Prepare();
			
			_sqlite = sqlite;
		}

		public void Execute(int startLogicalChunkNumber, int endLogicalChunkNumber) {
			_startParam.Value = startLogicalChunkNumber;
			_endParam.Value = endLogicalChunkNumber;
			_sqlite.ExecuteNonQuery(_cmd);
		}
	}
}
