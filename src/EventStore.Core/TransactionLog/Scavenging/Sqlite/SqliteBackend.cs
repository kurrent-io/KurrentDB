// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Serilog;
using SQLitePCL;

namespace EventStore.Core.TransactionLog.Scavenging.Sqlite;

public class SqliteBackend : IDisposable {
	protected static readonly ILogger Log = Serilog.Log.ForContext<SqliteBackend>();
	private readonly SqliteConnection _connection;
	private SqliteTransaction _transaction;
	private const int SqliteDuplicateKeyError = 19;

	public const string CacheSize = "cache_size";
	public const string PageCount = "page_count";
	public const string PageSize = "page_size";
	public const string Synchronous = "synchronous";
	public const string JournalMode = "journal_mode";
	public const string WalAutocheckpoint = "wal_autocheckpoint";

	public SqliteBackend(SqliteConnection connection) {
		_connection = connection;
	}

	public void Dispose() {
		_connection.Close();
		_connection.Dispose();
	}

	public void InitializeDb(string createSql) {
		var createTableCmd = _connection.CreateCommand();
		createTableCmd.CommandText = createSql;
		createTableCmd.ExecuteNonQuery();
	}

	public SqliteCommand CreateCommand() {
		return _connection.CreateCommand();
	}

	public bool ExecuteReadAndDelete<TValue>(SqliteCommand selectCmd, SqliteCommand deleteCmd,
		Func<SqliteDataReader, TValue> getValue, out TValue value) {

		if (ExecuteSingleRead(selectCmd, getValue, out value)) {
			var affectedRows = ExecuteNonQuery(deleteCmd);
			
			if (affectedRows == 1) {
				return true;
			} 
			if (affectedRows > 1) {
				throw new SystemException("More values removed than expected!");
			}
		}

		value = default;
		return false;
	}

	public int ExecuteNonQuery(SqliteCommand cmd) {
		try {
			cmd.Transaction = _transaction;
			return cmd.ExecuteNonQuery();
		}
		catch (SqliteException e) when (e.SqliteErrorCode == SqliteDuplicateKeyError) {
			throw new ArgumentException();
		}
	}

	public bool ExecuteSingleRead<TValue>(SqliteCommand cmd, Func<SqliteDataReader, TValue> getValue, out TValue value) {
		cmd.Transaction = _transaction;
		using (var reader = cmd.ExecuteReader()) {
			if (reader.Read()) {
				value = getValue(reader);
				return true;
			}
		}
		
		value = default;
		return false;
	}

	public IEnumerable<KeyValuePair<TKey, TValue>> ExecuteReader<TKey, TValue>(SqliteCommand cmd,
		Func<SqliteDataReader, KeyValuePair<TKey, TValue>> toValue) {

		cmd.Transaction = _transaction;
		using (var reader = cmd.ExecuteReader()) {
			while (reader.Read()) {
				yield return toValue(reader);
			}
		}
	}

	public static T GetNullableFieldValue<T>(int ordinal, SqliteDataReader reader) {
		if (!reader.IsDBNull(ordinal)) {
			return reader.GetFieldValue<T>(ordinal);
		}

		return default;
	}

	public static TimeSpan? GetTimeSpanFromSeconds(int ordinal, SqliteDataReader reader) {
		if (!reader.IsDBNull(ordinal)) {
			return TimeSpan.FromSeconds(reader.GetFieldValue<long>(ordinal));
		}

		return null;
	}
	
	public SqliteTransaction BeginTransaction() {
		_transaction = _connection.BeginTransaction();
		return _transaction;
	}

