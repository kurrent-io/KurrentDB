// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;
using KurrentDB.Core.TransactionLog.Scavenging.Data;
using KurrentDB.Core.TransactionLog.Scavenging.Interfaces;
using Microsoft.Data.Sqlite;
using Serilog;

namespace KurrentDB.Core.TransactionLog.Scavenging.Sqlite;

// Encapsulates a connection to sqlite, complete with prepared statements and an API to access it
public class SqliteScavengeBackend<TStreamId> : IScavengeStateBackend<TStreamId> {
	// WAL with SYNCHRONOUS NORMAL means that
	//  - committing a transaction does not wait to it to flush to disk
	//  - which is nice and quick, but means in powerloss the last x transactions
	//    can be lost. the database will be in a valid state.
	//  - this is suitable for us because scavenge will continue from the last
	//    persisted checkpoint.
	private const string SqliteWalJournalMode = "wal";
	private const int SqliteNormalSynchronousValue = 1;
	private const int WalAutocheckpointDisabled = 0;

	private const int DefaultSqliteCacheSize = 2 * 1024 * 1024;
	private const int DefaultSqlitePageSize = 16 * 1024;
	private readonly ILogger _logger;
	private readonly int _pageSizeInBytes;
	private readonly long _cacheSizeInBytes;
	private SqliteBackend _sqliteBackend;

	private const int SchemaVersion = 1;

	public IScavengeMap<TStreamId, Unit> CollisionStorage { get; private set; }
	public IScavengeMap<ulong, TStreamId> Hashes { get; private set; }
	public IMetastreamScavengeMap<ulong> MetaStorage { get; private set; }
	public IMetastreamScavengeMap<TStreamId> MetaCollisionStorage { get; private set; }
	public IOriginalStreamScavengeMap<ulong> OriginalStorage { get; private set; }
	public IOriginalStreamScavengeMap<TStreamId> OriginalCollisionStorage { get; private set; }
	public IScavengeMap<Unit, ScavengeCheckpoint> CheckpointStorage { get; private set; }
	public IScavengeMap<int, ChunkTimeStampRange> ChunkTimeStampRanges { get; private set; }
	public IChunkWeightScavengeMap ChunkWeights { get; private set; }
	public ITransactionFactory<SqliteTransaction> TransactionFactory { get; private set; }
	public ITransactionManager TransactionManager { get; private set; }

	public SqliteScavengeBackend(
		ILogger logger,
		int pageSizeInBytes = DefaultSqlitePageSize,
		long cacheSizeInBytes = DefaultSqliteCacheSize) {
		Ensure.Positive(pageSizeInBytes, nameof(pageSizeInBytes));
		Ensure.Positive(cacheSizeInBytes, nameof(cacheSizeInBytes));
		_logger = logger;
		_pageSizeInBytes = pageSizeInBytes;
		_cacheSizeInBytes = cacheSizeInBytes;
	}

	public void Dispose() {
		_sqliteBackend.Dispose();
	}

	public void Initialize(SqliteConnection connection) {
		_sqliteBackend = new SqliteBackend(connection);

		ConfigureFeatures();
		InitializeSchemaVersion();

		var collisionStorage = new SqliteCollisionScavengeMap<TStreamId>();
		CollisionStorage = collisionStorage;

		var hashes = new SqliteScavengeMap<ulong, TStreamId>("HashUsers", "INT");
		Hashes = hashes;

		// passing "INT" could increase the speed of the accumulation when the database becomes large
		var metaStorage = new SqliteMetastreamScavengeMap<ulong>("MetastreamDatas"/*, "INT"*/);
		MetaStorage = metaStorage;

		var metaCollisionStorage = new SqliteMetastreamScavengeMap<TStreamId>("MetastreamDataCollisions");
		MetaCollisionStorage = metaCollisionStorage;

		// passing "INT" could increase the speed of the accumulation when the database becomes large
		// but may slow down calculation and require adjustments to the iteration queries / indexes
		var originalStorage = new SqliteOriginalStreamScavengeMap<ulong>("OriginalStreamDatas"/*, "INT"*/);
		OriginalStorage = originalStorage;

		var originalCollisionStorage = new SqliteOriginalStreamScavengeMap<TStreamId>("OriginalStreamDataCollisions");
		OriginalCollisionStorage = originalCollisionStorage;

		var checkpointStorage = new SqliteScavengeCheckpointMap<TStreamId>();
		CheckpointStorage = checkpointStorage;

		var chunkTimeStampRanges = new SqliteChunkTimeStampRangeScavengeMap();
		ChunkTimeStampRanges = chunkTimeStampRanges;

		var chunkWeights = new SqliteChunkWeightScavengeMap();
		ChunkWeights = chunkWeights;

		var transactionFactory = new SqliteTransactionFactory();
		TransactionFactory = transactionFactory;

		TransactionManager = new SqliteTransactionManager(TransactionFactory, CheckpointStorage);

		var allMaps = new IInitializeSqliteBackend[] { collisionStorage, hashes, metaStorage, metaCollisionStorage,
			originalStorage, originalCollisionStorage, checkpointStorage, chunkTimeStampRanges, chunkWeights,
			transactionFactory};

		foreach (var map in allMaps) {
			map.Initialize(_sqliteBackend);
		}
	}

