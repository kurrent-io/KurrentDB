// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Linq;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace EventStore.Core.XUnit.Tests.Scavenge.Sqlite;

public class SqliteScavengeBackendTests : SqliteDbPerTest<SqliteScavengeBackendTests>  {
	private readonly ITestOutputHelper _testOutputHelper;

	public SqliteScavengeBackendTests(ITestOutputHelper testOutputHelper) {
		_testOutputHelper = testOutputHelper;
	}

	[Fact]
	public void should_successfully_enable_features_on_initialization() {
		var sut = new SqliteScavengeBackend<string>(Serilog.Log.Logger);
		var result = Record.Exception(() => sut.Initialize(Fixture.DbConnection));
		Assert.Null(result);
	}
	
	[Fact]
	public void should_initialize_multiple_times_without_error() {
		var sut = new SqliteScavengeBackend<string>(Serilog.Log.Logger);
		var result = Record.Exception(() => sut.Initialize(Fixture.DbConnection));
		Assert.Null(result);
		
		result = Record.Exception(() => sut.Initialize(Fixture.DbConnection));
		Assert.Null(result);
	}

	[Fact]
	public void should_commit_and_read_in_a_transaction_successfully() {
		var sut = new SqliteScavengeBackend<string>(Serilog.Log.Logger);
		sut.Initialize(Fixture.DbConnection);
		
		var add = sut.TransactionFactory.Begin();
		sut.CheckpointStorage[Unit.Instance] = new ScavengeCheckpoint.ExecutingIndex(
			new ScavengePoint(1,1, DateTime.UtcNow, 1));
		sut.ChunkTimeStampRanges[0] = new ChunkTimeStampRange(DateTime.Now, DateTime.Now.AddMinutes(1));
		sut.ChunkWeights[0] = 1f;
		sut.CollisionStorage["collision-1"] = Unit.Instance;
		sut.Hashes[0] = "hash-one";
		sut.MetaCollisionStorage["collision-1"] = MetastreamData.Empty;
		sut.MetaStorage[0] = MetastreamData.Empty;
		sut.OriginalStorage[0] = new OriginalStreamData() {
			Status = CalculationStatus.Active,
			DiscardPoint = DiscardPoint.KeepAll,
			IsTombstoned = true,
			MaxAge = TimeSpan.FromSeconds(30),
			MaxCount = 10,
			TruncateBefore = 5,
			MaybeDiscardPoint = DiscardPoint.DiscardBefore(2)
		};
		sut.TransactionFactory.Commit(add);
		
		var read = sut.TransactionFactory.Begin();
		Assert.True(sut.CheckpointStorage.TryGetValue(Unit.Instance, out _));
		Assert.True(sut.ChunkTimeStampRanges.TryGetValue(0, out _));
		Assert.True(sut.ChunkWeights.TryGetValue(0, out _));
		Assert.True(sut.CollisionStorage.TryGetValue("collision-1", out _));
		Assert.True(sut.Hashes.TryGetValue(0, out _));
		Assert.True(sut.MetaCollisionStorage.TryGetValue("collision-1", out _));
		Assert.True(sut.MetaStorage.TryGetValue(0, out _));
		Assert.True(sut.OriginalStorage.TryGetValue(0, out _));
		sut.TransactionFactory.Commit(read);
	}

	[Fact]
	public void should_commit_and_read_all_in_a_transaction_successfully() {
		var sut = new SqliteScavengeBackend<string>(Serilog.Log.Logger);
		sut.Initialize(Fixture.DbConnection);
		
		var add = sut.TransactionFactory.Begin();
		sut.ChunkTimeStampRanges[0] = new ChunkTimeStampRange(DateTime.Now, DateTime.Now.AddMinutes(1));
		sut.ChunkWeights[0] = 1f;
		sut.CollisionStorage["collision-1"] = Unit.Instance;
		sut.Hashes[0] = "hash-one";
		sut.MetaCollisionStorage["collision-1"] = MetastreamData.Empty;
		sut.MetaStorage[0] = MetastreamData.Empty;
		sut.OriginalStorage[0] = new OriginalStreamData() {
			Status = CalculationStatus.Active,
			DiscardPoint = DiscardPoint.KeepAll,
			IsTombstoned = true,
			MaxAge = TimeSpan.FromSeconds(30),
			MaxCount = 10,
			TruncateBefore = 5,
			MaybeDiscardPoint = DiscardPoint.DiscardBefore(2)
		};
		sut.TransactionFactory.Commit(add);
		
		var read = sut.TransactionFactory.Begin();
		Assert.NotEmpty(sut.ChunkTimeStampRanges.AllRecords());
		Assert.NotEmpty(sut.ChunkWeights.AllRecords());
		Assert.NotEmpty(sut.CollisionStorage.AllRecords());
		Assert.NotEmpty(sut.Hashes.AllRecords());
		Assert.NotEmpty(sut.MetaCollisionStorage.AllRecords());
		Assert.NotEmpty(sut.MetaStorage.AllRecords());
		Assert.NotEmpty(sut.OriginalStorage.AllRecords());
		sut.TransactionFactory.Commit(read);
	}
	
