// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.Tests;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;
using EventStore.Core.XUnit.Tests.Scavenge.Sqlite;
using Xunit;
using static EventStore.Core.XUnit.Tests.Scavenge.Infrastructure.StreamMetadatas;

namespace EventStore.Core.XUnit.Tests.Scavenge;

public class TombstoneTests : SqliteDbPerTest<TombstoneTests> {
	[Fact]
	public async Task simple_tombstone() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "ab-1"),
					Rec.CommittedDelete(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(1),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task single_tombstone() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.CommittedDelete(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task tombstone_with_metadata() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(t++, "$$ab-1", metadata: MaxCount1),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"),
					Rec.CommittedDelete(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(state => {
				Assert.True(state.TryGetOriginalStreamData("ab-1", out _));
				Assert.False(state.TryGetMetastreamData("$$ab-1", out _));
			})
			.RunAsync(x => new[] {
				// when the stream is hard deleted we can get rid of _all_ the metadata too
				// do not keep the last metadata record
				x.Recs[0].KeepIndexes(3),
				x.Recs[1],
			});
	}

	[Fact]
	public async Task tombstone_with_meta_collision() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					// ab-1 has metadata but not tombstone
					Rec.Write(t++, "$$ab-1", metadata: MaxCount1),
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk(
					// ab-2 is tombstoned
					Rec.Write(t++, "ab-2"),
					Rec.Write(t++, "ab-2"),
					Rec.CommittedDelete(t++, "ab-2"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(0, 2), // "ab-1" keep last event and metadata
				x.Recs[1].KeepIndexes(2), // "ab-2" keeps tombstone
				x.Recs[2],
			});
	}

	[Fact]
	public async Task tombstone_in_metadata_stream_not_supported() {
		// eventstore refuses denies access to write such a tombstone in the first place,
		// including in ESv5
		var logger = new FakeTFScavengerLog();
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.CommittedDelete(t++, "$$ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithLogger(logger)
			.RunAsync();

		Assert.True(logger.Completed);
		Assert.Equal(EventStore.Core.TransactionLog.Chunks.ScavengeResult.Errored, logger.Result);
		Assert.Equal("Error while scavenging DB: Found Tombstone in metadata stream $$ab-1.", logger.Error);
	}

	[Fact]
	public async Task tombstone_in_transaction_not_supported() {
		// if we wanted to support this we would have to apply the tombstone at the point that it
		// gets committed. also chunkexecutor would have to be careful not to discard the tombstone
		// of a tombstoned stream even though it can discard pretty much everything in transactions
		// in that case
		var logger = new FakeTFScavengerLog();
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.TransSt(0, "ab-1"),
					Rec.Delete(0, "ab-1"))
				.Chunk(ScavengePointRec(1)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.WithLogger(logger)
			.RunAsync();

		Assert.True(logger.Completed);
		Assert.Equal(EventStore.Core.TransactionLog.Chunks.ScavengeResult.Errored, logger.Result);
		Assert.Equal("Error while scavenging DB: Found Tombstone in transaction in stream ab-1.", logger.Error);
	}

	[Fact]
	public async Task tombstone_stream_with_transaction() {
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					Rec.Write(0, "ab-1"),
					Rec.TransSt(1, "ab-1"), // <-- keep transaction start
					Rec.Prepare(1, "ab-1"),
					Rec.TransEnd(1, "ab-1"),
					Rec.Commit(1, "ab-1"), // <-- keep comit
					Rec.Write(2, "ab-1"),
					Rec.Write(3, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.CommittedDelete(4, "ab-1")) // <-- keep tombstone
				.Chunk(ScavengePointRec(5)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(
				x => new[] {
					x.Recs[0].KeepIndexes(1, 4, 7),
					x.Recs[1],
				},
				x => new[] {
					x.Recs[0].KeepIndexes(7),
					x.Recs[1],
				});
	}

	[Fact]
	public async Task tombstone_stream_with_transaction_and_unsafe_ignore_hard_deletes() {
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithUnsafeIgnoreHardDeletes(true)
			.WithDb(x => x
				.Chunk(
					Rec.Write(0, "ab-1"),
					Rec.TransSt(1, "ab-1"),
					Rec.Prepare(1, "ab-1"),
					Rec.TransEnd(1, "ab-1"),
					Rec.Commit(1, "ab-1"), // <-- keep commit only
					Rec.Write(2, "ab-1"),
					Rec.Write(3, "$$ab-1", "$metadata", metadata: MaxCount1),
					Rec.CommittedDelete(4, "ab-1"))
				.Chunk(ScavengePointRec(5)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.RunAsync(
				x => new[] {
					x.Recs[0].KeepIndexes(4),
					x.Recs[1],
				},
				x => new[] {
					x.Recs[0].KeepIndexes(),
					x.Recs[1],
				});
	}

	// see comments in EventCalculator.cs
	[Fact]
	public async Task int32max_tombstone_with_subsequent_events() {
		var t = 0;
		await new Scenario<LogFormat.V2, string>()
			.WithDbPath(Fixture.Directory)
			.WithDb(x => x
				.Chunk(
					// old data
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"),

					// int32max tombstone that has not been upgraded in the index
					Rec.Write(t++, "ab-1",
						eventType: "$streamDeleted",
						eventNumber: int.MaxValue,
						prepareFlags:
							PrepareFlags.StreamDelete |
							PrepareFlags.TransactionBegin |
							PrepareFlags.TransactionEnd |
							PrepareFlags.IsCommitted),

					// additional data
					Rec.Write(t++, "ab-1"),
					Rec.Write(t++, "ab-1"))
				.Chunk(ScavengePointRec(t++)))
			.WithState(x => x.WithConnectionPool(Fixture.DbConnectionPool))
			.AssertState(state => {
				// still counted as tombstoned by scavenge
				Assert.True(state.TryGetOriginalStreamData("ab-1", out var data));
				Assert.True(data.IsTombstoned);
			})
			.RunAsync(x => new[] {
				x.Recs[0].KeepIndexes(2, 3, 4), // keep the int32max tombstone
				x.Recs[1],
			});
	}
}