	public void ClearTransaction() {
		_transaction = null;

#if MANUAL_SQLITE_CHECKPOINT
		// This forces the WAL to checkpoint and restart (or throws), which means all the pages in
		// the WAL are written into the database and the WAL continues to write from the beginning of
		// the file. Its purpose is to
		//    1. Demonstrate that this is possible - i.e. no readers or anything else preventing
		//       the wal from being restarted, which could, if consistent, result in a very large WAL
		//    2. Collect some data about how many pages needed to be moved
		//
		// We leave it off in production because
		//    1. We don't necessarily want to do this after every commit (although our commits are
		//       quite coarse-grained)
		//    2. It can fail (legitimately?) on rollback when a scavenge is cancelled. We'd perhaps
		//       only want to call this on successful commit.
		//    3. it doesn't reuse the command or stopwatch.
		try {
			var sw = Stopwatch.StartNew();
			using var cmd = _connection.CreateCommand();
			cmd.CommandText = "PRAGMA wal_checkpoint(RESTART)";
			using var reader = cmd.ExecuteReader();
			if (reader.Read()) {
				var blocked = reader.GetInt32(0);
				var pagesModified = reader.GetInt32(1);
				var pagesMoved = reader.GetInt32(2);
				Log.Debug(
					"Checkpointed and restarted the WAL in {elapsed}. blocked?: {blocked}. {pagesModified} pages modified. {pagesMoved} pages moved.",
					sw.Elapsed, blocked, pagesModified, pagesMoved);
			} else {
				var msg = "Could not read result when checkpointing and restarting the WAL";
				Log.Error(msg);
				throw new Exception(msg);
			}
		} catch (Exception ex) {
			Log.Error(ex, "Failed to checkpoint and restart the WAL");
			throw;
		}
#endif
	}

	public Stats GetStats() {
		var databaseSize = long.Parse(GetPragmaValue(PageSize)) * long.Parse(GetPragmaValue(PageCount));
		var cacheSize = SqliteCacheSize.FromPragmaValue(GetPragmaValue(CacheSize));
		return new Stats(raw.sqlite3_memory_used(), databaseSize, cacheSize.CacheSizeInBytes);
	}

	public void SetPragmaValue(string name, string value) {
		using (var cmd = _connection.CreateCommand()) {
			cmd.CommandText = $"PRAGMA {name}={value}";
			cmd.ExecuteNonQuery();
		}
	}
	
	public string GetPragmaValue(string name) {
		var cmd = _connection.CreateCommand();
		cmd.CommandText = "PRAGMA " + name;
		var result = cmd.ExecuteScalar();
		
		if (result != null) {
			return result.ToString();
		}

		throw new Exception("Unexpected pragma result!");
	}

	public void SetCacheSize(long cacheSizeInBytes) {
		// cache size in kibi bytes is passed as a negative value, otherwise it's amount of pages
		var cacheSize = new SqliteCacheSize(cacheSizeInBytes);
		SetPragmaValue(CacheSize, cacheSize.NegativeKibibytes.ToString());
		var currentCacheSize = long.Parse(GetPragmaValue(CacheSize));
		if (currentCacheSize != cacheSize.NegativeKibibytes) {
			throw new Exception($"Failed to configure cache size, unexpected value: {currentCacheSize}");
		}
	}
	
	public class Stats {
		public Stats(long memoryUsage, long databaseSize, long cacheSize) {
			MemoryUsage = memoryUsage;
			DatabaseSize = databaseSize;
			CacheSize = cacheSize;
		}

		public long MemoryUsage { get; }
		public long DatabaseSize { get; }
		public long CacheSize { get; }

		public string PrettyPrint() {
			var dbSizeMb = (float)DatabaseSize / 1_000_000;
			var cacheSizeMb = (float)CacheSize / 1_000_000;
			var memSizeMb = (float)MemoryUsage / 1_000_000;
			return
				$"ScavengeState size: {dbSizeMb:N2} MB. " +
				$"Cache size: {cacheSizeMb:N2} MB. " +
				$"Memory usage: {memSizeMb:N2} MB";
		}
	}

	readonly struct SqliteCacheSize {
		public SqliteCacheSize(long cacheSizeInBytes) {
			CacheSizeInBytes = cacheSizeInBytes;
		}

		public static SqliteCacheSize FromPragmaValue(string value) {
			var cacheSizeInKibiBytes = long.Parse(value);
			return new SqliteCacheSize(-1 * cacheSizeInKibiBytes * 1024);
		}

		public long CacheSizeInBytes { get; }
		public long NegativeKibibytes {
			get {
				var cacheSizeInKibiBytes = CacheSizeInBytes / 1024;
				return -1 * cacheSizeInKibiBytes;
			}
		}
	}
}