	[Fact]
	public void should_commit_and_delete_in_a_transaction_successfully() {
		var sut = new SqliteScavengeBackend<string>(Serilog.Log.Logger);
		sut.Initialize(Fixture.DbConnection);
		
		var checkpoint = new ScavengeCheckpoint.ExecutingIndex(new ScavengePoint(1,1, DateTime.UtcNow, 1));
		
		var add = sut.TransactionFactory.Begin();
		sut.CheckpointStorage[Unit.Instance] = checkpoint;
		sut.ChunkTimeStampRanges[0] = new ChunkTimeStampRange(DateTime.Now, DateTime.Now.AddMinutes(1));
		sut.ChunkWeights[0] = 1f;
		sut.CollisionStorage["collision-1"] = Unit.Instance;
		sut.Hashes[0] = "hash-one";
		sut.MetaCollisionStorage["collision-1"] = MetastreamData.Empty;
		sut.MetaStorage[0] = MetastreamData.Empty;
		sut.OriginalStorage[0] = new OriginalStreamData() {
			Status = CalculationStatus.Active,
			DiscardPoint = DiscardPoint.KeepAll,
			IsTombstoned = true,
			MaxAge = TimeSpan.FromSeconds(30),
			MaxCount = 10,
			TruncateBefore = 5,
			MaybeDiscardPoint = DiscardPoint.DiscardBefore(2)
		};
		sut.TransactionFactory.Commit(add);
		
		var delete = sut.TransactionFactory.Begin();
		Assert.True(sut.CheckpointStorage.TryRemove(Unit.Instance, out _));
		Assert.True(sut.ChunkTimeStampRanges.TryRemove(0, out _));
		Assert.True(sut.ChunkWeights.TryRemove(0, out _));
		Assert.True(sut.CollisionStorage.TryRemove("collision-1", out _));
		Assert.True(sut.Hashes.TryRemove(0, out _));
		Assert.True(sut.MetaCollisionStorage.TryRemove("collision-1", out _));
		Assert.True(sut.MetaStorage.TryRemove(0, out _));
		Assert.True(sut.OriginalStorage.TryRemove(0, out _));
		sut.TransactionFactory.Commit(delete);
	}
	
	[Fact]
	public void should_restore_previous_state_on_rollback() {
		var sut = new SqliteScavengeBackend<string>(Serilog.Log.Logger);
		sut.Initialize(Fixture.DbConnection);

		// Setup
		var tx1 = sut.TransactionFactory.Begin();
		sut.ChunkWeights[0] = 1f;
		sut.ChunkWeights[1] = 1f;
		sut.ChunkWeights[2] = 1f;
		
		sut.Hashes[0] = "hash-one";
		sut.Hashes[1] = "hash-two";
		sut.Hashes[2] = "hash-three";
		sut.TransactionFactory.Commit(tx1);
		
		// Rollback
		var tx2 = sut.TransactionFactory.Begin();
		sut.ChunkWeights[3] = 2f;
		sut.ChunkWeights[4] = 2f;
		
		sut.Hashes[3] = "hash-four";
		sut.Hashes[4] = "hash-five";
		
		Assert.Equal(5, sut.ChunkWeights.AllRecords().Count());
		Assert.Equal(5, sut.Hashes.AllRecords().Count());
		
		sut.TransactionFactory.Rollback(tx2);
		
		Assert.Equal(3, sut.ChunkWeights.AllRecords().Count());
		Assert.Equal(3, sut.Hashes.AllRecords().Count());
	}
	