	private void ConfigureFeatures() {
		_logger.Debug("SCAVENGING: Setting page size to {pageSize:N0} bytes.", _pageSizeInBytes);
		_sqliteBackend.SetPragmaValue(SqliteBackend.PageSize, _pageSizeInBytes.ToString());
		var pageSize = int.Parse(_sqliteBackend.GetPragmaValue(SqliteBackend.PageSize));
		if (pageSize != _pageSizeInBytes) {
			// note we will fail to set it if the database already exists with a different page size
			// the page size could be changed if the vacuumed the database
			throw new Exception(
				$"Failed to configure page size to {_pageSizeInBytes}. Actually {pageSize}. " +
				$"This is probably a pre-existing database with a different page size.");
		}

		_sqliteBackend.SetPragmaValue(SqliteBackend.JournalMode, SqliteWalJournalMode);
		var journalMode = _sqliteBackend.GetPragmaValue(SqliteBackend.JournalMode);
		if (journalMode.ToLower() != SqliteWalJournalMode) {
			throw new Exception($"Failed to configure journal mode, unexpected value: {journalMode}");
		}

#if MANUAL_SQLITE_CHECKPOINT
		_sqliteBackend.SetPragmaValue(SqliteBackend.WalAutocheckpoint, WalAutocheckpointDisabled.ToString());
		var autocheckpoint = int.Parse(_sqliteBackend.GetPragmaValue(SqliteBackend.WalAutocheckpoint));
		if (autocheckpoint != WalAutocheckpointDisabled) {
			throw new Exception($"Failed to configure wal autocheckpoint, unexpected value: {autocheckpoint}");
		}
#endif

		_sqliteBackend.SetPragmaValue(SqliteBackend.Synchronous, SqliteNormalSynchronousValue.ToString());
		var synchronousMode = int.Parse(_sqliteBackend.GetPragmaValue(SqliteBackend.Synchronous));
		if (synchronousMode != SqliteNormalSynchronousValue) {
			throw new Exception($"Failed to configure synchronous mode, unexpected value: {synchronousMode}");
		}

		_sqliteBackend.SetCacheSize(_cacheSizeInBytes);
	}

	private void InitializeSchemaVersion() {
		var tableName = "SchemaVersion";

		using (var cmd = _sqliteBackend.CreateCommand()) {
			cmd.CommandText = $"CREATE TABLE IF NOT EXISTS {tableName} (version Integer PRIMARY KEY)";
			cmd.ExecuteNonQuery();

			cmd.CommandText = $"SELECT MAX(version) FROM {tableName}";
			var currentVersion = cmd.ExecuteScalar();

			if (currentVersion == DBNull.Value) {
				cmd.CommandText = $"INSERT INTO {tableName} VALUES({SchemaVersion})";
				cmd.ExecuteNonQuery();
			} else if (currentVersion != null && (long)currentVersion < SchemaVersion) {
				// need schema update
			}
		}
	}

	public SqliteBackend.Stats GetStats() {
		return _sqliteBackend.GetStats();
	}

	public void LogStats() {
		_logger.Debug($"SCAVENGING: {GetStats().PrettyPrint()}");
	}
}