	[Fact(Skip = "Long running, run manually")]
	public void test_memory_usage() {
		const ulong streamCount = 1_000_000;
		const int chunkCount = 100;
		const int collisionStorageCount = 5;
		const int cacheSizeInBytes = 4 * 1024 * 1024;

		var sut = new SqliteScavengeBackend<string>(Serilog.Log.Logger, cacheSizeInBytes);
		sut.Initialize(Fixture.DbConnection);

		var stopwatch = new Stopwatch();
		stopwatch.Start();

		var transaction = sut.TransactionFactory.Begin();
		for (ulong i = 0; i < streamCount; i++) {
			var streamId = Guid.NewGuid().ToString();
			sut.Hashes[i] = streamId;
			sut.MetaStorage[i] = new MetastreamData(false, DiscardPoint.KeepAll);
			sut.OriginalStorage[i] = new OriginalStreamData() {
				Status = CalculationStatus.Active,
				DiscardPoint = DiscardPoint.KeepAll,
				IsTombstoned = false,
				MaxAge = TimeSpan.FromHours(1),
				MaxCount = 10,
				MaybeDiscardPoint = DiscardPoint.KeepAll,
				TruncateBefore = 15
			};
		}

		for (int i = 0; i < chunkCount; i++) {
			sut.ChunkWeights[i] = 0.5f;
			sut.ChunkTimeStampRanges[i] = new ChunkTimeStampRange(DateTime.Today, DateTime.Today.AddHours(1*i));
		}

		for (int i = 0; i < collisionStorageCount; i++) {
			sut.CollisionStorage[$"stream-collision-{i}"] = Unit.Instance;
			sut.MetaCollisionStorage[$"meta-stream-collision-{i}"] = new MetastreamData(isTombstoned: true, DiscardPoint.KeepAll);
			sut.OriginalCollisionStorage[$"stream-collision-{i}"] = new OriginalStreamData() {
				Status = CalculationStatus.Archived,
				DiscardPoint = DiscardPoint.KeepAll,
				IsTombstoned = false,
				MaxAge = TimeSpan.FromHours(1),
				MaxCount = 10,
				MaybeDiscardPoint = DiscardPoint.KeepAll,
				TruncateBefore = 15
			};
		}
		
		sut.TransactionFactory.Commit(transaction);
		stopwatch.Stop();
		
		var stats = sut.GetStats();
		_testOutputHelper.WriteLine(
			$"SQLite Memory Usage: {stats.MemoryUsage:N0} " +
			$"Db Size: {stats.DatabaseSize:N0} " +
			$"Cache Size: {stats.CacheSize:N0} for {streamCount:N0} streams in {stopwatch.Elapsed}");
	}
	
	[Fact (Skip = "Long running, run manually")]
	public void test_index_with_archived() {
		const ulong archivedCount = 50_000_000;
		const ulong streamCount = 1_000_000;
		const int chunkCount = 100;
		const int collisionStorageCount = 5;
		const int cacheSizeInBytes = 4 * 1024 * 1024;

		var sut = new SqliteScavengeBackend<string>(Serilog.Log.Logger, cacheSizeInBytes);
		sut.Initialize(Fixture.DbConnection);

		var insert = new Stopwatch();
		insert.Start();

		var transaction = sut.TransactionFactory.Begin();
		for (ulong i = 0; i < archivedCount + streamCount; i++) {
			var streamId = Guid.NewGuid().ToString();
			sut.Hashes[i] = streamId;
			sut.MetaStorage[i] = new MetastreamData(false, DiscardPoint.KeepAll);
			sut.OriginalStorage[i] = new OriginalStreamData() {
				Status = i < archivedCount ? CalculationStatus.Archived : CalculationStatus.Active,
				DiscardPoint = DiscardPoint.KeepAll,
				IsTombstoned = false,
				MaxAge = TimeSpan.FromHours(1),
				MaxCount = 10,
				MaybeDiscardPoint = DiscardPoint.KeepAll,
				TruncateBefore = 15
			};
		}

		for (int i = 0; i < chunkCount; i++) {
			sut.ChunkWeights[i] = 0.5f;
			sut.ChunkTimeStampRanges[i] = new ChunkTimeStampRange(DateTime.Today, DateTime.Today.AddHours(1*i));
		}

		for (int i = 0; i < collisionStorageCount; i++) {
			sut.CollisionStorage[$"stream-collision-{i}"] = Unit.Instance;
			sut.MetaCollisionStorage[$"meta-stream-collision-{i}"] = new MetastreamData(isTombstoned: true, DiscardPoint.KeepAll);
			sut.OriginalCollisionStorage[$"stream-collision-{i}"] = new OriginalStreamData() {
				Status = CalculationStatus.Archived,
				DiscardPoint = DiscardPoint.KeepAll,
				IsTombstoned = false,
				MaxAge = TimeSpan.FromHours(1),
				MaxCount = 10,
				MaybeDiscardPoint = DiscardPoint.KeepAll,
				TruncateBefore = 15
			};
		}
		
		sut.TransactionFactory.Commit(transaction);
		insert.Stop();

		var readStopwatch = new Stopwatch();
		readStopwatch.Start();
		ulong k = 0;
		foreach (var active in sut.OriginalStorage.ActiveRecordsFromCheckpoint(1_000_000)) {
			k += active.Key;
		}
		readStopwatch.Stop();

		var stats = sut.GetStats();
		_testOutputHelper.WriteLine(
			$"Read all active records from checkpoint in: {readStopwatch.Elapsed} " +
			$"with {archivedCount:N0} archived & {streamCount:N0} active, inserted in {insert.Elapsed}. " +
			$"DB Size: {stats.DatabaseSize:N0}");
	}
}
